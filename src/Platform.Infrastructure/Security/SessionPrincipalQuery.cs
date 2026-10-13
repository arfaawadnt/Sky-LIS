using Microsoft.EntityFrameworkCore;
using Platform.Application.PlatformAdmin;
using Microsoft.Extensions.Caching.Memory;
using Platform.Application.Abstractions.Security;
using Platform.Application.Identity;
using Platform.Domain.Organization;
using Platform.Domain.Settings;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Persistence.Settings;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.Infrastructure.Security;

/// <summary>
/// The one read on every authenticated request (02 §4.4): session ⋈ user ⋈ membership of the session's tenant ⋈ its
/// role. It runs elevated because it must read <c>app.tenant_memberships</c> before the tenant is known; this is one of
/// the allow-listed elevation sites (ElevationScopingTests). The principal carries the member's <b>effective</b>
/// privileges (BR-17, intake LIS-INT-ORG-001 §5): permission keys = (role ∪ added − removed) within the catalogue and,
/// outside the platform tenant, without platform-only modules; special rights = the user's override, else the role's;
/// result rights = the role's. An inactive role grants nothing. Results are cached ≤ 10 s per session id and evicted
/// on revocation and on every privilege change (<c>IPrincipalCacheInvalidator</c>). The same read carries the idle
/// timeout of the session's tenant (<c>sessionTimeoutMinutes</c>, validation F-01): the tenant's row and the platform's row
/// of <c>app.tenant_settings</c> (readable here because the query is elevated), resolved as <c>ITenantSettings</c> does; a
/// changed value applies to a session within the cache lifetime.
/// </summary>
public sealed class SessionPrincipalQuery(AppDbContext context, ITenantContextAccessor tenant, IMemoryCache cache, ITenantStatusLookup tenantStatus) : ISessionPrincipalQuery
{
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(10);

