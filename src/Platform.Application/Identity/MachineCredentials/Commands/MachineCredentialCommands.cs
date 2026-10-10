using FluentValidation;
using Platform.Application.Abstractions.Audit;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Identity;
using Platform.Domain.Authorization;
using Platform.Domain.Identity;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Identity.MachineCredentials.Commands;

// Machine credential management (02 §6.3; intake LIS-INT-INT-001 M1–M3). The catalogue assigns the permission
// integration.manage (the pack's placeholder "integrations.manage"); step-up is configuration (02 §4.5) and not wired yet.

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Manage)]
public sealed record CreateMachineCredentialCommand(string Name, IReadOnlyList<string> Scopes, DateTimeOffset? ExpiresAtUtc) : ICommand<MachineCredentialSecretResponse>;

public sealed class CreateMachineCredentialValidator : AbstractValidator<CreateMachineCredentialCommand>
{
    public CreateMachineCredentialValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(MachineCredential.MaxNameLength);
        RuleFor(command => command.Scopes).NotNull().Must(scopes => scopes is { Count: >= 1 and <= MachineCredential.MaxScopes })
            .WithMessage($"Between 1 and {MachineCredential.MaxScopes} permission keys are required.");
        RuleForEach(command => command.Scopes).NotEmpty().MaximumLength(100);
    }
}

public sealed class CreateMachineCredentialHandler(
    IMachineCredentialRepository credentials,
    IMachineServiceIdentityStore identities,
    IMachineSecrets secrets,
    IUserPrivileges privileges,
    ICurrentTenant tenant,
    ICurrentUser user,
    ISecurityEventLog events,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<CreateMachineCredentialCommand, MachineCredentialSecretResponse>
{
    public async Task<MachineCredentialSecretResponse> Handle(CreateMachineCredentialCommand command, CancellationToken cancellationToken)
    {
        var tenantId = MachineCredentialIssuance.RequireTenant(tenant);
        var name = MachineCredential.CleanName(command.Name);
        MachineCredentialIssuance.EnsureGrantable(privileges, command.Scopes);   // SOD-INT-001
        if (await credentials.LiveNameExistsAsync(tenantId, name, cancellationToken))
        {
            throw new DomainException(MachineCredentialCodes.NameExists, $"A live machine credential named '{name}' already exists.", DomainFault.Conflict);
        }

        var issued = MachineCredentialIssuance.Issue(new MachineCredentialSpec(tenantId, name, command.Scopes.ToList(), null, command.ExpiresAtUtc), credentials, identities, secrets, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await events.WriteAsync(new SecurityEvent(MachineSecurityEvents.CredentialCreated, SecurityEvent.Info, tenantId, user.UserId, issued.Credential.ClientId), cancellationToken);
        return new MachineCredentialSecretResponse { Id = issued.Credential.Id, ClientId = issued.Credential.ClientId, ClientSecret = issued.Secret, ExpiresAtUtc = issued.Credential.ExpiresAtUtc };
    }
}

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Manage)]
public sealed record RotateMachineCredentialCommand(Guid CredentialId) : ICommand<MachineCredentialSecretResponse>;

public sealed class RotateMachineCredentialValidator : AbstractValidator<RotateMachineCredentialCommand>
{
    public RotateMachineCredentialValidator() => RuleFor(command => command.CredentialId).NotEmpty();
}

public sealed class RotateMachineCredentialHandler(
    IMachineCredentialRepository credentials,
    IRefreshSessionRepository sessions,
    ISessionPrincipalQuery principals,
    IMachineSecrets secrets,
    ICurrentTenant tenant,
    ICurrentUser user,
    ISecurityEventLog events,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<RotateMachineCredentialCommand, MachineCredentialSecretResponse>
{
    public async Task<MachineCredentialSecretResponse> Handle(RotateMachineCredentialCommand command, CancellationToken cancellationToken)
    {
        var tenantId = MachineCredentialIssuance.RequireTenant(tenant);
        var credential = await credentials.GetInTenantAsync(command.CredentialId, tenantId, cancellationToken)
            ?? throw new DomainException(MachineCredentialCodes.CredentialNotFound, "Machine credential not found.", DomainFault.NotFound);

        var now = clock.UtcNow;
        var secret = secrets.NewSecret();
        credential.RotateSecret(secret.Hash, now);   // MCR-010 unless live
        await MachineCredentialIssuance.EndSessionsAsync(credential, sessions, principals, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await events.WriteAsync(new SecurityEvent(MachineSecurityEvents.CredentialRotated, SecurityEvent.Info, tenantId, user.UserId, credential.ClientId), cancellationToken);
        return new MachineCredentialSecretResponse { Id = credential.Id, ClientId = credential.ClientId, ClientSecret = secret.Secret, ExpiresAtUtc = credential.ExpiresAtUtc };
    }
}

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Manage)]
public sealed record RevokeMachineCredentialCommand(Guid CredentialId, string? Reason) : ICommand;

public sealed class RevokeMachineCredentialValidator : AbstractValidator<RevokeMachineCredentialCommand>
{
    public RevokeMachineCredentialValidator()
    {
        RuleFor(command => command.CredentialId).NotEmpty();
        RuleFor(command => command.Reason).MaximumLength(MachineCredential.MaxReasonLength);
    }
}

public sealed class RevokeMachineCredentialHandler(
    IMachineCredentialRepository credentials,
    IPlatformUserRepository users,
    IRefreshSessionRepository sessions,
    ISessionPrincipalQuery principals,
    ICurrentTenant tenant,
    ICurrentUser user,
    ISecurityEventLog events,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<RevokeMachineCredentialCommand>
{
    public async Task Handle(RevokeMachineCredentialCommand command, CancellationToken cancellationToken)
    {
        var tenantId = MachineCredentialIssuance.RequireTenant(tenant);
        var actor = user.UserId ?? throw new DomainException(Contracts.Common.ProblemCodes.AuthenticatedActorRequired, "An authenticated user is required.", DomainFault.Unauthenticated);
        var credential = await credentials.GetInTenantAsync(command.CredentialId, tenantId, cancellationToken)
            ?? throw new DomainException(MachineCredentialCodes.CredentialNotFound, "Machine credential not found.", DomainFault.NotFound);

        var now = clock.UtcNow;
        var wasRevoked = credential.IsRevoked;
        credential.Revoke(actor, command.Reason, now);
        await MachineCredentialIssuance.EndServiceIdentityAsync(credential, users, sessions, principals, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!wasRevoked)
        {
            await events.WriteAsync(new SecurityEvent(MachineSecurityEvents.CredentialRevoked, SecurityEvent.Info, tenantId, actor, credential.ClientId), cancellationToken);
        }
    }
}
