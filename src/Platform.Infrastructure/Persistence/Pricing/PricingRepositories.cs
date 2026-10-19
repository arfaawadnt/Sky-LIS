using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Pricing;
using Platform.Contracts.Common;
using Platform.Contracts.Pricing;
using Platform.Domain.Pricing;

namespace Platform.Infrastructure.Persistence.Pricing;

internal sealed class PayerRepository(AppDbContext context) : Repository<Payer>(context), IPayerRepository
{
    private static readonly SortColumns<Payer> Sorts = new SortColumns<Payer>(PricingSortKeys.Payers)
        .Column("nameEn", payer => payer.Name.En)
        .Column("nameAr", payer => payer.Name.Ar)
        .Column("searchKey", payer => payer.SearchKey)
        .Column("type", payer => payer.Type)
        .ThenBy(payer => payer.Name.En)
        .ThenBy(payer => payer.Id);

    public override Task<Payer?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        Set.Include(payer => payer.Tiers).FirstOrDefaultAsync(payer => payer.Id == id, cancellationToken);

    public Task<bool> SearchKeyExistsAsync(string searchKey, Guid? exceptPayerId, CancellationToken cancellationToken) =>
        Set.AnyAsync(payer => payer.SearchKey == searchKey && payer.Id != exceptPayerId, cancellationToken);

    public Task<bool> WebUsernameExistsAsync(string webUsername, Guid? exceptPayerId, CancellationToken cancellationToken) =>
        Set.AnyAsync(payer => payer.WebUsername == webUsername && payer.Id != exceptPayerId, cancellationToken);

    public Task<DefaultPayerIds?> FindDefaultAsync(CancellationToken cancellationToken) => PricingReads.FindDefaultAsync(Context, cancellationToken);

