namespace Platform.Domain.Organization;

/// <summary>Refusal codes of the Organization module (intake LIS-INT-ORG-001 §7–§9).</summary>
public static class OrganizationCodes
{
    // 001–099: validation and state
    public const string UnknownPermission = "ORG-001";
    public const string NameRequired = "ORG-002";
    public const string BranchCodeInvalid = "ORG-003";
    public const string DescriptionTooLong = "ORG-004";
    public const string PermissionRequired = "ORG-005";
    public const string DiscountCapInvalid = "ORG-006";
    public const string ResultRightInvalid = "ORG-007";
    public const string BranchContactInvalid = "ORG-008";
    public const string CurrencyInvalid = "ORG-009";
    public const string RoleInactive = "ORG-010";
    public const string RoleHasMembers = "ORG-011";
    public const string BranchNotUsable = "ORG-012";
    public const string PrimaryAdministratorRoleProtected = "ORG-014";
    public const string PasswordLength = "ORG-020";
    public const string PasswordContainsUsername = "ORG-021";
    public const string PasswordExpiryInvalid = "ORG-022";
    public const string UsernameInvalid = "ORG-023";
    public const string GrantBeyondOwnPrivileges = "ORG-030";
    public const string UserBelongsToOtherTenants = "ORG-031";
    public const string OverridesNotAllowedOnPlatform = "ORG-032";
    public const string PlatformPermissionOutsidePlatform = "ORG-033";

    // 100–199: conflicts
    public const string BranchCodeExists = "ORG-100";
    public const string RoleNameExists = "ORG-101";
    public const string UsernameExists = "ORG-102";
    public const string EmailExists = "ORG-103";
    public const string MobileExists = "ORG-104";
    public const string EmployeeCodeExists = "ORG-105";
    public const string LastActiveBranch = "ORG-110";

    // 404/405/406: not found
    public const string RoleNotFound = "ORG-404";
    public const string BranchNotFound = "ORG-405";
    public const string UserNotFound = "ORG-406";

    /// <summary>SOD-ORG-001: an administrator does not administer their own access (lock, reset, role, overrides, activation). Fixed, not configurable.</summary>
    public const string SodActorIsTarget = "SOD-ORG-001";
}
