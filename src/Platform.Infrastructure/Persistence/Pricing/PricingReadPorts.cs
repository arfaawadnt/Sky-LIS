using Microsoft.EntityFrameworkCore;
using Platform.Application.Pricing;
using Platform.Contracts.Pricing;
using Platform.Domain.Pricing;
using Platform.SharedKernel.Abstractions;

namespace Platform.Infrastructure.Persistence.Pricing;

/// <summary>
/// <see cref="IPriceLookup"/> over the module's tables (intake §5 "Quote resolution", BR-06). Reads only, no tracking, a
/// handful of indexed queries per call; total (never throws for unknown ids). Today is the server's UTC date (intake Q5).
/// </summary>
internal sealed class PriceLookup(AppDbContext context, IClock clock) : IPriceLookup
{
    public async Task<IReadOnlyList<PriceQuoteLine>> QuoteAsync(Guid? payerId, Guid? contractTierId, IReadOnlyList<Guid> serviceIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceIds);
        var ids = serviceIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var tier = await ResolveTierAsync(payerId, contractTierId, cancellationToken).ConfigureAwait(false);
        if (tier is null)
        {
            // Neither payer nor tier and no cash payer provisioned yet: cash prices from the default list.
            return payerId is null && contractTierId is null ? await DefaultListQuoteAsync(ids, cancellationToken).ConfigureAwait(false) : Unpriced(ids);
        }

        if (!tier.IsActive || !tier.PayerIsActive)
        {
            return Unpriced(ids);
        }

        var pricing = await context.Set<ContractPricing>().AsNoTracking()
            .Where(candidate => candidate.ContractTierId == tier.Id)
            .Select(candidate => new { candidate.Id, candidate.EffectiveFrom, candidate.EffectiveTo, candidate.AppliedAtUtc })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (pricing?.AppliedAtUtc is not null)
        {
            var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
            if (today < pricing.EffectiveFrom || (pricing.EffectiveTo is { } to && today > to))
            {
                return Unpriced(ids);
            }

            var keys = ids.ToArray();
            var rows = await context.Set<ContractPrice>().AsNoTracking()
                .Where(price => price.ContractPricingId == pricing.Id && keys.Contains(price.ServiceId))
                .Select(price => new PriceQuoteLine(price.ServiceId, price.ListPrice, price.PatientShare, price.PayerShare, price.AlertCondition, true))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var byService = rows.ToDictionary(row => row.ServiceId);
            return ids.Select(id => byService.TryGetValue(id, out var line) ? line : UnpricedLine(id)).ToList();
        }