    public async Task<SessionPrincipal?> ResolveAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken)
    {
        var key = CacheKey(sessionId);
        if (cache.TryGetValue(key, out SessionPrincipal? cached) && cached is not null)
        {
            return cached.UserId == userId ? cached : null;
        }

        SessionPrincipal? principal;
        var timeoutKey = SettingKeys.SessionTimeoutMinutes.Key;
        var platformTenant = TenantIds.Platform;
        using (tenant.Elevate())
        {
            var row = await (
                from session in context.RefreshSessions.AsNoTracking()
                join user in context.PlatformUsers.AsNoTracking() on session.PlatformUserId equals user.Id
                from membership in context.TenantMemberships.AsNoTracking()
                    .Where(m => m.TenantId == session.TenantId && m.PlatformUserId == user.Id)
                    .DefaultIfEmpty()
                where session.Id == sessionId && session.PlatformUserId == userId
                select new
                {
                    session.Id,
                    UserId = user.Id,
                    user.DisplayName,
                    user.IsActive,
                    user.MustChangePassword,
                    user.PasswordExpiresAtUtc,
                    session.TenantId,
                    session.Kind,
                    session.Scope,
                    session.ExpiresAtUtc,
                    session.AbsoluteExpiresAtUtc,
                    session.IsRevoked,
                    session.ReauthenticatedAtUtc,
                    session.BranchId,
                    MembershipId = (Guid?)membership.Id,
                    MembershipActive = (bool?)membership.IsActive,
                    MembershipName = membership.FullName,
                    membership.PreferredLanguage,
                    membership.BranchIds,
                    membership.DepartmentIds,
                    IsPrimaryAdministrator = (bool?)membership.IsPrimaryAdministrator,
                    membership.AddedPermissionKeys,
                    membership.RemovedPermissionKeys,
                    membership.SpecialRightsOverride,
                    RoleId = membership.RoleId,
                    // F-01: the idle timeout of the session's tenant and the platform's, as two scalar subqueries of this one read
                    OwnTimeout = context.Set<TenantSetting>().Where(s => s.TenantId == session.TenantId && s.Key == timeoutKey).Select(s => s.Value).FirstOrDefault(),
                    PlatformTimeout = context.Set<TenantSetting>().Where(s => s.TenantId == platformTenant && s.Key == timeoutKey).Select(s => s.Value).FirstOrDefault(),
                }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (row is null)
            {
                principal = null;
            }
            else
            {
                // The role with its owned special and result rights; an inactive role grants nothing.
                Role? role = null;
                if (row.MembershipId is not null && row.RoleId is { } roleId && row.TenantId is { } roleTenant)
                {
                    role = await context.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.TenantId == roleTenant && r.Id == roleId && r.IsActive, cancellationToken).ConfigureAwait(false);
                }

                principal = new SessionPrincipal(
                    row.Id,
                    row.UserId,
                    row.MembershipName is null ? row.DisplayName : row.MembershipName.For(row.PreferredLanguage),
                    row.IsActive,
                    row.TenantId,
                    row.Kind,
                    row.Scope,
                    row.ExpiresAtUtc,
                    row.AbsoluteExpiresAtUtc,
                    row.IsRevoked,
                    row.ReauthenticatedAtUtc,
                    (row.MembershipActive ?? false) && (row.TenantId is not { } clientTenant || await tenantStatus.IsActiveAsync(clientTenant, cancellationToken).ConfigureAwait(false)),   // an inactive laboratory ends its users' sessions (SA-04)
                    row.PreferredLanguage ?? "en",
                    role?.Name.En,
                    EffectivePrivileges.PermissionKeys(role?.PermissionKeys, row.AddedPermissionKeys, row.RemovedPermissionKeys, row.TenantId == TenantIds.Platform),
                    row.BranchIds ?? [],
                    row.DepartmentIds ?? [],
                    row.BranchId)
                {
                    SpecialRights = EffectivePrivileges.SpecialRightsOf(role?.SpecialRights, row.SpecialRightsOverride),
                    ResultRights = role?.ResultRightValues() ?? [],
                    MembershipId = row.MembershipId,
                    RoleId = role?.Id,
                    RoleNameAr = role?.Name.Ar,
                    DisplayNameEn = row.MembershipName?.En,
                    DisplayNameAr = row.MembershipName?.Ar,
                    IsPrimaryAdministrator = row.IsPrimaryAdministrator ?? false,
                    MustChangePassword = row.MustChangePassword,
                    PasswordExpiresAtUtc = row.PasswordExpiresAtUtc,
                    SessionTimeoutMinutes = SettingKeys.SessionTimeoutMinutes.Resolve(row.OwnTimeout, row.PlatformTimeout),
                };
            }
        }

        if (principal is not null)
        {
            cache.Set(key, principal, CacheLifetime);
        }

        return principal;
    }

    public void Evict(Guid sessionId) => cache.Remove(CacheKey(sessionId));

    private static string CacheKey(Guid sessionId) => $"session-principal:{sessionId:N}";
}

