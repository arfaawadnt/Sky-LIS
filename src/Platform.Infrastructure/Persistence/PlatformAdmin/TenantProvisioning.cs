using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Platform.Application.Abstractions.Security;
using Platform.Application.Catalogue;
using Platform.Application.PlatformAdmin;

namespace Platform.Infrastructure.Persistence.PlatformAdmin;

// The provisioning transaction and the BR-25 copy (intake LIS-INT-PLT-001 §11). This file is the allow-listed
// elevation site of provisioning (WebApiHardeningTests, ElevationScopingTests): elevation is confined to (1) opening
// the transaction's connection, so the connection may read the platform tenant's catalogue, and (2) the two catalogue
// cloner calls, which read one tenant and write another and refuse to run otherwise. Everything else of provisioning
// (organization adapters, the handler's flush) runs in the new tenant's scope without elevation, so the query filter
// keeps their reads inside the new tenant.

/// <summary>
/// <see cref="ITenantProvisioningScope"/>: <c>ForTenant(new)</c>, an elevated moment to open one database transaction
/// (the connection is stamped with the new tenant and the RLS bypass when it opens, 01 §4.5), the work in the new
/// tenant's scope, then the commit. An exception disposes the transaction uncommitted: nothing of the tenant remains.
/// </summary>
internal sealed class TenantProvisioningScope(AppDbContext context, ICurrentTenantSetter tenantScope) : ITenantProvisioningScope
{
    public async Task RunAsync(Guid newTenantId, Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (context.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException("Provisioning opens its own transaction; it cannot join another one.");
        }

        using (tenantScope.ForTenant(newTenantId))
        {
            IDbContextTransaction transaction;
            using (tenantScope.Elevate())
            {
                transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (transaction.ConfigureAwait(false))
            {
                await work(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

/// <summary>
/// <see cref="IMasterDataCloner"/> (BR-25): catalogue A stages its copies before the handler's flush; catalogue B copies
/// set-based after it, remapping services and clinical groups by code onto A's flushed rows (so A always runs first).
/// A's clinical groups are staged without their report-template link (the templates are B's); B links them to its copies
/// by name in the same transaction (validation Z-02).
/// Each half runs elevated, inside the provisioning transaction, with the new tenant still current. A later half
/// (pricing …) is added here in its place of the order.
/// </summary>
internal sealed class MasterDataCloner(ICatalogueACloner catalogueA, ICatalogueBCloner catalogueB, ICurrentTenantSetter tenantScope) : IMasterDataCloner
{
    public async Task StageAsync(Guid sourceTenantId, Guid targetTenantId, CancellationToken cancellationToken)
    {
        using (tenantScope.Elevate())
        {
            await catalogueA.CloneAsync(sourceTenantId, targetTenantId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task ApplyAsync(Guid sourceTenantId, Guid targetTenantId, CancellationToken cancellationToken)
    {
        using (tenantScope.Elevate())
        {
            await catalogueB.CloneAsync(sourceTenantId, targetTenantId, cancellationToken).ConfigureAwait(false);
        }
    }
}
