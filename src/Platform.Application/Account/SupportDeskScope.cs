using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Domain.Account;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Account;

/// <summary>
/// The two sides of the support desk (intake LIS-INT-ACC-001 §4–§5): laboratory requests run in a laboratory tenant,
/// desk requests only in the platform tenant (SUP-403), whatever permission keys a role holds (MODULES.md §1).
/// </summary>
public static class SupportDeskScope
{
    /// <summary>The laboratory of the session for S1, S2, S6, S7; the platform tenant is refused (SUP-020).</summary>
    public static Guid RequireLaboratory(ICurrentTenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        var tenantId = tenant.TenantId ?? throw new DomainException(ProblemCodes.TenantUnresolved, "The tenant of the request is not resolved.", DomainFault.Refused);
        if (TenantIds.IsPlatform(tenantId))
        {
            throw new DomainException(AccountCodes.PlatformCannotOpen, "The platform desk answers tickets from the support desk page.", DomainFault.Refused);
        }

        return tenantId;
    }

    /// <summary>The platform desk check of S3, S4, S5, S7 (desk) and the desk registers (SUP-403).</summary>
    public static void RequireDesk(ICurrentTenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        if (tenant.TenantId is not { } id || !TenantIds.IsPlatform(id))
        {
            throw new DomainException(AccountCodes.DeskRequiresPlatform, "The support desk is available only in the Super Admin portal.", DomainFault.Refused);
        }
    }
}

/// <summary>The acting user of a self-service or support request.</summary>
public static class AccountActor
{
    public static Guid Require(ICurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.UserId ?? throw new DomainException(ProblemCodes.AuthenticatedActorRequired, "An authenticated user is required.", DomainFault.Unauthenticated);
    }

    /// <summary>The user and the session; a machine principal without a session cannot change a password or preferences.</summary>
    public static (Guid UserId, Guid SessionId) RequireSession(ICurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.UserId is not { } userId || user.SessionId is not { } sessionId)
        {
            throw new DomainException(AccountCodes.SessionActorRequired, "A signed-in session is required.", DomainFault.Refused);
        }

        return (userId, sessionId);
    }
}
