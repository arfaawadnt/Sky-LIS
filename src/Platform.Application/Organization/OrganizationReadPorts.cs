using Platform.Application.Abstractions.Persistence;
using Platform.Contracts.Common;
using Platform.Contracts.Organization;
using Platform.Domain.Organization;

namespace Platform.Application.Organization;

// Read ports of the Organization module (MODULES.md §1): other modules declare nothing for these; they inject the
// ports below. Adapters live in Platform.Infrastructure.Persistence.Organization and are registered by convention.

/// <summary>A branch as other modules see it.</summary>
public sealed record BranchSummary(Guid Id, string Code, string NameEn, string NameAr, string Currency, bool IsActive);

/// <summary>Branches of the current tenant (treasuries, registration, results, the branch-selection page).</summary>
public interface IBranchLookup
{
    Task<BranchSummary?> FindAsync(Guid branchId, CancellationToken cancellationToken);

    /// <summary>Active branches of the current tenant, ordered by code.</summary>
    Task<IReadOnlyList<BranchSummary>> ListActiveAsync(CancellationToken cancellationToken);

    Task<bool> IsActiveAsync(Guid branchId, CancellationToken cancellationToken);

    /// <summary>The branches of the given ids in the current tenant, active or not (registers that show a branch code and name).</summary>
    Task<IReadOnlyDictionary<Guid, BranchSummary>> GetManyAsync(IReadOnlyCollection<Guid> branchIds, CancellationToken cancellationToken);
}

/// <summary>
/// The platform geography as the Organization module reads it (countries and governorates are platform-wide <c>saas</c>
/// tables with no tenant, readable by the runtime role; no elevation is needed).
/// </summary>
public interface IBranchGeography
{
    /// <summary>The country of a governorate, or null when the governorate does not exist.</summary>
    Task<Guid?> CountryOfGovernorateAsync(Guid governorateId, CancellationToken cancellationToken);
}

/// <summary>Display names of users for registers ("requested by", "verified by"): the membership's bilingual name in the current tenant, else the platform display name.</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, UserNameItem>> GetNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}

/// <summary>Evicts cached session principals so a privilege change applies to the next request (02 §4.4, BR-17).</summary>
public interface IPrincipalCacheInvalidator
{
    /// <summary>Every live session of the user in the current tenant.</summary>
    Task EvictUserAsync(Guid platformUserId, CancellationToken cancellationToken);

    /// <summary>Every live session, in the current tenant, of every member holding the role.</summary>
    Task EvictRoleMembersAsync(Guid roleId, CancellationToken cancellationToken);
}

/// <summary><see cref="Sort"/>: server-side order before paging (validation v0.5.0 B-03; keys in <see cref="OrganizationSortKeys.Users"/>).</summary>
public sealed record StaffFilter(string? Search, Guid? RoleId, StaffStatus? Status, ListSort Sort = default);

/// <summary>The status shown on the users register, derived at read time.</summary>
public enum StaffStatus
{
    Active = 0,
    Locked = 1,
    Expired = 2,
    Inactive = 3,
}

/// <summary>Everything the user editor shows, as stored; the query handler resolves the effective privileges from it.</summary>
public sealed record StaffAccountView(
    Guid UserId,
    Guid MembershipId,
    string Username,
    string FullNameEn,
    string FullNameAr,
    string Email,
    string? Mobile,
    string EmployeeCode,
    string? Designation,
    IReadOnlyList<string> Shifts,
    string PreferredLanguage,
    Guid? RoleId,
    string? RoleNameEn,
    string? RoleNameAr,
    bool RoleActive,
    IReadOnlyList<string> RolePermissionKeys,
    SpecialRights? RoleSpecialRights,
    IReadOnlyList<ResultRight> RoleResultRights,
    IReadOnlyList<Guid> BranchIds,
    bool UserActive,
    bool MembershipActive,
    bool IsLockedOut,
    DateTimeOffset? LockoutEndUtc,
    bool MustChangePassword,
    DateTimeOffset? PasswordExpiresAtUtc,
    DateTimeOffset PasswordChangedAtUtc,
    Guid? SignatureFileId,
    bool IsPrimaryAdministrator,
    IReadOnlyList<string> AddedPermissionKeys,
    IReadOnlyList<string> RemovedPermissionKeys,
    SpecialRights? SpecialRightsOverride);

/// <summary>The users register of the current tenant (CP-38, SA-07): platform users joined to their membership and role.</summary>
public interface IStaffDirectory
{
    Task<Paged<UserListItem>> ListAsync(StaffFilter filter, PageRequest page, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task<StaffAccountView?> GetAsync(Guid platformUserId, CancellationToken cancellationToken);
}
