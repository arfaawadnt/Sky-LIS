using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Platform.Contracts.Audit;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.MultiTenancy;
using ClientTenant = Platform.Domain.PlatformAdmin.Tenant;

namespace Platform.Infrastructure.Persistence.Audit;

/// <summary>
/// The "client" of the platform tenant's audit log (SA-09; UAT C-18). The platform's own trail records actions on the
/// laboratories: a client record (<c>Tenant</c>, its id is the client's tenant id), its wallet and the wallet's movements
/// (by wallet id), and events whose payload names the client (<c>tenantId</c>). This helper derives the client of each
/// entry of a page, adds the client's names, and narrows the two audit sources to one client for the filter. Only the
/// platform tenant's log has clients: a laboratory's log is its own and the helper leaves it untouched.
/// </summary>
internal static class AuditClients
{
    // nameof(ClientTenant) would be the alias ("ClientTenant"), not the entity type the audit rows store ("Tenant"):
    // field changes of the client record itself then lost their client (UAT C-18, re-test 2026-10-11).
    private const string TenantEntity = nameof(Platform.Domain.PlatformAdmin.Tenant);
    private const string WalletEntity = nameof(Wallet);
    private const string TenantIdField = "TenantId";
    private const string WalletIdField = "WalletId";

    public static bool IsPlatformLog(Guid? currentTenant) => currentTenant is { } tenant && TenantIds.IsPlatform(tenant);

