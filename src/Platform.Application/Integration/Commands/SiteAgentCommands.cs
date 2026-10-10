using FluentValidation;
using Platform.Application.Abstractions.Audit;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Identity;
using Platform.Application.Identity.MachineCredentials;
using Platform.Contracts.Integration;
using Platform.Domain.Authorization;
using Platform.Domain.Identity;
using Platform.Domain.Integration;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Integration.Commands;

// Site agents (CP-61; intake rows G1–G5). An agent and its machine credential are created, rotated and ended in one
// commit: the credential issuance of the identity capability (02 §6.3) runs inside these handlers (intake §5).

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Manage)]
public sealed record CreateSiteAgentCommand(string Name, Guid BranchId, string? ConfigJson) : ICommand<SiteAgentSecretResponse>;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Manage)]
public sealed record UpdateSiteAgentCommand(Guid AgentId, string Name, string? ConfigJson) : ICommand;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Manage)]
public sealed record RotateSiteAgentSecretCommand(Guid AgentId) : ICommand<SiteAgentSecretResponse>;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Manage)]
public sealed record PushSiteAgentConfigCommand(Guid AgentId) : ICommand;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Manage)]
public sealed record DeactivateSiteAgentCommand(Guid AgentId) : ICommand;

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Manage)]
public sealed record ReactivateSiteAgentCommand(Guid AgentId) : ICommand<SiteAgentSecretResponse>;

public sealed class CreateSiteAgentValidator : AbstractValidator<CreateSiteAgentCommand>
{
    public CreateSiteAgentValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(SiteAgent.MaxNameLength);
        RuleFor(command => command.BranchId).NotEmpty();
        RuleFor(command => command.ConfigJson).MaximumLength(SiteAgent.MaxConfigLength);
    }
}

public sealed class UpdateSiteAgentValidator : AbstractValidator<UpdateSiteAgentCommand>
{
    public UpdateSiteAgentValidator()
    {
        RuleFor(command => command.AgentId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(SiteAgent.MaxNameLength);
        RuleFor(command => command.ConfigJson).MaximumLength(SiteAgent.MaxConfigLength);
    }
}

public sealed class RotateSiteAgentSecretValidator : AbstractValidator<RotateSiteAgentSecretCommand>
{
    public RotateSiteAgentSecretValidator() => RuleFor(command => command.AgentId).NotEmpty();
}

public sealed class PushSiteAgentConfigValidator : AbstractValidator<PushSiteAgentConfigCommand>
{
    public PushSiteAgentConfigValidator() => RuleFor(command => command.AgentId).NotEmpty();
}

public sealed class DeactivateSiteAgentValidator : AbstractValidator<DeactivateSiteAgentCommand>
{
    public DeactivateSiteAgentValidator() => RuleFor(command => command.AgentId).NotEmpty();
}

public sealed class ReactivateSiteAgentValidator : AbstractValidator<ReactivateSiteAgentCommand>
{
    public ReactivateSiteAgentValidator() => RuleFor(command => command.AgentId).NotEmpty();
}

/// <summary>The machine-credential side of the site-agent transitions; every method stages, the handler commits once.</summary>
internal static class AgentCredentials
{
    public static string CredentialName(string agentName) => $"Site agent: {SiteAgent.CleanName(agentName)}";

    public static async Task<SiteAgent> LoadAsync(ISiteAgentRepository agents, Guid agentId, CancellationToken cancellationToken) =>
        await agents.GetForUpdateAsync(agentId, cancellationToken)
        ?? throw new DomainException(IntegrationCodes.AgentNotFound, "Site agent not found.", DomainFault.NotFound);

    /// <summary>M1 for an agent: the fixed agent scopes, bound to the agent's branch (no privilege check: the scopes are the system's, not the actor's choice).</summary>
    public static IssuedMachineCredential Issue(Guid tenantId, string agentName, Guid branchId, IMachineCredentialRepository credentials, IMachineServiceIdentityStore identities, IMachineSecrets secrets, DateTimeOffset nowUtc) =>
        MachineCredentialIssuance.Issue(new MachineCredentialSpec(tenantId, CredentialName(agentName), AgentScopes.All.ToList(), branchId, null), credentials, identities, secrets, nowUtc);

