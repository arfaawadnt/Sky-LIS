using Platform.Contracts.Common;

namespace Platform.Contracts.Organization;

// Request and response shapes of the Organization module (intake LIS-INT-ORG-001). Validation here is shape only;
// rules live in the aggregates and handlers.

// ---- Branches (CP-36) -------------------------------------------------------------------------------------------

public sealed record CreateBranchRequest(string Code, string NameEn, string NameAr, Guid? CountryId, Guid? GovernorateId, string? Phone, string? Email, string Currency);

public sealed record UpdateBranchRequest(string NameEn, string NameAr, Guid? CountryId, Guid? GovernorateId, string? Phone, string? Email, string Currency);

public sealed record BranchListItem(
    Guid Id,
    string Code,
    string NameEn,
    string NameAr,
    Guid? CountryId,
    Guid? GovernorateId,
    string? Phone,
    string? Email,
    string Currency,
    bool IsActive);

/// <summary>A branch the current user may bind the session to (CP-04).</summary>
public sealed record SelectableBranch(Guid Id, string Code, string NameEn, string NameAr);

/// <summary>The branch-selection list: the session's current branch and the branches it may select.</summary>
public sealed record SelectableBranchesResponse(Guid? CurrentBranchId, IReadOnlyList<SelectableBranch> Branches);

// ---- Roles and privileges (CP-37, SA-06) ------------------------------------------------------------------------

/// <summary><see cref="DiscountCapPercent"/>: null = the tenant parameter <c>discountCapPercent</c>, 0 = no discount, 1–100 = the cap.</summary>
public sealed record SpecialRightsDto(
    int? DiscountCapPercent,
    bool CanPay,
    bool CanRefund,
    bool CanTransfer,
    bool CanCancel,
    bool CanModifyVerifiedResult,
    bool CanPreviewWithoutVerify,
    bool CanPrintInvoice,
    bool CanSeeVip);

/// <summary>Result rights on one clinical group in one branch (<see cref="BranchId"/> null = every branch).</summary>
public sealed record ResultRightDto(Guid ClinicalGroupId, Guid? BranchId, bool CanSee, bool CanReview, bool CanVerify, bool CanEdit, bool CanPrint);

public sealed record CreateRoleRequest(string NameEn, string NameAr, string? Description, IReadOnlyList<string> PermissionKeys);

public sealed record UpdateRoleRequest(string NameEn, string NameAr, string? Description);

public sealed record SetRolePermissionsRequest(IReadOnlyList<string> PermissionKeys);

public sealed record SetRoleResultRightsRequest(IReadOnlyList<ResultRightDto> Rights);

public sealed record RoleListItem(Guid Id, string NameEn, string NameAr, string? Description, int PermissionCount, int MemberCount, bool IsActive);

public sealed record RoleDetail(
    Guid Id,
    string NameEn,
    string NameAr,
    string? Description,
    bool IsActive,
    IReadOnlyList<string> PermissionKeys,
    SpecialRightsDto SpecialRights,
    IReadOnlyList<ResultRightDto> ResultRights,
    int MemberCount,
    bool HoldsPrimaryAdministrator);

/// <summary>One module of the permission catalogue with the actions it exposes (keys <c>module.action</c>).</summary>
public sealed record PermissionModuleDto(string Key, string DisplayName, IReadOnlyList<string> Actions, bool PlatformOnly);

/// <summary>The privilege matrix source: every module and the closed action set in display order.</summary>
public sealed record PermissionCatalogueResponse(IReadOnlyList<PermissionModuleDto> Modules, IReadOnlyList<string> Actions);

// ---- Users (CP-38, SA-07) ---------------------------------------------------------------------------------------

/// <summary>A staff account: the platform user and the membership in the current tenant. The password travels as a credential envelope (D-03).</summary>
public sealed record CreateUserRequest
{
    public string Username { get; init; } = string.Empty;

    [CredentialField]
    public string Password { get; init; } = string.Empty;

    public bool MustChangePassword { get; init; } = true;

    public string FullNameEn { get; init; } = string.Empty;