        return tier.IsDefault ? await DefaultListQuoteAsync(ids, cancellationToken).ConfigureAwait(false) : Unpriced(ids);
    }

    private async Task<TierRow?> ResolveTierAsync(Guid? payerId, Guid? contractTierId, CancellationToken cancellationToken)
    {
        if (contractTierId is { } tierId)
        {
            var tier = await Tiers(candidate => candidate.Id == tierId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            return tier is not null && (payerId is null || payerId == tier.PayerId) ? tier : null;
        }

        if (payerId is { } onlyPayer)
        {
            var tiers = await Tiers(candidate => candidate.PayerId == onlyPayer).ToListAsync(cancellationToken).ConfigureAwait(false);
            var active = tiers.Where(candidate => candidate.IsActive).ToList();
            return tiers.FirstOrDefault(candidate => candidate.IsDefault) ?? (active.Count == 1 ? active[0] : null);
        }

        var cash = await PricingReads.FindDefaultAsync(context, cancellationToken).ConfigureAwait(false);
        return cash is null ? null : await Tiers(candidate => candidate.Id == cash.ContractTierId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The cash rule (intake Q2): the default list at 100 % / 0 %, active lines only.</summary>
    private async Task<IReadOnlyList<PriceQuoteLine>> DefaultListQuoteAsync(List<Guid> ids, CancellationToken cancellationToken)
    {
        var listId = await PricingReads.CashDefaultListIdAsync(context, cancellationToken).ConfigureAwait(false);
        if (listId is null)
        {
            return Unpriced(ids);
        }

        var keys = ids.ToArray();
        var prices = await context.Set<PriceLine>().AsNoTracking()
            .Where(line => line.PriceListId == listId && line.IsActive && keys.Contains(line.ServiceId))
            .Select(line => new { line.ServiceId, line.Price })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byService = prices.ToDictionary(line => line.ServiceId, line => line.Price);
        return ids.Select(id => byService.TryGetValue(id, out var price) ? new PriceQuoteLine(id, price, price, 0m, null, true) : UnpricedLine(id)).ToList();
    }

    private IQueryable<TierRow> Tiers(System.Linq.Expressions.Expression<Func<ContractTier, bool>> predicate) =>
        from tier in context.Set<ContractTier>().AsNoTracking().Where(predicate)
        join payer in context.Set<Payer>().AsNoTracking() on tier.PayerId equals payer.Id
        select new TierRow(tier.Id, tier.PayerId, tier.IsActive, payer.IsActive, tier.IsDefault);

    private static List<PriceQuoteLine> Unpriced(IEnumerable<Guid> ids) => ids.Select(UnpricedLine).ToList();

    private static PriceQuoteLine UnpricedLine(Guid serviceId) => new(serviceId, 0m, 0m, 0m, null, false);

    private sealed record TierRow(Guid Id, Guid PayerId, bool IsActive, bool PayerIsActive, bool IsDefault);
}

/// <summary><see cref="IPayerLookup"/> over <c>app.payers</c> and <c>app.contract_tiers</c>.</summary>
internal sealed class PayerLookup(AppDbContext context) : IPayerLookup
{
    public async Task<PayerSummary?> FindPayerAsync(Guid payerId, CancellationToken cancellationToken)
    {
        var row = await context.Set<Payer>().AsNoTracking()
            .Where(payer => payer.Id == payerId)
            .Select(payer => new { payer.Id, NameEn = payer.Name.En, NameAr = payer.Name.Ar, payer.SearchKey, payer.Type, payer.IsActive, payer.IsDefault })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : new PayerSummary(row.Id, row.NameEn, row.NameAr, row.SearchKey, row.Type.ToString(), row.IsActive, row.IsDefault);
    }

    public async Task<ContractTierSummary?> FindTierAsync(Guid contractTierId, CancellationToken cancellationToken) =>
        (await TierRows(tier => tier.Id == contractTierId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false))?.ToSummary();

    public async Task<IReadOnlyList<ContractTierSummary>> ListTiersAsync(Guid payerId, CancellationToken cancellationToken)
    {
        var rows = await TierRows(tier => tier.PayerId == payerId).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.OrderByDescending(row => row.IsDefault).ThenBy(row => row.NameEn, StringComparer.OrdinalIgnoreCase).Select(row => row.ToSummary()).ToList();
    }

    public Task<DefaultPayerIds?> FindDefaultAsync(CancellationToken cancellationToken) => PricingReads.FindDefaultAsync(context, cancellationToken);

    private IQueryable<TierSummaryRow> TierRows(System.Linq.Expressions.Expression<Func<ContractTier, bool>> predicate) =>
        from tier in context.Set<ContractTier>().AsNoTracking().Where(predicate)
        join payer in context.Set<Payer>().AsNoTracking() on tier.PayerId equals payer.Id
        select new TierSummaryRow(tier.Id, tier.PayerId, payer.Type, tier.Name.En, tier.Name.Ar, tier.IsActive, payer.IsActive, tier.IsDefault);

    private sealed record TierSummaryRow(Guid Id, Guid PayerId, PayerType PayerType, string NameEn, string NameAr, bool IsActive, bool PayerIsActive, bool IsDefault)
    {
        public ContractTierSummary ToSummary() => new(Id, PayerId, PayerType.ToString(), NameEn, NameAr, IsActive, PayerIsActive, IsDefault);
    }
}

/// <summary>
/// <see cref="IPricingServiceCatalog"/>: the catalogue's <c>app.services</c> belongs to Catalogue A (same wave), so it is read
/// by raw SQL against the agreed table name and never through the catalogue's entity types (MODULE-PATTERN §3).
/// The explicit tenant predicate doubles the RLS policy the table carries.
/// </summary>
internal sealed class PricingServiceCatalog(AppDbContext context) : IPricingServiceCatalog
{
    public async Task<IReadOnlySet<Guid>> ExistingServiceIdsAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceIds);
        if (serviceIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var tenantId = context.CurrentTenantIdOrNil;
        var ids = serviceIds.Distinct().ToArray();
        var found = await context.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM app.services WHERE tenant_id = {tenantId} AND id = ANY({ids})")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return found.ToHashSet();
    }

    public async Task<IReadOnlyDictionary<string, Guid>> ServiceIdsByCodeAsync(IReadOnlyCollection<string> codes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(codes);
        var keys = codes.Select(code => code.Trim().ToUpperInvariant()).Where(code => code.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        if (keys.Length == 0)
        {
            return new Dictionary<string, Guid>(StringComparer.Ordinal);
        }

        var tenantId = context.CurrentTenantIdOrNil;
        var rows = await context.Database
            .SqlQuery<ServiceLabelRow>($"SELECT id, code, name_en, name_ar, is_active FROM app.services WHERE tenant_id = {tenantId} AND code = ANY({keys})")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.GroupBy(row => row.Code, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().Id, StringComparer.Ordinal);
    }

    public async Task<IReadOnlyDictionary<Guid, CatalogueServiceLabel>> ServiceLabelsAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken)
    {
        var labels = await PricingReads.ServiceLabelsAsync(context, serviceIds, cancellationToken).ConfigureAwait(false);
        return labels.ToDictionary(pair => pair.Key, pair => new CatalogueServiceLabel(pair.Value.Id, pair.Value.Code, pair.Value.NameEn, pair.Value.NameAr));
    }
}

/// <summary>
/// <see cref="IServicePricesReader"/> (CR 2026-10-19 C22): the service by raw SQL on <c>app.services</c> (same-wave rule), then its
/// base lines joined to their lists and its contract prices joined to pricing, tier and payer. Reads only, no tracking.
/// </summary>
internal sealed class ServicePricesReader(AppDbContext context, IClock clock) : IServicePricesReader
{
    public async Task<ServicePricesView?> ReadAsync(Guid serviceId, CancellationToken cancellationToken)
    {
        var tenantId = context.CurrentTenantIdOrNil;
        var service = await context.Database
            .SqlQuery<ServiceHeadRow>($"SELECT id, code, name_en, name_ar, type, is_active FROM app.services WHERE tenant_id = {tenantId} AND id = {serviceId}")
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (service is null)
        {
            return null;
        }

        var basePrices = await (
            from line in context.Set<PriceLine>().AsNoTracking()
            where line.ServiceId == serviceId
            join list in context.Set<BasePriceList>().AsNoTracking() on line.PriceListId equals list.Id
            orderby list.Year descending, list.Code
            select new ServiceBasePriceItem(list.Id, list.Code, list.Name.En, list.Name.Ar, list.Year, list.IsActive, line.Price, line.IsActive))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var contractRows = await (
            from price in context.Set<ContractPrice>().AsNoTracking()
            where price.ServiceId == serviceId
            join pricing in context.Set<ContractPricing>().AsNoTracking() on price.ContractPricingId equals pricing.Id
            join tier in context.Set<ContractTier>().AsNoTracking() on pricing.ContractTierId equals tier.Id
            join payer in context.Set<Payer>().AsNoTracking() on pricing.PayerId equals payer.Id
            from source in context.Set<BasePriceList>().AsNoTracking().Where(list => list.Id == price.SourceListId).DefaultIfEmpty()
            orderby payer.IsDefault descending, payer.Name.En, tier.IsDefault descending, tier.Name.En
            select new
            {
                TierId = tier.Id,
                PayerId = payer.Id,
                PayerNameEn = payer.Name.En,
                PayerNameAr = payer.Name.Ar,
                TierNameEn = tier.Name.En,
                TierNameAr = tier.Name.Ar,
                price.ListPrice,
                price.PatientShare,
                price.PayerShare,
                price.IsExcluded,
                SourceListCode = source == null ? null : source.Code,
                TierActive = tier.IsActive,
                PayerActive = payer.IsActive,
                pricing.EffectiveFrom,
                pricing.EffectiveTo,
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var contractPrices = contractRows.Select(row => new ServiceContractPriceItem(
            row.TierId, row.PayerId, row.PayerNameEn, row.PayerNameAr, row.TierNameEn, row.TierNameAr,
            row.ListPrice, row.PatientShare, row.PayerShare, row.IsExcluded, row.SourceListCode, row.TierActive, row.PayerActive,
            today >= row.EffectiveFrom && (row.EffectiveTo == null || today <= row.EffectiveTo.Value))).ToList();

        return new ServicePricesView(service.Id, service.Code, service.NameEn, service.NameAr, service.Type, service.IsActive, string.Empty, basePrices, contractPrices);
    }
}

/// <summary>The head of one catalogue service for the service prices page (materialised from raw SQL).</summary>
internal sealed class ServiceHeadRow
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}

/// <summary>One catalogue service as the pricing projections show it (materialised from raw SQL).</summary>
internal sealed class ServiceLabelRow
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}

/// <summary>Reads shared by the repositories and the read ports of the module.</summary>
internal static class PricingReads
{
    public static async Task<DefaultPayerIds?> FindDefaultAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var row = await (
            from payer in context.Set<Payer>().AsNoTracking()
            where payer.IsDefault
            join tier in context.Set<ContractTier>().AsNoTracking() on payer.Id equals tier.PayerId
            where tier.IsDefault
            select new { PayerId = payer.Id, TierId = tier.Id })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return row is null ? null : new DefaultPayerIds(row.PayerId, row.TierId, Created: false);
    }

    /// <summary>The cash default list (intake Q2): the active list with the highest year, ties broken by the most recent creation.</summary>
    public static Task<Guid?> CashDefaultListIdAsync(AppDbContext context, CancellationToken cancellationToken) =>
        context.Set<BasePriceList>().AsNoTracking()
            .Where(list => list.IsActive)
            .OrderByDescending(list => list.Year).ThenByDescending(list => list.CreatedAtUtc).ThenByDescending(list => list.Id)
            .Select(list => (Guid?)list.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public static async Task<IReadOnlyDictionary<Guid, PriceListSnapshot>> SnapshotsAsync(AppDbContext context, IReadOnlyCollection<Guid> listIds, CancellationToken cancellationToken)
    {
        var ids = listIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, PriceListSnapshot>();
        }

        var heads = await context.Set<BasePriceList>().AsNoTracking()
            .Where(list => ids.Contains(list.Id))
            .Select(list => new { list.Id, list.Code, list.IsActive })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var lines = await context.Set<PriceLine>().AsNoTracking()
            .Where(line => ids.Contains(line.PriceListId) && line.IsActive)
            .Select(line => new { line.PriceListId, line.ServiceId, line.Price })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var byList = lines.GroupBy(line => line.PriceListId).ToDictionary(group => group.Key, group => group.ToDictionary(line => line.ServiceId, line => line.Price));
        return heads.ToDictionary(
            head => head.Id,
            head => new PriceListSnapshot(head.Id, head.Code, head.IsActive, byList.TryGetValue(head.Id, out var prices) ? prices : new Dictionary<Guid, decimal>()));
    }

    /// <summary>
    /// Codes and names of catalogue services for the grids, the CSV and the review report: <c>app.services</c> is Catalogue A's
    /// (same wave), read by raw SQL on its agreed columns (<c>id, tenant_id, code, name_en, name_ar, is_active</c>), never through
    /// its entity types. Unknown ids are absent from the result.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, ServiceLabelRow>> ServiceLabelsAsync(AppDbContext context, IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken)
    {
        var ids = serviceIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, ServiceLabelRow>();
        }

        var tenantId = context.CurrentTenantIdOrNil;
        var rows = await context.Database
            // The ad-hoc row type is mapped by the snake_case naming convention: the columns keep their own names.
            .SqlQuery<ServiceLabelRow>($"SELECT id, code, name_en, name_ar, is_active FROM app.services WHERE tenant_id = {tenantId} AND id = ANY({ids})")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.ToDictionary(row => row.Id);
    }

    /// <summary>An <c>ILIKE</c> pattern matching the text anywhere, with the wildcard characters of the input escaped.</summary>
    public static string ContainsPattern(string search)
    {
        var escaped = search.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }
}