    public async Task<Paged<PayerListItem>> ListAsync(PayerFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking();
        if (filter.Search is { } search)
        {
            var pattern = PricingReads.ContainsPattern(search);
            query = query.Where(payer => EF.Functions.ILike(payer.Name.En, pattern, "\\") || EF.Functions.ILike(payer.Name.Ar, pattern, "\\") || EF.Functions.ILike(payer.SearchKey, pattern, "\\"));
        }

        if (filter.Type is { } type)
        {
            query = query.Where(payer => payer.Type == type);
        }

        if (filter.IsActive is { } active)
        {
            query = query.Where(payer => payer.IsActive == active);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await Sorts.Apply(query, filter.Sort, payers => payers.OrderByDescending(payer => payer.IsDefault).ThenBy(payer => payer.Name.En).ThenBy(payer => payer.Id))
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(PayerRow.Projection)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new Paged<PayerListItem>(rows.Select(row => row.ToItem()).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<PayerListItem?> GetListItemAsync(Guid payerId, CancellationToken cancellationToken)
    {
        var row = await Set.AsNoTracking().Where(payer => payer.Id == payerId).Select(PayerRow.Projection).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return row?.ToItem();
    }

    public async Task<IReadOnlyList<TierOption>> ListTierOptionsAsync(bool activeOnly, CancellationToken cancellationToken)
    {
        var rows = await (
            from tier in Context.Set<ContractTier>().AsNoTracking()
            join payer in Set.AsNoTracking() on tier.PayerId equals payer.Id
            where !activeOnly || (tier.IsActive && payer.IsActive)
            orderby payer.IsDefault descending, payer.Name.En, tier.IsDefault descending, tier.Name.En
            select new
            {
                tier.Id,
                tier.PayerId,
                PayerNameEn = payer.Name.En,
                PayerNameAr = payer.Name.Ar,
                payer.Type,
                NameEn = tier.Name.En,
                NameAr = tier.Name.Ar,
                tier.IsActive,
                PayerIsActive = payer.IsActive,
                tier.IsDefault,
                HasPricing = Context.Set<ContractPricing>().Any(pricing => pricing.ContractTierId == tier.Id),
            }).ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows.Select(row => new TierOption(row.Id, row.PayerId, row.PayerNameEn, row.PayerNameAr, row.Type.ToString(), row.NameEn, row.NameAr, row.IsActive, row.PayerIsActive, row.IsDefault, row.HasPricing)).ToList();
    }

    private sealed record PayerRow(Guid Id, string NameEn, string NameAr, string SearchKey, PayerType Type, bool WebPortalEnabled, string? WebUsername, string? Phone1, string? Phone2, string? WhatsApp, string? Email, bool IsActive, bool IsDefault, List<ContractTierItem> Tiers)
    {
        public static readonly System.Linq.Expressions.Expression<Func<Payer, PayerRow>> Projection = payer => new PayerRow(
            payer.Id,
            payer.Name.En,
            payer.Name.Ar,
            payer.SearchKey,
            payer.Type,
            payer.WebPortalEnabled,
            payer.WebUsername,
            payer.Phone1,
            payer.Phone2,
            payer.WhatsApp,
            payer.Email,
            payer.IsActive,
            payer.IsDefault,
            payer.Tiers.OrderByDescending(tier => tier.IsDefault).ThenBy(tier => tier.Name.En)
                .Select(tier => new ContractTierItem(tier.Id, tier.PayerId, tier.Name.En, tier.Name.Ar, tier.IsActive, tier.IsDefault))
                .ToList());

        public PayerListItem ToItem() => new(Id, NameEn, NameAr, SearchKey, Type.ToString(), WebPortalEnabled, WebUsername, Phone1, Phone2, WhatsApp, Email, IsActive, IsDefault, Tiers);
    }
}

internal sealed class BasePriceListRepository(AppDbContext context) : Repository<BasePriceList>(context), IBasePriceListRepository
{
    private static readonly SortColumns<BasePriceList> Sorts = new SortColumns<BasePriceList>(PricingSortKeys.BasePriceLists)
        .Column("code", list => list.Code)
        .Column("nameEn", list => list.Name.En)
        .Column("nameAr", list => list.Name.Ar)
        .Column("year", list => list.Year)
        .ThenBy(list => list.Code)
        .ThenBy(list => list.Id);

    public override Task<BasePriceList?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        Set.Include(list => list.Lines).FirstOrDefaultAsync(list => list.Id == id, cancellationToken);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        Set.AnyAsync(list => list.Code == code, cancellationToken);

    public async Task<IReadOnlySet<Guid>> ExistingIdsAsync(IReadOnlyCollection<Guid> listIds, CancellationToken cancellationToken)
    {
        var ids = listIds.ToArray();
        var found = await Set.AsNoTracking().Where(list => ids.Contains(list.Id)).Select(list => list.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return found.ToHashSet();
    }

    public Task<IReadOnlyDictionary<Guid, PriceListSnapshot>> SnapshotsAsync(IReadOnlyCollection<Guid> listIds, CancellationToken cancellationToken) =>
        PricingReads.SnapshotsAsync(Context, listIds, cancellationToken);

    public async Task<Paged<BasePriceListItem>> ListAsync(BasePriceListFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking();
        if (filter.Search is { } search)
        {
            var pattern = PricingReads.ContainsPattern(search);
            query = query.Where(list => EF.Functions.ILike(list.Code, pattern, "\\") || EF.Functions.ILike(list.Name.En, pattern, "\\") || EF.Functions.ILike(list.Name.Ar, pattern, "\\"));
        }

        if (filter.Year is { } year)
        {
            query = query.Where(list => list.Year == year);
        }

        if (filter.IsActive is { } active)
        {
            query = query.Where(list => list.IsActive == active);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var items = await Sorts.Apply(query, filter.Sort, lists => lists.OrderByDescending(list => list.Year).ThenBy(list => list.Code))
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(list => new BasePriceListItem(
                list.Id,
                list.Code,
                list.Name.En,
                list.Name.Ar,
                list.Year,
                list.IsActive,
                list.Lines.Count(),
                list.Lines.Count(line => line.IsActive),
                Context.Set<PricingSource>().Where(source => source.BasePriceListId == list.Id).Select(source => source.ContractPricingId).Distinct().Count(),
                list.CopiedFromListId,
                Set.Where(origin => origin.Id == list.CopiedFromListId).Select(origin => origin.Code).FirstOrDefault()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new Paged<BasePriceListItem>(items, page.Page, page.PageSize, total);
    }

    public async Task<BasePriceListDetail?> GetDetailAsync(Guid listId, CancellationToken cancellationToken)
    {
        var head = await Set.AsNoTracking()
            .Where(list => list.Id == listId)
            .Select(list => new
            {
                list.Id,
                list.Code,
                NameEn = list.Name.En,
                NameAr = list.Name.Ar,
                list.Year,
                list.IsActive,
                LineCount = list.Lines.Count(),
                ActiveLineCount = list.Lines.Count(line => line.IsActive),
                list.CopiedFromListId,
                CopiedFromCode = Set.Where(origin => origin.Id == list.CopiedFromListId).Select(origin => origin.Code).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (head is null)
        {
            return null;
        }

        var derived = await (
            from pricing in Context.Set<ContractPricing>().AsNoTracking()
            where pricing.Sources.Any(source => source.BasePriceListId == listId)
            join payer in Context.Set<Payer>().AsNoTracking() on pricing.PayerId equals payer.Id
            join tier in Context.Set<ContractTier>().AsNoTracking() on pricing.ContractTierId equals tier.Id
            orderby payer.Name.En, tier.Name.En
            select new DerivedContractItem(pricing.Id, pricing.PayerId, pricing.ContractTierId, payer.Name.En, payer.Name.Ar, tier.Name.En, tier.Name.Ar, pricing.AppliedAtUtc))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var cashDefault = await PricingReads.CashDefaultListIdAsync(Context, cancellationToken).ConfigureAwait(false);
        return new BasePriceListDetail(
            head.Id, head.Code, head.NameEn, head.NameAr, head.Year, head.IsActive, head.LineCount, head.ActiveLineCount,
            head.CopiedFromListId, head.CopiedFromCode, cashDefault == head.Id, derived);
    }

    public async Task<IReadOnlyList<PriceLineItem>?> GetLinesAsync(Guid listId, CancellationToken cancellationToken)
    {
        if (!await Set.AnyAsync(list => list.Id == listId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var lines = await Context.Set<PriceLine>().AsNoTracking()
            .Where(line => line.PriceListId == listId)
            .OrderBy(line => line.Id)
            .Select(line => new { line.Id, line.ServiceId, line.Price, line.IsActive })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var labels = await PricingReads.ServiceLabelsAsync(Context, lines.Select(line => line.ServiceId).ToList(), cancellationToken).ConfigureAwait(false);
        return lines.Select(line =>
        {
            var label = labels.GetValueOrDefault(line.ServiceId);
            return new PriceLineItem(line.Id, line.ServiceId, line.Price, line.IsActive, label?.Code, label?.NameEn, label?.NameAr);
        }).ToList();
    }
}

internal sealed class ContractPricingRepository(AppDbContext context) : Repository<ContractPricing>(context), IContractPricingRepository
{
    public override Task<ContractPricing?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        WithChildren().FirstOrDefaultAsync(pricing => pricing.Id == id, cancellationToken);

    public Task<ContractPricing?> GetForUpdateByTierAsync(Guid contractTierId, CancellationToken cancellationToken) =>
        WithChildren().FirstOrDefaultAsync(pricing => pricing.ContractTierId == contractTierId, cancellationToken);

    public async Task<IReadOnlyList<ContractPricing>> ListReferencingForUpdateAsync(Guid baseListId, CancellationToken cancellationToken) =>
        await WithChildren()
            .Where(pricing => pricing.Sources.Any(source => source.BasePriceListId == baseListId))
            .OrderBy(pricing => pricing.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<ContractPricingView?> GetViewByTierAsync(Guid contractTierId, CancellationToken cancellationToken)
    {
        var head = await Set.AsNoTracking()
            .Where(pricing => pricing.ContractTierId == contractTierId)
            .Select(pricing => new { pricing.Id, pricing.EffectiveFrom, pricing.EffectiveTo, pricing.AppliedAtUtc })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (head is null)
        {
            return null;
        }

        var terms = await (
            from source in Context.Set<PricingSource>().AsNoTracking()
            where source.ContractPricingId == head.Id
            join list in Context.Set<BasePriceList>().AsNoTracking() on source.BasePriceListId equals list.Id
            orderby source.Priority
            select new PricingFormulaTermView(source.Priority, list.Id, list.Code, list.Name.En, list.Name.Ar, list.Year, list.IsActive, source.PatientFactor, source.PayerFactor))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var rows = await (
            from price in Context.Set<ContractPrice>().AsNoTracking()
            where price.ContractPricingId == head.Id
            from list in Context.Set<BasePriceList>().AsNoTracking().Where(list => list.Id == price.SourceListId).DefaultIfEmpty()
            orderby price.Id
            select new { price.ServiceId, price.ListPrice, price.PatientShare, price.PayerShare, price.IsExcluded, price.AlertCondition, price.SourceListId, SourceListCode = list == null ? null : list.Code })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var labels = await PricingReads.ServiceLabelsAsync(Context, rows.Select(row => row.ServiceId).ToList(), cancellationToken).ConfigureAwait(false);
        var prices = rows.Select(row =>
        {
            var label = labels.GetValueOrDefault(row.ServiceId);
            return new ContractPriceItem(row.ServiceId, row.ListPrice, row.PatientShare, row.PayerShare, row.IsExcluded, row.AlertCondition, row.SourceListId, row.SourceListCode, label?.Code, label?.NameEn, label?.NameAr);
        }).ToList();

        return new ContractPricingView(head.Id, head.EffectiveFrom, head.EffectiveTo, head.AppliedAtUtc, terms, prices);
    }

    public async Task<IReadOnlyList<ContractPricingListItem>> ListAsync(Guid? payerId, CancellationToken cancellationToken) =>
        await (
            from pricing in Set.AsNoTracking()
            where payerId == null || pricing.PayerId == payerId
            join payer in Context.Set<Payer>().AsNoTracking() on pricing.PayerId equals payer.Id
            join tier in Context.Set<ContractTier>().AsNoTracking() on pricing.ContractTierId equals tier.Id
            orderby payer.IsDefault descending, payer.Name.En, tier.Name.En
            select new ContractPricingListItem(
                pricing.Id,
                pricing.PayerId,
                pricing.ContractTierId,
                payer.Name.En,
                payer.Name.Ar,
                tier.Name.En,
                tier.Name.Ar,
                pricing.EffectiveFrom,
                pricing.EffectiveTo,
                pricing.AppliedAtUtc,
                pricing.Prices.Count(),
                pricing.Prices.Count(price => price.IsExcluded),
                pricing.Prices.Count(price => price.AlertCondition != null)))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    private IQueryable<ContractPricing> WithChildren() =>
        Set.Include(pricing => pricing.Sources).Include(pricing => pricing.Prices).AsSplitQuery();
}
