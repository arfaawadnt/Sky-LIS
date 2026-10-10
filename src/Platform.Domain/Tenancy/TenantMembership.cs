using Platform.Domain.Organization;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Tenancy;

/// <summary>
/// A person's profile inside one tenant (D-06): the tenant-side half of the identity split, stored in
/// <c>app.tenant_memberships</c> under FORCE ROW LEVEL SECURITY with the key <c>(tenant_id, id)</c>. It carries the
/// role the member inherits (BR-17), the working scope (branches: empty = every active branch of the tenant, intake
/// LIS-INT-ORG-001 A1), the staff profile of SRS CP-38 (designation, shifts, signature) and the per-user overrides
/// that make the member "modified" until restored to the role. The tenant id is explicit at creation because
/// provisioning runs for a tenant other than the caller's (pitfall P-30).
/// </summary>
public sealed class TenantMembership : AggregateRoot, ITenantScoped
{
    public const int MaxEmployeeCodeLength = 32;
    public const int MaxDesignationLength = 100;
    public const int MaxShifts = 10;
    public const int MaxShiftLength = 40;
    public const int MaxBranches = 200;
    private static readonly string[] SupportedLanguages = ["en", "ar"];

    private TenantMembership()
    {
        EmployeeCode = string.Empty;
        FullName = null!;
        PreferredLanguage = "en";
        BranchIds = [];
        DepartmentIds = [];
        Shifts = [];
        AddedPermissionKeys = [];
        RemovedPermissionKeys = [];
    }

    private TenantMembership(Guid id, Guid tenantId, Guid platformUserId, string employeeCode, LocalizedText fullName, string preferredLanguage)
        : base(id)
    {
        TenantId = tenantId;
        PlatformUserId = platformUserId;
        EmployeeCode = employeeCode;
        FullName = fullName;
        PreferredLanguage = preferredLanguage;
        IsActive = true;
        BranchIds = [];
        DepartmentIds = [];
        Shifts = [];
        AddedPermissionKeys = [];
        RemovedPermissionKeys = [];
    }

    public Guid TenantId { get; private set; }

    public Guid PlatformUserId { get; private set; }

    /// <summary>The laboratory's own staff number; unique per tenant.</summary>
    public string EmployeeCode { get; private set; }

    /// <summary>Bilingual display name, as master data carries Arabic names on every record.</summary>
    public LocalizedText FullName { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>The privilege group the member inherits (01 §4.7, BR-17); null until the administrator assigns one.</summary>
    public Guid? RoleId { get; private set; }

    /// <summary>The branches the member may select at sign-in (CP-04); empty means every active branch of the tenant.</summary>
    public Guid[] BranchIds { get; private set; }

    public Guid[] DepartmentIds { get; private set; }

    public string PreferredLanguage { get; private set; }

    /// <summary>Job title shown on the user list and on signed reports (CP-38).</summary>
    public string? Designation { get; private set; }

    /// <summary>Free shift labels (Morning, Night …; LDM manual §12.6 step 7).</summary>
    public string[] Shifts { get; private set; }

    /// <summary>The stored signature image printed on reports the member verifies (LDM manual §12.7); a file id of the file service.</summary>
    public Guid? SignatureFileId { get; private set; }

    /// <summary>The tenant's main administrator (CP-38/SA-07 "main administrator protected", INV-ORG-010).</summary>
    public bool IsPrimaryAdministrator { get; private set; }

    /// <summary>Override: catalogued keys granted to this user on top of the role (BR-17).</summary>
    public string[] AddedPermissionKeys { get; private set; }

    /// <summary>Override: keys of the role withheld from this user (BR-17).</summary>
    public string[] RemovedPermissionKeys { get; private set; }

    /// <summary>Override: special rights that replace the role's for this user; <see langword="null"/> = the role's apply.</summary>
    public SpecialRights? SpecialRightsOverride { get; private set; }

    /// <summary>The "Modified" flag of LDM manual §12.8: the user's privileges differ from the role's.</summary>
    public bool IsModified => AddedPermissionKeys.Length > 0 || RemovedPermissionKeys.Length > 0 || SpecialRightsOverride is not null;

    /// <summary>Optimistic concurrency token, mapped onto PostgreSQL's <c>xmin</c> system column.</summary>
    public uint RowVersion { get; private set; }

    public static TenantMembership Create(Guid tenantId, Guid platformUserId, string employeeCode, LocalizedText fullName, string preferredLanguage, DateTimeOffset nowUtc)
    {
        Require(tenantId != Guid.Empty, TenancyCodes.TenantRequired, "A membership needs a tenant.", DomainFault.Validation);
        Require(platformUserId != Guid.Empty, TenancyCodes.UserRequired, "A membership needs a platform user.", DomainFault.Validation);
        ArgumentNullException.ThrowIfNull(fullName);

        var membership = new TenantMembership(Guid.CreateVersion7(), tenantId, platformUserId, CleanEmployeeCode(employeeCode), fullName, RequireLanguage(preferredLanguage));
        membership.Raise(new TenantMembershipCreated(tenantId, membership.Id, platformUserId, nowUtc));
        return membership;
    }

    public void Rename(LocalizedText fullName)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        FullName = fullName;
    }

