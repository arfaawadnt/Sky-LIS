using Platform.SharedKernel.Localization;

namespace Platform.Application.PlatformAdmin;

// Ports through which tenant provisioning (SA-04 manual create, CP-02 subscription wizard; WF-01) reaches the modules
// that own the new tenant's first rows. Everything runs in ONE database transaction opened by
// ITenantProvisioningScope: the handler stages the client, its wallet, the default branch, the administrator and the
// staged half of the master-data copy, flushes once (its single SaveChangesAsync), then the set-based half of the
// copy runs on the flushed rows, and the scope commits. Adapters run with the NEW tenant as the current tenant and
// never save.

/// <summary>
/// The provisioning transaction (intake §11): binds the scope to the new tenant, opens one database transaction whose
/// connection may read the platform tenant's catalogue (BR-25), runs <paramref name="work"/> in the new tenant's
/// scope (not elevated) and commits; any exception rolls everything back, including the set-based copies.
/// </summary>
public interface ITenantProvisioningScope
{
    Task RunAsync(Guid newTenantId, Func<CancellationToken, Task> work, CancellationToken cancellationToken);
}

/// <summary>
/// Stages the first branch of a new laboratory (organization module). Called inside the new tenant's scope, before
/// the administrator, so the administrator's membership can be assigned to it.
/// </summary>
public interface IDefaultBranchProvisioner
{
    /// <summary>Stages the main branch named after the laboratory and returns its id (not yet committed).</summary>
    Task<Guid> ProvisionAsync(Guid tenantId, LocalizedText name, CancellationToken cancellationToken);
}

/// <summary>
/// Stages the laboratory administrator (organization module): the platform user (global username, the given
/// Argon2id hash, the manager's mobile for recovery), the membership in the new tenant and a role holding the tenant
/// permissions. Called inside the new tenant's scope.
/// </summary>
public interface ITenantAdministratorProvisioner
{
    /// <summary>Returns the id of the platform user it staged. The username was checked as free by the caller (CLI-101).</summary>
    Task<Guid> ProvisionAsync(Guid tenantId, string username, string displayName, string mobile, string passwordHash, CancellationToken cancellationToken);
}

/// <summary>
/// The copy of the global master data (the catalogue of <see cref="Platform.SharedKernel.MultiTenancy.TenantIds.Platform"/>)
/// to a new tenant (BR-25), in the two phases its halves need: <see cref="StageAsync"/> before the handler's flush
/// (catalogue A builds its copies through the aggregates and stages them), <see cref="ApplyAsync"/> after it (catalogue B
/// copies set-based in SQL and remaps services and clinical groups by code onto catalogue A's flushed rows). Both run
/// inside <see cref="ITenantProvisioningScope"/>; the composite elevates around each half, which the catalogue cloners
/// require (they read one tenant and write another).
/// </summary>
public interface IMasterDataCloner
{
    Task StageAsync(Guid sourceTenantId, Guid targetTenantId, CancellationToken cancellationToken);

    Task ApplyAsync(Guid sourceTenantId, Guid targetTenantId, CancellationToken cancellationToken);
}

/// <summary>
/// Read port of SA-04's rule "deactivation blocks all client users at login": true while the client may sign in
/// (Active, or Trial before its end date). The platform tenant is always active; a tenant without a client record
/// (seeded by the development tools) is treated as active. Wired into sign-in and principal resolution by the
/// identity owner; the refusal code is <c>CLI-020</c> (<c>PlatformAdminCodes.ClientInactive</c>).
/// </summary>
public interface ITenantStatusLookup
{
    Task<bool> IsActiveAsync(Guid tenantId, CancellationToken cancellationToken);
}