    public string FullNameAr { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string? Mobile { get; init; }

    public string? Designation { get; init; }

    public IReadOnlyList<string>? Shifts { get; init; }

    public Guid? RoleId { get; init; }

    /// <summary>Empty or null = every active branch of the tenant.</summary>
    public IReadOnlyList<Guid>? BranchIds { get; init; }

    public DateTimeOffset? PasswordExpiresAtUtc { get; init; }

    /// <summary>Defaults to the upper-cased username.</summary>
    public string? EmployeeCode { get; init; }

    public string? PreferredLanguage { get; init; }
}

public sealed record UpdateUserRequest(
    string FullNameEn,
    string FullNameAr,
    string Email,
    string? Mobile,
    string? Designation,
    IReadOnlyList<string>? Shifts,
    string PreferredLanguage,
    DateTimeOffset? PasswordExpiresAtUtc,
    bool MustChangePassword);

/// <summary>An administrator's password reset (CP-38); the temporary password travels as a credential envelope (D-03).</summary>
public sealed record ResetUserPasswordRequest
{
    [CredentialField]
    public string TemporaryPassword { get; init; } = string.Empty;

    public bool MustChangePassword { get; init; } = true;
}

public sealed record AssignUserRoleRequest(Guid RoleId);

/// <summary>Empty = every active branch of the tenant.</summary>
public sealed record AssignUserBranchesRequest(IReadOnlyList<Guid> BranchIds);

/// <summary>BR-17 per-user overrides; <see cref="SpecialRights"/> null = the role's special rights apply.</summary>
public sealed record SetUserOverridesRequest(IReadOnlyList<string> AddedPermissionKeys, IReadOnlyList<string> RemovedPermissionKeys, SpecialRightsDto? SpecialRights);

/// <summary>
/// Status names: Active, Locked, Expired, Inactive (membership deactivated). <see cref="BranchCount"/> 0 = every active
/// branch; <see cref="BranchCodes"/> the codes of the assigned branches, by code (empty = every branch; UAT R1-06).
/// </summary>
public sealed record UserListItem(
    Guid UserId,
    string Username,
    string FullNameEn,
    string FullNameAr,
    string Email,
    string? Mobile,
    string? Designation,
    Guid? RoleId,
    string? RoleNameEn,
    string? RoleNameAr,
    int BranchCount,
    string Status,
    bool MustChangePassword,
    DateTimeOffset? PasswordExpiresAtUtc,
    bool IsModified,
    bool IsPrimaryAdministrator,
    IReadOnlyList<string>? BranchCodes = null);

public sealed record UserOverridesDto(IReadOnlyList<string> AddedPermissionKeys, IReadOnlyList<string> RemovedPermissionKeys, SpecialRightsDto? SpecialRights);

/// <summary>What a member may do: permission keys, special rights and result rights resolved as the session resolves them (BR-17).</summary>
public sealed record EffectivePrivilegesDto(IReadOnlyList<string> PermissionKeys, SpecialRightsDto SpecialRights, IReadOnlyList<ResultRightDto> ResultRights);

public sealed record UserDetail(
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
    IReadOnlyList<Guid> BranchIds,
    string Status,
    bool IsActive,
    bool IsLocked,
    DateTimeOffset? LockoutEndUtc,
    bool MustChangePassword,
    DateTimeOffset? PasswordExpiresAtUtc,
    DateTimeOffset PasswordChangedAtUtc,
    Guid? SignatureFileId,
    bool IsPrimaryAdministrator,
    bool IsModified,
    bool BelongsToOtherTenants,
    UserOverridesDto Overrides,
    EffectivePrivilegesDto Effective,
    IReadOnlyList<string> RolePermissionKeys,
    SpecialRightsDto RoleSpecialRights);

/// <summary>The current session's own privileges (any authenticated actor): what the screens may offer.</summary>
public sealed record MyPrivilegesResponse(
    Guid UserId,
    Guid? TenantId,
    Guid? BranchId,
    bool IsPrimaryAdministrator,
    bool MustChangePassword,
    DateTimeOffset? PasswordExpiresAtUtc,
    EffectivePrivilegesDto Privileges);

/// <summary>Names of a user, for registers of other modules (read port <c>IUserDirectory</c>).</summary>
public sealed record UserNameItem(Guid UserId, string DisplayName, string? FullNameEn, string? FullNameAr);