    /// <summary>U2 Edit the staff profile (CP-38): names, designation, shifts, language.</summary>
    public void UpdateProfile(LocalizedText fullName, string? designation, IEnumerable<string> shifts, string preferredLanguage)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        ArgumentNullException.ThrowIfNull(shifts);
        var language = RequireLanguage(preferredLanguage);
        var cleanDesignation = CleanDesignation(designation);
        var cleanShifts = CleanShifts(shifts);

        FullName = fullName;
        Designation = cleanDesignation;
        Shifts = cleanShifts;
        PreferredLanguage = language;
    }

    /// <summary>Sets or clears the stored signature image (a file id of the file service).</summary>
    public void SetSignature(Guid? signatureFileId) => SignatureFileId = signatureFileId == Guid.Empty ? null : signatureFileId;

    public void AssignScope(IEnumerable<Guid> branchIds, IEnumerable<Guid> departmentIds)
    {
        ArgumentNullException.ThrowIfNull(branchIds);
        ArgumentNullException.ThrowIfNull(departmentIds);
        BranchIds = branchIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        DepartmentIds = departmentIds.Where(id => id != Guid.Empty).Distinct().ToArray();
    }

    /// <summary>U3 Assign the branches the member may work in (empty = all active branches). The handler checks that each exists and is active (ORG-012).</summary>
    public void AssignBranches(IEnumerable<Guid> branchIds)
    {
        ArgumentNullException.ThrowIfNull(branchIds);
        var ids = branchIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        Require(ids.Length <= MaxBranches, OrganizationCodes.BranchNotUsable, $"At most {MaxBranches} branches.", DomainFault.Validation);
        BranchIds = ids;
    }

    public void SetPreferredLanguage(string language) => PreferredLanguage = RequireLanguage(language);

    /// <summary>
    /// U4 Assign the role whose privileges the member inherits: same tenant (TEN-011), active (ORG-010); the primary
    /// administrator keeps their role (TEN-012). Assigning the current role again changes nothing.
    /// </summary>
    public void AssignRole(Role role, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(role);
        Require(role.TenantId == TenantId, TenancyCodes.RoleOfAnotherTenant, "The role belongs to another tenant.", DomainFault.Refused);
        Require(role.IsActive, OrganizationCodes.RoleInactive, "The role is inactive.", DomainFault.Conflict);
        if (RoleId == role.Id)
        {
            return;
        }

        Require(!IsPrimaryAdministrator || RoleId is null, TenancyCodes.PrimaryAdministratorProtected, "The main administrator's role cannot be changed.", DomainFault.Refused);
        RoleId = role.Id;
        Raise(new TenantMembershipRoleChanged(TenantId, Id, PlatformUserId, role.Id, nowUtc));
    }

    /// <summary>
    /// U5 Per-user overrides (BR-17: they affect this user only). Keys are catalogued (ORG-001); a key is not both
    /// added and removed (TEN-013); nothing is removed from the primary administrator (TEN-012).
    /// </summary>
    public void SetOverrides(IEnumerable<string> addedPermissionKeys, IEnumerable<string> removedPermissionKeys, SpecialRights? specialRights, DateTimeOffset nowUtc)
    {
        var added = EffectivePrivileges.Clean(addedPermissionKeys);
        var removed = EffectivePrivileges.Clean(removedPermissionKeys);
        var both = added.Intersect(removed, StringComparer.Ordinal).ToList();
        Require(both.Count == 0, TenancyCodes.OverrideConflict, $"A permission cannot be both added and removed: {string.Join(", ", both)}.", DomainFault.Validation);
        Require(!IsPrimaryAdministrator || removed.Length == 0, TenancyCodes.PrimaryAdministratorProtected, "Permissions cannot be withheld from the main administrator.", DomainFault.Refused);

        var rights = specialRights?.Copy();
        if (added.SequenceEqual(AddedPermissionKeys, StringComparer.Ordinal) && removed.SequenceEqual(RemovedPermissionKeys, StringComparer.Ordinal) && Equals(rights, SpecialRightsOverride))
        {
            return;
        }

        AddedPermissionKeys = added;
        RemovedPermissionKeys = removed;
        SpecialRightsOverride = rights;
        Raise(new TenantMembershipPrivilegesOverridden(TenantId, Id, PlatformUserId, added.Length, removed.Length, rights is not null, nowUtc));
    }

    /// <summary>U6 Restore to role (LDM manual §12.8): every individual change is dropped. Idempotent.</summary>
    public void RestoreToRole(DateTimeOffset nowUtc)
    {
        if (!IsModified)
        {
            return;
        }

        AddedPermissionKeys = [];
        RemovedPermissionKeys = [];
        SpecialRightsOverride = null;
        Raise(new TenantMembershipRestoredToRole(TenantId, Id, PlatformUserId, RoleId, nowUtc));
    }

    /// <summary>Flags the tenant's main administrator (platform provisioning only).</summary>
    public void MarkPrimaryAdministrator() => IsPrimaryAdministrator = true;

    /// <summary>U10 Deactivate: the member no longer works in this tenant; the primary administrator cannot be deactivated (TEN-012). Idempotent.</summary>
    public void Deactivate(DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return;
        }

        Require(!IsPrimaryAdministrator, TenancyCodes.PrimaryAdministratorProtected, "The main administrator cannot be deactivated.", DomainFault.Refused);
        IsActive = false;
        Raise(new TenantMembershipDeactivated(TenantId, Id, PlatformUserId, nowUtc));
    }

    public void Reactivate(DateTimeOffset nowUtc)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        Raise(new TenantMembershipReactivated(TenantId, Id, PlatformUserId, nowUtc));
    }

    public static string CleanEmployeeCode(string employeeCode)
    {
        var value = (employeeCode ?? string.Empty).Trim();
        Require(value.Length is >= 1 and <= MaxEmployeeCodeLength, TenancyCodes.EmployeeCodeInvalid, $"The employee code must be 1 to {MaxEmployeeCodeLength} characters.", DomainFault.Validation);
        return value;
    }

    private static string? CleanDesignation(string? designation)
    {
        if (string.IsNullOrWhiteSpace(designation))
        {
            return null;
        }

        var value = designation.Trim();
        Require(value.Length <= MaxDesignationLength, TenancyCodes.DesignationInvalid, $"The designation is at most {MaxDesignationLength} characters.", DomainFault.Validation);
        return value;
    }

    private static string[] CleanShifts(IEnumerable<string> shifts)
    {
        var values = shifts.Select(shift => (shift ?? string.Empty).Trim()).Where(shift => shift.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Require(
            values.Length <= MaxShifts && values.All(shift => shift.Length <= MaxShiftLength),
            TenancyCodes.ShiftsInvalid,
            $"At most {MaxShifts} shifts of up to {MaxShiftLength} characters.",
            DomainFault.Validation);
        return values;
    }

    private static string RequireLanguage(string language)
    {
        var value = (language ?? string.Empty).Trim().ToLowerInvariant();
        Require(SupportedLanguages.Contains(value), TenancyCodes.LanguageInvalid, "The language must be 'en' or 'ar'.", DomainFault.Validation);
        return value;
    }
}
