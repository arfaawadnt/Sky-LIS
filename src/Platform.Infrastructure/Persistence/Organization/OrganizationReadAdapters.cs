using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions.Security;
using Platform.Application.Organization;
using Platform.Contracts.Common;
using Platform.Contracts.Organization;
using Platform.Domain.Organization;
using Platform.Domain.PlatformAdmin;

namespace Platform.Infrastructure.Persistence.Organization;

/// <summary><see cref="IBranchLookup"/> over <c>app.branches</c>, bounded to the current tenant by the query filter and RLS.</summary>
internal sealed class BranchLookup(AppDbContext context) : IBranchLookup
{
    public Task<BranchSummary?> FindAsync(Guid branchId, CancellationToken cancellationToken) =>
        context.Set<Branch>().AsNoTracking()
            .Where(branch => branch.Id == branchId)
            .Select(branch => new BranchSummary(branch.Id, branch.Code, branch.Name.En, branch.Name.Ar, branch.Currency, branch.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<BranchSummary>> ListActiveAsync(CancellationToken cancellationToken) =>
        await context.Set<Branch>().AsNoTracking()
            .Where(branch => branch.IsActive)
            .OrderBy(branch => branch.Code)
            .Select(branch => new BranchSummary(branch.Id, branch.Code, branch.Name.En, branch.Name.Ar, branch.Currency, branch.IsActive))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<bool> IsActiveAsync(Guid branchId, CancellationToken cancellationToken) =>
        context.Set<Branch>().AnyAsync(branch => branch.Id == branchId && branch.IsActive, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, BranchSummary>> GetManyAsync(IReadOnlyCollection<Guid> branchIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(branchIds);
        if (branchIds.Count == 0)
        {
            return new Dictionary<Guid, BranchSummary>();
        }

        var ids = branchIds.Distinct().ToList();
        return await context.Set<Branch>().AsNoTracking()
            .Where(branch => ids.Contains(branch.Id))
            .Select(branch => new BranchSummary(branch.Id, branch.Code, branch.Name.En, branch.Name.Ar, branch.Currency, branch.IsActive))
            .ToDictionaryAsync(branch => branch.Id, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary><see cref="IBranchGeography"/> over <c>saas.governorates</c> (platform reference data: no tenant filter, no RLS, read-only here).</summary>
internal sealed class BranchGeography(AppDbContext context) : IBranchGeography
{
    public Task<Guid?> CountryOfGovernorateAsync(Guid governorateId, CancellationToken cancellationToken) =>
        context.Set<Governorate>().AsNoTracking()
            .Where(governorate => governorate.Id == governorateId)
            .Select(governorate => (Guid?)governorate.CountryId)
            .FirstOrDefaultAsync(cancellationToken);
}

/// <summary><see cref="IUserDirectory"/>: the membership's bilingual name in the current tenant, else the platform display name.</summary>
internal sealed class UserDirectory(AppDbContext context) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<Guid, UserNameItem>> GetNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, UserNameItem>();
        }

        var ids = userIds.Distinct().ToList();
        var rows = await (
            from user in context.PlatformUsers.AsNoTracking().Where(user => ids.Contains(user.Id))
            from membership in context.TenantMemberships.AsNoTracking().Where(membership => membership.PlatformUserId == user.Id).DefaultIfEmpty()
            select new
            {
                user.Id,
                user.DisplayName,
                NameEn = membership == null ? null : membership.FullName.En,
                NameAr = membership == null ? null : membership.FullName.Ar,
            }).ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows.GroupBy(row => row.Id).ToDictionary(group => group.Key, group =>
        {
            var row = group.First();
            return new UserNameItem(row.Id, row.NameEn ?? row.DisplayName, row.NameEn, row.NameAr);
        });
    }
}

/// <summary>
/// Evicts the cached principals of live sessions in the current tenant (02 §4.4): the next request of those sessions
/// re-resolves role, overrides and branches. <c>identity.refresh_sessions</c> carries no RLS; the tenant condition is explicit.
/// </summary>
internal sealed class PrincipalCacheInvalidator(AppDbContext context, ICurrentTenant tenant, ISessionPrincipalQuery principals) : IPrincipalCacheInvalidator
{
    public async Task EvictUserAsync(Guid platformUserId, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId;
        var sessions = await context.RefreshSessions.AsNoTracking()
            .Where(session => session.PlatformUserId == platformUserId && !session.IsRevoked && session.TenantId == tenantId)
            .Select(session => session.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        sessions.ForEach(principals.Evict);
    }

    public async Task EvictRoleMembersAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId;
        var sessions = await context.RefreshSessions.AsNoTracking()
            .Where(session => !session.IsRevoked && session.TenantId == tenantId
                              && context.TenantMemberships.Any(membership => membership.RoleId == roleId && membership.PlatformUserId == session.PlatformUserId))
            .Select(session => session.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        sessions.ForEach(principals.Evict);
    }
}

/// <summary>The users register (CP-38, SA-07): memberships of the current tenant joined to their platform user and role.</summary>
internal sealed class StaffDirectory(AppDbContext context) : IStaffDirectory
{
    public async Task<Paged<UserListItem>> ListAsync(StaffFilter filter, PageRequest page, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query =
            from membership in context.TenantMemberships.AsNoTracking()
            join user in context.PlatformUsers.AsNoTracking() on membership.PlatformUserId equals user.Id
            select new { membership, user };

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = OrganizationReads.ContainsPattern(filter.Search);
            query = query.Where(row => EF.Functions.ILike(row.user.Username, pattern)
                                       || EF.Functions.ILike(row.membership.FullName.En, pattern)
                                       || EF.Functions.ILike(row.membership.FullName.Ar, pattern)
                                       || EF.Functions.ILike(row.user.Email, pattern)
                                       || (row.user.Mobile != null && EF.Functions.ILike(row.user.Mobile, pattern)));
        }

        if (filter.RoleId is { } roleId)
        {
            query = query.Where(row => row.membership.RoleId == roleId);
        }

        query = filter.Status switch
        {
            StaffStatus.Inactive => query.Where(row => !row.user.IsActive || !row.membership.IsActive),
            StaffStatus.Locked => query.Where(row => row.user.IsActive && row.membership.IsActive && row.user.IsLockedOut && (row.user.LockoutEndUtc == null || row.user.LockoutEndUtc > nowUtc)),
            StaffStatus.Expired => query.Where(row => row.user.IsActive && row.membership.IsActive
                                                      && !(row.user.IsLockedOut && (row.user.LockoutEndUtc == null || row.user.LockoutEndUtc > nowUtc))
                                                      && row.user.PasswordExpiresAtUtc != null && row.user.PasswordExpiresAtUtc <= nowUtc),
            StaffStatus.Active => query.Where(row => row.user.IsActive && row.membership.IsActive
                                                     && !(row.user.IsLockedOut && (row.user.LockoutEndUtc == null || row.user.LockoutEndUtc > nowUtc))
                                                     && (row.user.PasswordExpiresAtUtc == null || row.user.PasswordExpiresAtUtc > nowUtc)),
            _ => query,
        };

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await (
            from row in query.OrderBy(row => row.user.Username).Skip(page.Skip).Take(page.PageSize)
            from role in context.Roles.AsNoTracking().Where(role => role.Id == row.membership.RoleId).DefaultIfEmpty()
            select new
            {
                UserId = row.user.Id,
                row.user.Username,
                NameEn = row.membership.FullName.En,
                NameAr = row.membership.FullName.Ar,
                row.user.Email,
                row.user.Mobile,
                row.membership.Designation,
                row.membership.RoleId,
                RoleNameEn = role == null ? null : role.Name.En,
                RoleNameAr = role == null ? null : role.Name.Ar,
                BranchCount = row.membership.BranchIds.Length,
                row.membership.BranchIds,
                UserActive = row.user.IsActive,
                MembershipActive = row.membership.IsActive,
                row.user.IsLockedOut,
                row.user.LockoutEndUtc,
                row.user.MustChangePassword,
                row.user.PasswordExpiresAtUtc,
                AddedCount = row.membership.AddedPermissionKeys.Length,
                RemovedCount = row.membership.RemovedPermissionKeys.Length,
                row.membership.SpecialRightsOverride,
                row.membership.IsPrimaryAdministrator,
            }).ToListAsync(cancellationToken).ConfigureAwait(false);

        // The codes of the page's assigned branches (the CSV names them, UAT R1-06), inactive ones included.
        var branchIds = rows.SelectMany(row => row.BranchIds).Distinct().ToArray();
        var codes = branchIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await context.Set<Branch>().AsNoTracking().Where(branch => branchIds.Contains(branch.Id))
                .ToDictionaryAsync(branch => branch.Id, branch => branch.Code, cancellationToken).ConfigureAwait(false);

        var items = rows.Select(row => new UserListItem(
            row.UserId,
            row.Username,
            row.NameEn,
            row.NameAr,
            row.Email,
            row.Mobile,
            row.Designation,
            row.RoleId,
            row.RoleNameEn,
            row.RoleNameAr,
            row.BranchCount,
            Application.Organization.Queries.StaffStatuses.Of(row.UserActive, row.MembershipActive, row.IsLockedOut && (row.LockoutEndUtc is null || row.LockoutEndUtc > nowUtc), row.PasswordExpiresAtUtc, nowUtc).ToString(),
            row.MustChangePassword,
            row.PasswordExpiresAtUtc,
            row.AddedCount > 0 || row.RemovedCount > 0 || row.SpecialRightsOverride != null,
            row.IsPrimaryAdministrator,
            row.BranchIds.Select(id => codes.TryGetValue(id, out var code) ? code : id.ToString("D")).Order(StringComparer.Ordinal).ToList())).ToList();

        return new Paged<UserListItem>(items, page.Page, page.PageSize, total);
    }

    public async Task<StaffAccountView?> GetAsync(Guid platformUserId, CancellationToken cancellationToken)
    {
        var membership = await context.TenantMemberships.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.PlatformUserId == platformUserId, cancellationToken).ConfigureAwait(false);
        if (membership is null)
        {
            return null;
        }

        var user = await context.PlatformUsers.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == platformUserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return null;
        }

        var role = membership.RoleId is { } roleId
            ? await context.Roles.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == roleId, cancellationToken).ConfigureAwait(false)
            : null;

        return new StaffAccountView(
            user.Id,
            membership.Id,
            user.Username,
            membership.FullName.En,
            membership.FullName.Ar,
            user.Email,
            user.Mobile,
            membership.EmployeeCode,
            membership.Designation,
            membership.Shifts,
            membership.PreferredLanguage,
            membership.RoleId,
            role?.Name.En,
            role?.Name.Ar,
            role?.IsActive ?? false,
            role?.PermissionKeys ?? [],
            role?.SpecialRights,
            role?.ResultRightValues() ?? [],
            membership.BranchIds,
            user.IsActive,
            membership.IsActive,
            user.IsLockedOut,
            user.LockoutEndUtc,
            user.MustChangePassword,
            user.PasswordExpiresAtUtc,
            user.PasswordChangedAtUtc,
            membership.SignatureFileId,
            membership.IsPrimaryAdministrator,
            membership.AddedPermissionKeys,
            membership.RemovedPermissionKeys,
            membership.SpecialRightsOverride);
    }
}
