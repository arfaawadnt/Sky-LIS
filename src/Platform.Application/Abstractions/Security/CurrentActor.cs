using Platform.Contracts.Common;
using Platform.Domain.Organization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Abstractions.Security;

/// <summary>
/// The authenticated actor of the current request, resolved per request from the session row (02 §4.4), never
/// from token claims. Populated by the host; read by behaviors, handlers and the audit-stamp interceptor.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    /// <summary>The session id (<c>sid</c>) when the actor is a session-bound principal.</summary>
    Guid? SessionId { get; }

    /// <summary>Display name as of the action, for audit attribution.</summary>
    string? DisplayName { get; }

    /// <summary>The branch the session is bound to (CP-04), or <see langword="null"/> until one is selected. Branch-stamped modules go through <see cref="BranchScope.Require"/>.</summary>
    Guid? BranchId { get; }

    /// <summary>The session's membership is the primary administrator of its tenant (CP-38); Organization reads it.</summary>
    bool IsPrimaryAdministrator { get; }
}

/// <summary>
/// The branch of the session for modules whose records are branch-stamped (MODULES.md §1): every registration,
/// sample, payment and result takes its branch from here, never from the request body (pitfall P-30).
/// </summary>
public static class BranchScope
{
    /// <summary>The session's branch, or <c>TENANT-004</c> (<see cref="DomainFault.Refused"/>) when the session has not selected one.</summary>
    public static Guid Require(ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        return currentUser.BranchId
            ?? throw new DomainException(ProblemCodes.BranchRequired, "Select a branch before working in this module.", DomainFault.Refused);
    }
}

/// <summary>The tenant of the current request (01 §4.5). Unresolved means the nil tenant: the database matches no row (fail-closed).</summary>
public interface ICurrentTenant
{
    Guid? TenantId { get; }

    /// <summary>True only inside an allow-listed elevation scope (outbox, provisioning, principal query); ElevationScopingTests polices the call sites.</summary>
    bool IsElevated { get; }
}

/// <summary>Scopes the current tenant for code that runs outside a request (jobs) or must bypass the tenant filter (allow-listed sites only).</summary>
public interface ICurrentTenantSetter
{
    /// <summary>Runs the scope as the given tenant, without elevation.</summary>
    IDisposable ForTenant(Guid tenantId);

    /// <summary>Turns the RLS bypass on for the scope. Allowed only from the three allow-listed sites (01 §4.4); always in a <c>using</c>.</summary>
    IDisposable Elevate();
}

/// <summary>
/// The current actor's effective privileges in the current tenant, resolved per request from the session principal
/// (02 §4.4, BR-17; intake LIS-INT-ORG-001 §5). Empty (no permission, <see cref="SpecialRights.None"/>, no result
/// right) for an anonymous or system scope. Billing reads <see cref="SpecialRights"/> (discount cap, pay, refund,
/// transfer, cancel, print invoice, VIP); Results reads <see cref="CanOnResults"/> and the special rights
/// "modify verified result" and "preview without verify".
/// </summary>
public interface IUserPrivileges
{
    bool Has(string permissionKey);

    IReadOnlySet<string> PermissionKeys { get; }

    /// <summary>The member's special rights; <see cref="SpecialRights.DiscountCapPercent"/> null defers to the tenant parameter <c>discountCapPercent</c> (<see cref="SpecialRights.EffectiveDiscountCap"/>).</summary>
    SpecialRights SpecialRights { get; }

    /// <summary>Result rights per clinical group and branch (branch null = every branch).</summary>
    IReadOnlyList<ResultRight> ResultRights { get; }

    /// <summary>True when a result right grants <paramref name="action"/> on results of <paramref name="clinicalGroupId"/> produced in <paramref name="branchId"/>.</summary>
    bool CanOnResults(ResultAction action, Guid clinicalGroupId, Guid branchId);
}
