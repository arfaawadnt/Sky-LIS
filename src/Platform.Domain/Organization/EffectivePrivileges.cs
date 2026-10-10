using Platform.Domain.Authorization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Organization;

/// <summary>
/// Permission-key rules shared by roles and membership overrides: catalogue validation (ORG-001), the platform-only
/// modules (ORG-033) and the resolution of a member's effective privileges (BR-17: role ∪ added − removed).
/// </summary>
public static class EffectivePrivileges
{
    public const int MaxKeys = 500;

    /// <summary>Modules that administer the platform itself (clients, plans, platform parameters): granted only in the platform tenant.</summary>
    public static readonly IReadOnlySet<string> PlatformOnlyModules = new HashSet<string>([PermissionCatalog.Platform, PermissionCatalog.Tenancy], StringComparer.Ordinal);

    /// <summary>Every catalogued key a client tenant may grant: the provisioned "Laboratory Administrator" holds exactly these.</summary>
    public static IReadOnlyList<string> TenantGrantableKeys =>
        PermissionCatalog.Keys.Where(key => !IsPlatformOnly(key)).Order(StringComparer.Ordinal).ToList();

    public static bool IsPlatformOnly(string permissionKey)
    {
        ArgumentNullException.ThrowIfNull(permissionKey);
        var dot = permissionKey.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 && PlatformOnlyModules.Contains(permissionKey[..dot]);
    }

    /// <summary>Trimmed, distinct, ordinally sorted, catalogued keys; an unknown key or more than <see cref="MaxKeys"/> is ORG-001.</summary>
    public static string[] Clean(IEnumerable<string> permissionKeys)
    {
        ArgumentNullException.ThrowIfNull(permissionKeys);
        var keys = permissionKeys.Select(key => (key ?? string.Empty).Trim()).Where(key => key.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var unknown = keys.Where(key => !PermissionCatalog.IsKnown(key)).ToList();
        if (unknown.Count > 0)
        {
            throw new DomainException(OrganizationCodes.UnknownPermission, $"Unknown permission(s): {string.Join(", ", unknown)}.", DomainFault.Validation);
        }

        if (keys.Length > MaxKeys)
        {
            throw new DomainException(OrganizationCodes.UnknownPermission, $"At most {MaxKeys} permissions.", DomainFault.Validation);
        }

        return keys;
    }

    /// <summary>
    /// The member's permission keys: the role's (when it is active) plus the user's added keys minus the removed keys,
    /// restricted to the catalogue and, outside the platform tenant, to the non-platform modules (defence in depth for INV-ORG-007).
    /// </summary>
    public static IReadOnlySet<string> PermissionKeys(IEnumerable<string>? roleKeys, IEnumerable<string>? addedKeys, IEnumerable<string>? removedKeys, bool platformTenant)
    {
        var removed = new HashSet<string>(removedKeys ?? [], StringComparer.Ordinal);
        return new HashSet<string>(
            (roleKeys ?? []).Concat(addedKeys ?? [])
                .Where(key => key is not null && !removed.Contains(key) && PermissionCatalog.IsKnown(key) && (platformTenant || !IsPlatformOnly(key))),
            StringComparer.Ordinal);
    }

    /// <summary>The member's special rights: the user's override, else the role's, else none.</summary>
    public static SpecialRights SpecialRightsOf(SpecialRights? roleRights, SpecialRights? overrideRights) => overrideRights ?? roleRights ?? SpecialRights.None;
}