/// <summary>
/// Lists a user's active memberships across tenants for the tenant selection at sign-in (D-06). Elevated: an
/// allow-listed site. <see cref="MembershipSummary.BranchIds"/> is the <b>selectable</b> list: the assigned branches,
/// or — for an unrestricted membership — every active branch of that tenant (intake LIS-INT-ORG-001 A1), so sign-in
/// auto-selects the branch when exactly one is selectable (CP-04).
/// </summary>
public sealed class MembershipDirectory(AppDbContext context, ITenantContextAccessor tenant, ITenantStatusLookup tenantStatus) : IMembershipDirectory
{
    public async Task<IReadOnlyList<MembershipSummary>> ListActiveForUserAsync(Guid platformUserId, CancellationToken cancellationToken)
    {
        using (tenant.Elevate())
        {
            var memberships = await context.TenantMemberships.AsNoTracking()
                .Where(m => m.PlatformUserId == platformUserId && m.IsActive)
                .OrderBy(m => m.TenantId)
                .Select(m => new { m.TenantId, m.Id, NameEn = m.FullName.En, NameAr = m.FullName.Ar, m.PreferredLanguage, m.BranchIds })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // SA-04: users of an inactive (deactivated or expired-trial) laboratory cannot sign in to it.
            var activeTenants = new HashSet<Guid>();
            foreach (var tenantId in memberships.Select(m => m.TenantId).Distinct())
            {
                if (await tenantStatus.IsActiveAsync(tenantId, cancellationToken).ConfigureAwait(false))
                {
                    activeTenants.Add(tenantId);
                }
            }

            memberships = memberships.Where(m => activeTenants.Contains(m.TenantId)).ToList();

            var unrestricted = memberships.Where(m => m.BranchIds.Length == 0).Select(m => m.TenantId).Distinct().ToList();
            var activeBranches = unrestricted.Count == 0
                ? []
                : await context.Set<Branch>().AsNoTracking()
                    .Where(branch => unrestricted.Contains(branch.TenantId) && branch.IsActive)
                    .OrderBy(branch => branch.Code)
                    .Select(branch => new { branch.TenantId, branch.Id })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

            return memberships
                .Select(m => new MembershipSummary(
                    m.TenantId,
                    m.Id,
                    m.NameEn,
                    m.NameAr,
                    m.PreferredLanguage,
                    m.BranchIds.Length > 0 ? m.BranchIds : activeBranches.Where(branch => branch.TenantId == m.TenantId).Select(branch => branch.Id).ToArray()))
                .ToList();
        }
    }

    public async Task<bool> HasInactiveClientMembershipAsync(Guid platformUserId, Guid? tenantId, CancellationToken cancellationToken)
    {
        using (tenant.Elevate())
        {
            var tenantIds = await context.TenantMemberships.AsNoTracking()
                .Where(m => m.PlatformUserId == platformUserId && m.IsActive && (tenantId == null || m.TenantId == tenantId))
                .Select(m => m.TenantId)
                .Distinct()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var candidate in tenantIds.Where(id => !TenantIds.IsPlatform(id)))
            {
                if (!await tenantStatus.IsActiveAsync(candidate, cancellationToken).ConfigureAwait(false))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

/// <summary>
/// Scoped holder of the resolved privileges (02 §4.4); filled by the host after authentication from the session
/// principal (<see cref="Set(SessionPrincipal)"/>), read by the authorization behavior and by modules that need the
/// special or result rights. Empty for anonymous and system scopes.
/// </summary>
public sealed class UserPrivilegesAccessor : IUserPrivileges
{
    private HashSet<string> _keys = new(StringComparer.Ordinal);

    public IReadOnlySet<string> PermissionKeys => _keys;

    public SpecialRights SpecialRights { get; private set; } = SpecialRights.None;

    public IReadOnlyList<ResultRight> ResultRights { get; private set; } = [];

    public bool Has(string permissionKey) => permissionKey is not null && _keys.Contains(permissionKey);

    public bool CanOnResults(ResultAction action, Guid clinicalGroupId, Guid branchId) => ResultRight.Allows(ResultRights, action, clinicalGroupId, branchId);

    /// <summary>Everything the principal resolved: permission keys, special rights and result rights.</summary>
    public void Set(SessionPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        _keys = new HashSet<string>(principal.PermissionKeys, StringComparer.Ordinal);
        SpecialRights = principal.SpecialRights;
        ResultRights = principal.ResultRights;
    }

    /// <summary>Permission keys only (no special or result right); kept for hosts that hold just the keys.</summary>
    public void Set(IEnumerable<string> permissionKeys)
    {
        ArgumentNullException.ThrowIfNull(permissionKeys);
        _keys = new HashSet<string>(permissionKeys, StringComparer.Ordinal);
        SpecialRights = SpecialRights.None;
        ResultRights = [];
    }

    public void Clear()
    {
        _keys = new HashSet<string>(StringComparer.Ordinal);
        SpecialRights = SpecialRights.None;
        ResultRights = [];
    }
}