    public static SiteAgentSecretResponse Response(Guid agentId, MachineCredential credential, string secret) =>
        new() { AgentId = agentId, CredentialId = credential.Id, ClientId = credential.ClientId, ClientSecret = secret, ExpiresAtUtc = credential.ExpiresAtUtc };
}

public sealed class CreateSiteAgentHandler(
    ISiteAgentRepository agents,
    IIntegrationReferenceLookup lookup,
    IMachineCredentialRepository credentials,
    IMachineServiceIdentityStore identities,
    IMachineSecrets secrets,
    ICurrentTenant tenant,
    ICurrentUser user,
    ISecurityEventLog events,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<CreateSiteAgentCommand, SiteAgentSecretResponse>
{
    public async Task<SiteAgentSecretResponse> Handle(CreateSiteAgentCommand command, CancellationToken cancellationToken)
    {
        var tenantId = MachineCredentialIssuance.RequireTenant(tenant);
        var name = SiteAgent.CleanName(command.Name);
        if (await agents.NameExistsAsync(name, null, cancellationToken) || await credentials.LiveNameExistsAsync(tenantId, AgentCredentials.CredentialName(name), cancellationToken))
        {
            throw new DomainException(IntegrationCodes.AgentNameExists, $"A site agent named '{name}' already exists.", DomainFault.Conflict);
        }

        await IntegrationReferences.RequireAsync(lookup, ReferenceKind.Branch, command.BranchId, cancellationToken);
        var now = clock.UtcNow;
        var issued = AgentCredentials.Issue(tenantId, name, command.BranchId, credentials, identities, secrets, now);
        var agent = SiteAgent.Register(name, command.BranchId, issued.Credential.Id, issued.Credential.ServiceUserId, command.ConfigJson, now);
        agents.Add(agent);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await events.WriteAsync(new SecurityEvent(MachineSecurityEvents.CredentialCreated, SecurityEvent.Info, tenantId, user.UserId, issued.Credential.ClientId), cancellationToken);
        return AgentCredentials.Response(agent.Id, issued.Credential, issued.Secret);
    }
}

public sealed class UpdateSiteAgentHandler(ISiteAgentRepository agents, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<UpdateSiteAgentCommand>
{
    public async Task Handle(UpdateSiteAgentCommand command, CancellationToken cancellationToken)
    {
        var agent = await AgentCredentials.LoadAsync(agents, command.AgentId, cancellationToken);
        var name = SiteAgent.CleanName(command.Name);
        if (await agents.NameExistsAsync(name, agent.Id, cancellationToken))
        {
            throw new DomainException(IntegrationCodes.AgentNameExists, $"A site agent named '{name}' already exists.", DomainFault.Conflict);
        }

        agent.Rename(name);
        agent.Configure(command.ConfigJson, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>G2: a live credential gets a new secret (its sessions end); a credential that is no longer live is replaced by a new one (new client id).</summary>
public sealed class RotateSiteAgentSecretHandler(
    ISiteAgentRepository agents,
    IMachineCredentialRepository credentials,
    IMachineServiceIdentityStore identities,
    IMachineSecrets secrets,
    IPlatformUserRepository users,
    IRefreshSessionRepository sessions,
    ISessionPrincipalQuery principals,
    ICurrentTenant tenant,
    ICurrentUser user,
    ISecurityEventLog events,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<RotateSiteAgentSecretCommand, SiteAgentSecretResponse>
{
    public async Task<SiteAgentSecretResponse> Handle(RotateSiteAgentSecretCommand command, CancellationToken cancellationToken)
    {
        var tenantId = MachineCredentialIssuance.RequireTenant(tenant);
        var agent = await AgentCredentials.LoadAsync(agents, command.AgentId, cancellationToken);
        agent.EnsureActive();
        var now = clock.UtcNow;
        var current = await credentials.GetInTenantAsync(agent.MachineCredentialId, tenantId, cancellationToken);

        SiteAgentSecretResponse response;
        string eventName;
        if (current is not null && current.IsLiveAt(now))
        {
            var secret = secrets.NewSecret();
            current.RotateSecret(secret.Hash, now);
            await MachineCredentialIssuance.EndSessionsAsync(current, sessions, principals, now, cancellationToken);
            response = AgentCredentials.Response(agent.Id, current, secret.Secret);
            eventName = MachineSecurityEvents.CredentialRotated;
        }
        else
        {
            if (current is not null)
            {
                current.Revoke(user.UserId ?? Guid.Empty, "Replaced by a new credential of the site agent.", now);
                await MachineCredentialIssuance.EndServiceIdentityAsync(current, users, sessions, principals, now, cancellationToken);
            }

            var issued = AgentCredentials.Issue(tenantId, agent.Name, agent.BranchId, credentials, identities, secrets, now);
            agent.ReplaceCredential(issued.Credential.Id, issued.Credential.ServiceUserId, now);
            response = AgentCredentials.Response(agent.Id, issued.Credential, issued.Secret);
            eventName = MachineSecurityEvents.CredentialCreated;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await events.WriteAsync(new SecurityEvent(eventName, SecurityEvent.Info, tenantId, user.UserId, response.ClientId), cancellationToken);
        return response;
    }
}

public sealed class PushSiteAgentConfigHandler(ISiteAgentRepository agents, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<PushSiteAgentConfigCommand>
{
    public async Task Handle(PushSiteAgentConfigCommand command, CancellationToken cancellationToken)
    {
        var agent = await AgentCredentials.LoadAsync(agents, command.AgentId, cancellationToken);
        agent.PushConfiguration(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>G4: the agent stops; its credential is revoked and its service identity ends in the same commit.</summary>
public sealed class DeactivateSiteAgentHandler(
    ISiteAgentRepository agents,
    IMachineCredentialRepository credentials,
    IPlatformUserRepository users,
    IRefreshSessionRepository sessions,
    ISessionPrincipalQuery principals,
    ICurrentTenant tenant,
    ICurrentUser user,
    ISecurityEventLog events,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<DeactivateSiteAgentCommand>
{
    public async Task Handle(DeactivateSiteAgentCommand command, CancellationToken cancellationToken)
    {
        var tenantId = MachineCredentialIssuance.RequireTenant(tenant);
        var agent = await AgentCredentials.LoadAsync(agents, command.AgentId, cancellationToken);
        var now = clock.UtcNow;
        var credential = await credentials.GetInTenantAsync(agent.MachineCredentialId, tenantId, cancellationToken);
        var revoke = credential is { IsRevoked: false };

        agent.Deactivate(now);
        if (credential is not null)
        {
            credential.Revoke(user.UserId ?? Guid.Empty, "Site agent deactivated.", now);
            await MachineCredentialIssuance.EndServiceIdentityAsync(credential, users, sessions, principals, now, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (revoke)
        {
            await events.WriteAsync(new SecurityEvent(MachineSecurityEvents.CredentialRevoked, SecurityEvent.Info, tenantId, user.UserId, credential!.ClientId), cancellationToken);
        }
    }
}

/// <summary>G5: the agent works again with a new credential (new client id and secret, shown once).</summary>
public sealed class ReactivateSiteAgentHandler(
    ISiteAgentRepository agents,
    IMachineCredentialRepository credentials,
    IMachineServiceIdentityStore identities,
    IMachineSecrets secrets,
    ICurrentTenant tenant,
    ICurrentUser user,
    ISecurityEventLog events,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<ReactivateSiteAgentCommand, SiteAgentSecretResponse>
{
    public async Task<SiteAgentSecretResponse> Handle(ReactivateSiteAgentCommand command, CancellationToken cancellationToken)
    {
        var tenantId = MachineCredentialIssuance.RequireTenant(tenant);
        var agent = await AgentCredentials.LoadAsync(agents, command.AgentId, cancellationToken);
        var now = clock.UtcNow;

        var issued = AgentCredentials.Issue(tenantId, agent.Name, agent.BranchId, credentials, identities, secrets, now);
        agent.Reactivate(issued.Credential.Id, issued.Credential.ServiceUserId, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await events.WriteAsync(new SecurityEvent(MachineSecurityEvents.CredentialCreated, SecurityEvent.Info, tenantId, user.UserId, issued.Credential.ClientId), cancellationToken);
        return AgentCredentials.Response(agent.Id, issued.Credential, issued.Secret);
    }
}
