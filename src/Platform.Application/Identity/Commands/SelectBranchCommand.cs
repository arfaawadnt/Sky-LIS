using FluentValidation;
using Platform.Application.Abstractions.Audit;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Organization;
using Platform.Contracts.Common;
using Platform.Contracts.Identity;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Identity.Commands;

/// <summary>
/// CP-04 Branch selection: binds the current session to one of the membership's branches. The choice is checked
/// against the membership scope resolved per request (<c>TENANT-005</c> otherwise), stored on the session row so
/// every successor of the rotation family inherits it, and the cached principal is evicted so the next request sees it.
/// An unrestricted membership (no assigned branch) may select any <b>active</b> branch of the tenant (intake
/// LIS-INT-ORG-001 A1, resolving foundation question FND A1).
/// </summary>
[RequireAuthenticatedActor]
public sealed record SelectBranchCommand(Guid BranchId) : ICommand<AuthProfile>;

public sealed class SelectBranchCommandValidator : AbstractValidator<SelectBranchCommand>
{
    public SelectBranchCommandValidator()
    {
        RuleFor(command => command.BranchId).NotEmpty();
    }
}

public sealed class SelectBranchCommandHandler(
    IRefreshSessionRepository sessions,
    ISessionPrincipalQuery principals,
    IBranchLookup branches,
    ISecurityEventLog events,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<SelectBranchCommand, AuthProfile>
{
    public async Task<AuthProfile> Handle(SelectBranchCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || currentUser.SessionId is not { } sessionId)
        {
            throw new DomainException(ProblemCodes.AuthenticatedActorRequired, "A session-bound actor is required.", DomainFault.Unauthenticated);
        }

        var principal = await principals.ResolveAsync(sessionId, userId, cancellationToken)
            ?? throw new DomainException(ProblemCodes.TokenSessionMismatch, "The session could not be resolved.", DomainFault.Unauthenticated);

        // The membership scope decides (01 §4.7): an assigned branch, or, for an unrestricted membership (empty list),
        // any active branch of the session's tenant (the lookup runs in the request's tenant scope).
        var allowed = principal.BranchIds.Count > 0
            ? principal.BranchIds.Contains(command.BranchId)
            : principal.TenantId is not null && await branches.IsActiveAsync(command.BranchId, cancellationToken);
        if (!allowed)
        {
            throw new DomainException(ProblemCodes.BranchNotAssigned, "The branch is not assigned to this user.", DomainFault.Refused);
        }

        var session = await sessions.GetForUpdateAsync(sessionId, cancellationToken)
            ?? throw new DomainException(ProblemCodes.SessionRevokedOrExpired, "The session no longer exists.", DomainFault.Unauthenticated);

        session.SelectBranch(command.BranchId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        principals.Evict(sessionId);
        await events.WriteAsync(new SecurityEvent(SecurityEvents.SessionContextChanged, SecurityEvent.Info, principal.TenantId, userId, "branch:" + command.BranchId), cancellationToken);

        var refreshed = await principals.ResolveAsync(sessionId, userId, cancellationToken)
            ?? throw new DomainException(ProblemCodes.TokenSessionMismatch, "The session could not be resolved.", DomainFault.Unauthenticated);
        return Profiles.ToProfile(refreshed, refreshed.Scope);
    }
}
