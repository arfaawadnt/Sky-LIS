using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Organization;

/// <summary>
/// A tenant-configurable privilege group (01 §4.7, 03 §9; SRS CP-37 / SA-06; LDM manual §12.2–§12.4): a bilingual
/// name, a description, the screen rights as catalogued permission keys, the special rights and the result rights
/// per clinical group and branch. Members inherit everything (BR-17); every privilege-affecting change raises
/// <see cref="RoleChanged"/> so the members' cached principals are evicted. A role is never deleted: it is
/// deactivated, and only when nobody holds it (INV-ORG-005).
/// </summary>
public sealed class Role : AggregateRoot, ITenantScoped
{
    public const int MaxPermissions = EffectivePrivileges.MaxKeys;
    public const int MaxDescriptionLength = 500;
    public const int MaxResultRights = 2000;

    private readonly List<RoleResultRight> _resultRights = [];

    private Role()
    {
        Name = null!;
        PermissionKeys = [];
        SpecialRights = null!;
    }

    private Role(Guid id, LocalizedText name, string? description, string[] permissionKeys, SpecialRights specialRights)
        : base(id)
    {
        Name = name;
        Description = description;
        PermissionKeys = permissionKeys;
        SpecialRights = specialRights;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }

    /// <summary>Unique per tenant on the English form, case-insensitive (INV-ORG-006).</summary>
    public LocalizedText Name { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Sorted, distinct, catalogued keys (<c>module.action</c>).</summary>
    public string[] PermissionKeys { get; private set; }

    /// <summary>The special rights members inherit unless a user carries an override.</summary>
    public SpecialRights SpecialRights { get; private set; }

    /// <summary>Result rights per clinical group and branch (owned rows).</summary>
    public IReadOnlyList<RoleResultRight> ResultRights => _resultRights;

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>R1 Create: at least one catalogued permission (ORG-005, ORG-001). New roles grant no special right unless given (least privilege, intake A2).</summary>
    public static Role Create(LocalizedText name, IEnumerable<string> permissionKeys, DateTimeOffset nowUtc, string? description = null, SpecialRights? specialRights = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        var keys = EffectivePrivileges.Clean(permissionKeys);
        Require(keys.Length > 0, OrganizationCodes.PermissionRequired, "An active role needs at least one permission.", DomainFault.Validation);

        var role = new Role(Guid.CreateVersion7(), name, CleanDescription(description), keys, (specialRights ?? SpecialRights.None).Copy());
        role.Raise(new RoleCreated(role.Id, nowUtc));
        return role;
    }

    /// <summary>
    /// Provisioning (U12): a role created for an explicit tenant, so a membership staged in the same unit of work can
    /// take it before the commit stamps anything (TEN-011 compares the tenants). Same rules as <see cref="Create"/>.
    /// </summary>
    public static Role CreateForTenant(Guid tenantId, LocalizedText name, IEnumerable<string> permissionKeys, DateTimeOffset nowUtc, string? description = null, SpecialRights? specialRights = null)
    {
        Require(tenantId != Guid.Empty, Tenancy.TenancyCodes.TenantRequired, "A provisioned role needs its tenant.", DomainFault.Validation);
        var role = Create(name, permissionKeys, nowUtc, description, specialRights);
        role.TenantId = tenantId;
        return role;
    }

    public void Rename(LocalizedText name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    /// <summary>R2 Rename and describe; allowed in both states.</summary>
    public void Describe(LocalizedText name, string? description)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Description = CleanDescription(description);
    }

    /// <summary>R3 Set permissions: an active role keeps at least one (ORG-005). Unchanged keys raise nothing.</summary>
    public void SetPermissions(IEnumerable<string> permissionKeys, DateTimeOffset nowUtc)
    {
        var keys = EffectivePrivileges.Clean(permissionKeys);
        Require(!IsActive || keys.Length > 0, OrganizationCodes.PermissionRequired, "An active role needs at least one permission.", DomainFault.Validation);
        if (keys.SequenceEqual(PermissionKeys, StringComparer.Ordinal))
        {
            return;
        }

        PermissionKeys = keys;
        Raise(new RoleChanged(Id, nowUtc, RoleChangeAspects.Permissions));
    }

    /// <summary>R4 Set special rights (the value validates the cap, ORG-006).</summary>
    public void SetSpecialRights(SpecialRights specialRights, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(specialRights);
        if (specialRights.Equals(SpecialRights))
        {
            return;
        }

        SpecialRights = specialRights.Copy();
        Raise(new RoleChanged(Id, nowUtc, RoleChangeAspects.SpecialRights));
    }

    /// <summary>
    /// R5 Replace the result rights. Rows are normalised (any right implies see), all-false rows are dropped, a clinical
    /// group is required and a (group, branch) pair appears once (ORG-007). Existing rows are updated in place.
    /// </summary>
    public void SetResultRights(IEnumerable<ResultRight> rights, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(rights);
        var wanted = rights.Select(right => right ?? throw new ArgumentException("A result right is required.", nameof(rights))).Select(right => right.Normalised()).Where(right => right.GrantsAnything).ToList();
        Require(wanted.All(right => right.ClinicalGroupId != Guid.Empty && right.BranchId != Guid.Empty), OrganizationCodes.ResultRightInvalid, "Every result right names a clinical group (and a branch, or none for all branches).", DomainFault.Validation);
        var duplicate = wanted.GroupBy(right => (right.ClinicalGroupId, right.BranchId)).FirstOrDefault(group => group.Count() > 1);
        Require(duplicate is null, OrganizationCodes.ResultRightInvalid, "A clinical group and branch appear more than once.", DomainFault.Validation);
        Require(wanted.Count <= MaxResultRights, OrganizationCodes.ResultRightInvalid, $"At most {MaxResultRights} result rights.", DomainFault.Validation);

        var changed = false;
        foreach (var stale in _resultRights.Where(existing => !wanted.Any(right => right.ClinicalGroupId == existing.ClinicalGroupId && right.BranchId == existing.BranchId)).ToList())
        {
            _resultRights.Remove(stale);
            changed = true;
        }

        foreach (var right in wanted)
        {
            var existing = _resultRights.FirstOrDefault(row => row.ClinicalGroupId == right.ClinicalGroupId && row.BranchId == right.BranchId);
            if (existing is null)
            {
                _resultRights.Add(RoleResultRight.From(right));
                changed = true;
            }
            else if (existing.ToRight() != right)
            {
                existing.Apply(right);
                changed = true;
            }
        }

        if (changed)
        {
            Raise(new RoleChanged(Id, nowUtc, RoleChangeAspects.ResultRights));
        }
    }

    /// <summary>The result rights as values (ordered by group then branch), for projections and the principal.</summary>
    public IReadOnlyList<ResultRight> ResultRightValues() =>
        _resultRights.Select(row => row.ToRight()).OrderBy(right => right.ClinicalGroupId).ThenBy(right => right.BranchId).ToList();

    /// <summary>R6 Deactivate: refused while the role has members (ORG-011). Idempotent.</summary>
    public void Deactivate(int memberCount, DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return;
        }

        Require(memberCount == 0, OrganizationCodes.RoleHasMembers, $"The role still has {memberCount} member(s); assign them another role first.", DomainFault.Conflict);
        IsActive = false;
        Raise(new RoleChanged(Id, nowUtc, RoleChangeAspects.Deactivated));
    }

    /// <summary>R7 Activate: an active role needs a permission (ORG-005). Idempotent.</summary>
    public void Reactivate(DateTimeOffset nowUtc)
    {
        if (IsActive)
        {
            return;
        }

        Require(PermissionKeys.Length > 0, OrganizationCodes.PermissionRequired, "An active role needs at least one permission.", DomainFault.Validation);
        IsActive = true;
        Raise(new RoleChanged(Id, nowUtc, RoleChangeAspects.Activated));
    }

    private static string? CleanDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var value = description.Trim();
        Require(value.Length <= MaxDescriptionLength, OrganizationCodes.DescriptionTooLong, $"The description is at most {MaxDescriptionLength} characters.", DomainFault.Validation);
        return value;
    }
}
