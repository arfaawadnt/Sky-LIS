using FluentValidation;
using Platform.Application.Abstractions.Audit;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Identity;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.Identity;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin.Commands;

// SA-08 session termination and the laboratory's own wallet request (CP-66).

/// <summary>X1 Terminate a live session of a platform administrator (not the caller's own: PLT-010).</summary>
[RequirePermissionPolicy(PermissionCatalog.Platform, PermissionAction.Manage)]
public sealed record TerminateAdminSessionCommand(Guid SessionId) : ICommand, IPlatformAdministrationRequest;

public sealed class TerminateAdminSessionValidator : AbstractValidator<TerminateAdminSessionCommand>
{
    public TerminateAdminSessionValidator() => RuleFor(command => command.SessionId).NotEmpty();
}

public sealed class TerminateAdminSessionHandler(
    IRefreshSessionRepository sessions,
    ISessionPrincipalQuery principals,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    ISecurityEventLog events,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<TerminateAdminSessionCommand>
{
    public async Task Handle(TerminateAdminSessionCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var now = clock.UtcNow;
        var session = await sessions.GetForUpdateAsync(command.SessionId, cancellationToken);
        if (session is null || session.TenantId != TenantIds.Platform || !session.IsLiveAt(now))
        {
            throw new DomainException(IdentityCodes.SessionNotFound, "No live administrator session has this id.", DomainFault.NotFound);
        }

        if (currentUser.SessionId is { } own)
        {
            var mine = own == session.Id ? session : await sessions.GetForUpdateAsync(own, cancellationToken);
            if (mine is not null && mine.FamilyId == session.FamilyId)
            {
                throw new DomainException(PlatformAdminCodes.OwnSessionTermination, "Sign out to end your own session.", DomainFault.Refused);
            }
        }

        session.Revoke(RevocationReason.Admin, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        principals.Evict(session.Id);
        await events.WriteAsync(new SecurityEvent(PlatformAdminSecurityEvents.SessionTerminatedByAdmin, SecurityEvent.Warning, TenantIds.Platform, session.PlatformUserId, $"session:{session.Id}"), cancellationToken);
    }
}

/// <summary>W4 The laboratory asks the platform for credit (CP-66 "Request top-up"): client tenants only.</summary>
[RequirePermissionPolicy(PermissionCatalog.Wallet, PermissionAction.Create)]
public sealed record RequestWalletTopUpCommand(decimal Amount, string? Note) : ICommand<CreatedResource>;

public sealed class RequestWalletTopUpValidator : AbstractValidator<RequestWalletTopUpCommand>
{
    public RequestWalletTopUpValidator()
    {
        RuleFor(command => command.Amount).GreaterThan(0).LessThanOrEqualTo(100_000_000m);
        RuleFor(command => command.Note).MaximumLength(Wallet.MaxNoteLength);
    }
}

public sealed class RequestWalletTopUpHandler(IWalletRepository wallets, ICurrentTenant currentTenant, ICurrentUser currentUser, IUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<RequestWalletTopUpCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(RequestWalletTopUpCommand command, CancellationToken cancellationToken)
    {
        var tenantId = OwnWallet.TenantOf(currentTenant);
        var actor = currentUser.UserId ?? throw new DomainException(ProblemCodes.AuthenticatedActorRequired, "An authenticated user is required.", DomainFault.Unauthenticated);
        var wallet = await wallets.GetByTenantForUpdateAsync(tenantId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.WalletNotFound, "This laboratory has no wallet.", DomainFault.NotFound);

        var request = wallet.RequestTopUp(command.Amount, command.Note, actor, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(request.Id);
    }
}
