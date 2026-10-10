namespace Platform.SharedKernel.MultiTenancy;

/// <summary>
/// Well-known tenant identifiers. The platform itself is a tenant: National Technology staff hold memberships and
/// roles in it, and the global master-data templates that every new laboratory receives a copy of (BR-25) are the
/// catalogue rows of this tenant. Row-level security and the query filter work unchanged for it.
/// </summary>
public static class TenantIds
{
    /// <summary>The platform (Super Admin portal) tenant.</summary>
    public static readonly Guid Platform = new("00000000-0000-0000-0000-000000000001");

    public static bool IsPlatform(Guid tenantId) => tenantId == Platform;
}
