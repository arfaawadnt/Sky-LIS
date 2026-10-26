namespace Platform.Domain.Tenancy;

/// <summary>Refusal codes of the Tenancy module (<c>TEN-nnn</c>).</summary>
public static class TenancyCodes
{
    public const string TenantRequired = "TEN-001";
    public const string UserRequired = "TEN-002";
    public const string EmployeeCodeInvalid = "TEN-003";
    public const string LanguageInvalid = "TEN-004";
    public const string DesignationInvalid = "TEN-005";
    public const string ShiftsInvalid = "TEN-006";
    public const string MembershipInactive = "TEN-010";
    public const string RoleOfAnotherTenant = "TEN-011";

    /// <summary>INV-ORG-010: the primary administrator cannot be deactivated, locked, demoted or have permissions removed.</summary>
    public const string PrimaryAdministratorProtected = "TEN-012";

    /// <summary>INV-ORG-012: a key cannot be both added and removed by a user's overrides.</summary>
    public const string OverrideConflict = "TEN-013";

    /// <summary>The results grid density preference is <c>comfortable</c> or <c>compact</c> (results page, 2026-10-14).</summary>
    public const string ResultsDensityInvalid = "TEN-014";

    /// <summary>LIS-CR-014 T2: the list preferences (column chooser) take identifiers only and stay within 4 KB.</summary>
    public const string ListPreferencesInvalid = "TEN-015";
    public const string MembershipNotFound = "TEN-404";
}