    /// <summary>The clients of the platform, by English name.</summary>
    public static async Task<IReadOnlyList<AuditClientItem>> ListAsync(AppDbContext context, CancellationToken cancellationToken) =>
        await context.Set<ClientTenant>().AsNoTracking()
            .Where(tenant => tenant.Id != TenantIds.Platform)
            .OrderBy(tenant => tenant.Name.En)
            .ThenBy(tenant => tenant.Id)
            .Select(tenant => new AuditClientItem(tenant.Id, tenant.Name.En, tenant.Name.Ar, tenant.PortalSlug))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Narrows both sources to the entries that concern <paramref name="clientTenantId"/>.</summary>
    public static async Task<(IQueryable<FieldChange> Changes, IQueryable<AuditTrailEntry> Trail)> NarrowAsync(
        AppDbContext context, IQueryable<FieldChange> changes, IQueryable<AuditTrailEntry> trail, Guid clientTenantId, CancellationToken cancellationToken)
    {
        var client = clientTenantId.ToString("D");
        var tenantIdPayload = JsonSerializer.Serialize(new Dictionary<string, string> { ["tenantId"] = client });
        var tenantIdAfter = ChangeJson(TenantIdField, client, after: true);
        var tenantIdBefore = ChangeJson(TenantIdField, client, after: false);

        // One wallet per client (INV-PLT-030); its rows and its movements' rows belong to the client too.
        var wallet = await context.Set<Wallet>().AsNoTracking().Where(candidate => candidate.TenantId == clientTenantId).Select(candidate => (Guid?)candidate.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var walletId = wallet?.ToString("D") ?? "-";
        var walletIdAfter = ChangeJson(WalletIdField, walletId, after: true);
        var walletIdPayload = JsonSerializer.Serialize(new Dictionary<string, string> { ["walletId"] = walletId });

        changes = changes.Where(change =>
            (change.EntityType == TenantEntity && change.EntityId == client)
            || (change.EntityType == WalletEntity && change.EntityId == walletId)
            || EF.Functions.JsonContains(change.Changes, tenantIdAfter)
            || EF.Functions.JsonContains(change.Changes, tenantIdBefore)
            || EF.Functions.JsonContains(change.Changes, walletIdAfter));
        trail = trail.Where(entry =>
            (entry.AggregateType == TenantEntity && entry.AggregateId == client)
            || (entry.AggregateType == WalletEntity && entry.AggregateId == walletId)
            || EF.Functions.JsonContains(entry.Payload, tenantIdPayload)
            || EF.Functions.JsonContains(entry.Payload, walletIdPayload));
        return (changes, trail);
    }

    /// <summary>The page's entries with the client each one concerns (id and names); entries of the platform's own records keep none.</summary>
    public static async Task<IReadOnlyList<AuditEntryItem>> FillAsync(AppDbContext context, IReadOnlyList<AuditEntryItem> items, CancellationToken cancellationToken)
    {
        var candidates = items.Select(item => (Item: item, Reference: ReferenceOf(item))).ToList();
        var walletIds = candidates.Select(candidate => candidate.Reference.WalletId).OfType<Guid>().Distinct().ToArray();
        var walletTenants = walletIds.Length == 0
            ? new Dictionary<Guid, Guid>()
            : await context.Set<Wallet>().AsNoTracking().Where(wallet => walletIds.Contains(wallet.Id))
                .ToDictionaryAsync(wallet => wallet.Id, wallet => wallet.TenantId, cancellationToken).ConfigureAwait(false);

        var resolved = candidates
            .Select(candidate => (candidate.Item, TenantId: candidate.Reference.TenantId
                ?? (candidate.Reference.WalletId is { } wallet && walletTenants.TryGetValue(wallet, out var owner) ? owner : (Guid?)null)))
            .Select(candidate => (candidate.Item, TenantId: candidate.TenantId is { } id && !TenantIds.IsPlatform(id) && id != Guid.Empty ? candidate.TenantId : null))
            .ToList();

        var tenantIds = resolved.Select(candidate => candidate.TenantId).OfType<Guid>().Distinct().ToArray();
        var names = tenantIds.Length == 0
            ? new Dictionary<Guid, (string En, string Ar)>()
            : (await context.Set<ClientTenant>().AsNoTracking().Where(tenant => tenantIds.Contains(tenant.Id))
                .Select(tenant => new { tenant.Id, tenant.Name.En, tenant.Name.Ar })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
                .ToDictionary(tenant => tenant.Id, tenant => (tenant.En, tenant.Ar));

        return resolved
            .Select(candidate => candidate.TenantId is { } id
                ? candidate.Item with
                {
                    ClientTenantId = id,
                    ClientNameEn = names.TryGetValue(id, out var name) ? name.En : null,
                    ClientNameAr = names.TryGetValue(id, out var nameAr) ? nameAr.Ar : null,
                }
                : candidate.Item)
            .ToList();
    }

    /// <summary>What an entry says about the client: a tenant id directly, or a wallet whose owner is the client.</summary>
    internal static (Guid? TenantId, Guid? WalletId) ReferenceOf(AuditEntryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.EntityType == TenantEntity && Guid.TryParse(item.EntityId, out var tenant))
        {
            return (tenant, null);
        }

        Guid? walletId = item.EntityType == WalletEntity && Guid.TryParse(item.EntityId, out var wallet) ? wallet : null;
        if (item.Changes is { Count: > 0 } changes)
        {
            var tenantChange = changes.FirstOrDefault(change => change.Field == TenantIdField);
            if (Guid.TryParse(tenantChange?.After ?? tenantChange?.Before, out var changed))
            {
                return (changed, null);
            }

            var walletChange = changes.FirstOrDefault(change => change.Field == WalletIdField);
            if (walletId is null && Guid.TryParse(walletChange?.After ?? walletChange?.Before, out var changedWallet))
            {
                walletId = changedWallet;
            }
        }

        if (!string.IsNullOrEmpty(item.Payload))
        {
            var (payloadTenant, payloadWallet) = PayloadIds(item.Payload);
            if (payloadTenant is not null)
            {
                return (payloadTenant, null);
            }

            walletId ??= payloadWallet;
        }

        return (null, walletId);
    }

    private static (Guid? TenantId, Guid? WalletId) PayloadIds(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            return (GuidProperty(document.RootElement, "tenantId"), GuidProperty(document.RootElement, "walletId"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static Guid? GuidProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var id) ? id : null;

    private static string ChangeJson(string field, string value, bool after) =>
        JsonSerializer.Serialize(new[] { new Dictionary<string, string> { ["field"] = field, [after ? "after" : "before"] = value } });
}
