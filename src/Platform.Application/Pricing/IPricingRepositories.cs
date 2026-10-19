using Platform.Application.Abstractions.Persistence;
using Platform.Contracts.Common;
using Platform.Contracts.Pricing;
using Platform.Domain.Pricing;

namespace Platform.Application.Pricing;

/// <summary>Filters of the payer register (CP-32): search over names and search key, type, active flag; <see cref="Sort"/> before paging (B-03, <see cref="PricingSortKeys.Payers"/>).</summary>
public sealed record PayerFilter(string? Search, PayerType? Type, bool? IsActive, ListSort Sort = default);

/// <summary>Filters of the base-list register (CP-33): search over code and names, year, active flag; <see cref="Sort"/> before paging (B-03, <see cref="PricingSortKeys.BasePriceLists"/>).</summary>
public sealed record BasePriceListFilter(string? Search, int? Year, bool? IsActive, ListSort Sort = default);

/// <summary>A tier's contract pricing as the CP-34 page and the review report read it.</summary>
public sealed record ContractPricingView(
    Guid Id,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    DateTimeOffset? AppliedAtUtc,
    IReadOnlyList<PricingFormulaTermView> Terms,
    IReadOnlyList<ContractPriceItem> Prices);

/// <summary>Write side of payers plus the projections of the payer register. <see cref="IRepository{T}.GetForUpdateAsync"/> loads the tiers.</summary>
public interface IPayerRepository : IRepository<Payer>
{
    Task<bool> SearchKeyExistsAsync(string searchKey, Guid? exceptPayerId, CancellationToken cancellationToken);

    Task<bool> WebUsernameExistsAsync(string webUsername, Guid? exceptPayerId, CancellationToken cancellationToken);

    /// <summary>The tenant's cash payer and default tier (INV-PRC-003), or <see langword="null"/> before provisioning.</summary>
    Task<DefaultPayerIds?> FindDefaultAsync(CancellationToken cancellationToken);

    Task<Paged<PayerListItem>> ListAsync(PayerFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<PayerListItem?> GetListItemAsync(Guid payerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TierOption>> ListTierOptionsAsync(bool activeOnly, CancellationToken cancellationToken);
}

/// <summary>Write side of base lists plus their register. <see cref="IRepository{T}.GetForUpdateAsync"/> loads the lines.</summary>
public interface IBasePriceListRepository : IRepository<BasePriceList>
{
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    /// <summary>Which of the ids are lists of the tenant (formula validation, PRC-038).</summary>
    Task<IReadOnlySet<Guid>> ExistingIdsAsync(IReadOnlyCollection<Guid> listIds, CancellationToken cancellationToken);

    /// <summary>The active flag and the prices of the active lines of each existing list (Apply, restore), read without tracking.</summary>
    Task<IReadOnlyDictionary<Guid, PriceListSnapshot>> SnapshotsAsync(IReadOnlyCollection<Guid> listIds, CancellationToken cancellationToken);

    Task<Paged<BasePriceListItem>> ListAsync(BasePriceListFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<BasePriceListDetail?> GetDetailAsync(Guid listId, CancellationToken cancellationToken);

    /// <summary>Every line of the list ordered by creation, or <see langword="null"/> when the list does not exist.</summary>
    Task<IReadOnlyList<PriceLineItem>?> GetLinesAsync(Guid listId, CancellationToken cancellationToken);
}

/// <summary>Write side of contract pricing plus its projections. <see cref="IRepository{T}.GetForUpdateAsync"/> loads the formula and the prices.</summary>
public interface IContractPricingRepository : IRepository<ContractPricing>
{
    /// <summary>The tier's contract pricing, tracked, with formula and prices (INV-PRC-007: at most one).</summary>
    Task<ContractPricing?> GetForUpdateByTierAsync(Guid contractTierId, CancellationToken cancellationToken);

    /// <summary>Every contract pricing whose formula references the list, tracked (CP-33 Apply).</summary>
    Task<IReadOnlyList<ContractPricing>> ListReferencingForUpdateAsync(Guid baseListId, CancellationToken cancellationToken);

    Task<ContractPricingView?> GetViewByTierAsync(Guid contractTierId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ContractPricingListItem>> ListAsync(Guid? payerId, CancellationToken cancellationToken);
}

/// <summary>
/// Read port onto the catalogue's services (module Catalogue A, same wave): the adapter reads <c>app.services</c> by raw
/// SQL (MODULE-PATTERN §3 "same-wave lookups"), so no catalogue type is referenced.
/// </summary>
public interface IPricingServiceCatalog
{
    /// <summary>The ids among <paramref name="serviceIds"/> that are services of the current tenant.</summary>
    Task<IReadOnlySet<Guid>> ExistingServiceIdsAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken);

    /// <summary>The id of every catalogue service of the tenant whose code is among <paramref name="codes"/> (upper-case keys; the Excel import, C21). Unknown codes are absent.</summary>
    Task<IReadOnlyDictionary<string, Guid>> ServiceIdsByCodeAsync(IReadOnlyCollection<string> codes, CancellationToken cancellationToken);

    /// <summary>Code and names of the given services of the tenant (unknown ids absent), for answers that carry rows the grids label (C21 adjust preview).</summary>
    Task<IReadOnlyDictionary<Guid, CatalogueServiceLabel>> ServiceLabelsAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken);
}
