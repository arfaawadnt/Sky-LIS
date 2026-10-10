using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Contracts.Common;
using Platform.Contracts.Pricing;
using Platform.Domain.Authorization;
using Platform.Domain.Pricing;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Pricing.Queries;

// Registers of the module (intake §14). Queries carry a policy like commands (D-04) and read projections only.

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.View)]
public sealed record ListPayersQuery(string? Search, string? Type, bool? IsActive, int? Page, int? PageSize) : IQuery<Paged<PayerListItem>>;

public sealed class ListPayersValidator : AbstractValidator<ListPayersQuery>
{
    public ListPayersValidator()
    {
        RuleFor(query => query.Search).MaximumLength(100);
        RuleFor(query => query.Type).IsEnumName(typeof(PayerType), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Type));
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
    }
}

public sealed class ListPayersHandler(IPayerRepository payers) : IQueryHandler<ListPayersQuery, Paged<PayerListItem>>
{
    public Task<Paged<PayerListItem>> Handle(ListPayersQuery query, CancellationToken cancellationToken)
    {
        PayerType? type = string.IsNullOrEmpty(query.Type) ? null : Enum.Parse<PayerType>(query.Type, ignoreCase: true);
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        return payers.ListAsync(new PayerFilter(search, type, query.IsActive), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.View)]
public sealed record GetPayerQuery(Guid PayerId) : IQuery<PayerListItem>;

public sealed class GetPayerHandler(IPayerRepository payers) : IQueryHandler<GetPayerQuery, PayerListItem>
{
    public async Task<PayerListItem> Handle(GetPayerQuery query, CancellationToken cancellationToken) =>
        await payers.GetListItemAsync(query.PayerId, cancellationToken)
        ?? throw new DomainException(PricingCodes.PayerNotFound, "Payer not found.", DomainFault.NotFound);
}

/// <summary>Every tier with its payer, for pickers (contract pricing page, copy-from dialog); <paramref name="ActiveOnly"/> drops inactive tiers and payers.</summary>
[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.View)]
public sealed record ListTierOptionsQuery(bool ActiveOnly) : IQuery<IReadOnlyList<TierOption>>;

public sealed class ListTierOptionsHandler(IPayerRepository payers) : IQueryHandler<ListTierOptionsQuery, IReadOnlyList<TierOption>>
{
    public Task<IReadOnlyList<TierOption>> Handle(ListTierOptionsQuery query, CancellationToken cancellationToken) =>
        payers.ListTierOptionsAsync(query.ActiveOnly, cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.View)]
public sealed record ListBasePriceListsQuery(string? Search, int? Year, bool? IsActive, int? Page, int? PageSize) : IQuery<Paged<BasePriceListItem>>;

public sealed class ListBasePriceListsValidator : AbstractValidator<ListBasePriceListsQuery>
{
    public ListBasePriceListsValidator()
    {
        RuleFor(query => query.Search).MaximumLength(100);
        RuleFor(query => query.Year).InclusiveBetween(BasePriceList.MinYear, BasePriceList.MaxYear).When(query => query.Year is not null);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
    }
}

public sealed class ListBasePriceListsHandler(IBasePriceListRepository lists) : IQueryHandler<ListBasePriceListsQuery, Paged<BasePriceListItem>>
{
    public Task<Paged<BasePriceListItem>> Handle(ListBasePriceListsQuery query, CancellationToken cancellationToken)
    {
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        return lists.ListAsync(new BasePriceListFilter(search, query.Year, query.IsActive), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.View)]
public sealed record GetBasePriceListQuery(Guid ListId) : IQuery<BasePriceListDetail>;

public sealed class GetBasePriceListHandler(IBasePriceListRepository lists) : IQueryHandler<GetBasePriceListQuery, BasePriceListDetail>
{
    public async Task<BasePriceListDetail> Handle(GetBasePriceListQuery query, CancellationToken cancellationToken) =>
        await lists.GetDetailAsync(query.ListId, cancellationToken)
        ?? throw new DomainException(PricingCodes.BaseListNotFound, "Base price list not found.", DomainFault.NotFound);
}

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.View)]
public sealed record GetPriceLinesQuery(Guid ListId) : IQuery<IReadOnlyList<PriceLineItem>>;

public sealed class GetPriceLinesHandler(IBasePriceListRepository lists) : IQueryHandler<GetPriceLinesQuery, IReadOnlyList<PriceLineItem>>
{
    public async Task<IReadOnlyList<PriceLineItem>> Handle(GetPriceLinesQuery query, CancellationToken cancellationToken) =>
        await lists.GetLinesAsync(query.ListId, cancellationToken)
        ?? throw new DomainException(PricingCodes.BaseListNotFound, "Base price list not found.", DomainFault.NotFound);
}

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.View)]
public sealed record ListContractPricingsQuery(Guid? PayerId) : IQuery<IReadOnlyList<ContractPricingListItem>>;

public sealed class ListContractPricingsHandler(IContractPricingRepository contracts) : IQueryHandler<ListContractPricingsQuery, IReadOnlyList<ContractPricingListItem>>
{
    public Task<IReadOnlyList<ContractPricingListItem>> Handle(ListContractPricingsQuery query, CancellationToken cancellationToken) =>
        contracts.ListAsync(query.PayerId, cancellationToken);
}

/// <summary>The CP-34 page of a tier; answers <c>Exists = false</c> (with the payer and tier) while the tier has no formula.</summary>
[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.View)]
public sealed record GetContractPricingQuery(Guid TierId) : IQuery<ContractPricingDetail>;

public sealed class GetContractPricingHandler(IPayerLookup payers, IContractPricingRepository contracts, ITenantSettings settings, IClock clock) : IQueryHandler<GetContractPricingQuery, ContractPricingDetail>
{
    public async Task<ContractPricingDetail> Handle(GetContractPricingQuery query, CancellationToken cancellationToken)
    {
        var tier = await payers.FindTierAsync(query.TierId, cancellationToken)
            ?? throw new DomainException(PricingCodes.TierNotFound, "Contract tier not found.", DomainFault.NotFound);
        var payer = await payers.FindPayerAsync(tier.PayerId, cancellationToken)
            ?? throw new DomainException(PricingCodes.PayerNotFound, "Payer not found.", DomainFault.NotFound);
        var view = await contracts.GetViewByTierAsync(tier.Id, cancellationToken);
        var currency = await settings.GetAsync(SettingKeys.DefaultCurrency, cancellationToken);
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var inEffect = view is not null && today >= view.EffectiveFrom && (view.EffectiveTo is null || today <= view.EffectiveTo.Value);

        return new ContractPricingDetail(
            view is not null, view?.Id, payer.Id, payer.NameEn, payer.NameAr, payer.Type, payer.IsActive,
            tier.Id, tier.NameEn, tier.NameAr, tier.IsActive, tier.IsDefault,
            view?.EffectiveFrom, view?.EffectiveTo, inEffect, view?.AppliedAtUtc, currency,
            view?.Terms ?? [], view?.Prices ?? []);
    }
}

/// <summary>Data of the printable review report (CP-34); the SPA renders and prints it.</summary>
[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.View)]
public sealed record GetContractPricingReviewQuery(Guid TierId) : IQuery<ContractPricingReviewDocument>;

public sealed class GetContractPricingReviewHandler(IPayerLookup payers, IContractPricingRepository contracts, ITenantSettings settings, ICurrentUser user, IClock clock) : IQueryHandler<GetContractPricingReviewQuery, ContractPricingReviewDocument>
{
    public async Task<ContractPricingReviewDocument> Handle(GetContractPricingReviewQuery query, CancellationToken cancellationToken)
    {
        var tier = await payers.FindTierAsync(query.TierId, cancellationToken)
            ?? throw new DomainException(PricingCodes.TierNotFound, "Contract tier not found.", DomainFault.NotFound);
        var payer = await payers.FindPayerAsync(tier.PayerId, cancellationToken)
            ?? throw new DomainException(PricingCodes.PayerNotFound, "Payer not found.", DomainFault.NotFound);
        var view = await contracts.GetViewByTierAsync(tier.Id, cancellationToken)
            ?? throw new DomainException(PricingCodes.ContractPricingNotFound, "The tier has no contract pricing yet.", DomainFault.NotFound);
        var currency = await settings.GetAsync(SettingKeys.DefaultCurrency, cancellationToken);

        return new ContractPricingReviewDocument(
            payer.NameEn, payer.NameAr, payer.SearchKey, tier.NameEn, tier.NameAr,
            view.EffectiveFrom, view.EffectiveTo, view.AppliedAtUtc, currency, view.Terms, view.Prices,
            user.DisplayName, clock.UtcNow);
    }
}

/// <summary>
/// CP-12 price query (BR-06): resolves the tier (tier given; payer only → its default or only active tier; neither →
/// the cash payer, which the endpoint provisions first, intake Q1), then prices through <see cref="IPriceLookup"/>.
/// Unknown or unpriced services answer <c>Priced = false</c>.
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.View)]
public sealed record QuotePricesQuery(Guid? PayerId, Guid? ContractTierId, IReadOnlyList<Guid> ServiceIds) : IQuery<PriceQuote>;

public sealed class QuotePricesValidator : AbstractValidator<QuotePricesQuery>
{
    public const int MaxServices = 200;

    public QuotePricesValidator()
    {
        RuleFor(query => query.ServiceIds).NotNull().Must(ids => ids is { Count: <= MaxServices }).WithMessage($"Quote at most {MaxServices} services at once.");
        RuleForEach(query => query.ServiceIds).NotEmpty();
    }
}

public sealed class QuotePricesHandler(IPayerLookup payers, IPriceLookup prices, ITenantSettings settings) : IQueryHandler<QuotePricesQuery, PriceQuote>
{
    public async Task<PriceQuote> Handle(QuotePricesQuery query, CancellationToken cancellationToken)
    {
        var tier = await ResolveTierAsync(query, cancellationToken);
        var payer = await payers.FindPayerAsync(tier.PayerId, cancellationToken)
            ?? throw new DomainException(PricingCodes.PayerNotFound, "Payer not found.", DomainFault.NotFound);
        var lines = await prices.QuoteAsync(payer.Id, tier.Id, query.ServiceIds.Distinct().ToList(), cancellationToken);
        var currency = await settings.GetAsync(SettingKeys.DefaultCurrency, cancellationToken);

        return new PriceQuote(
            payer.Id, tier.Id, payer.NameEn, payer.NameAr, tier.NameEn, tier.NameAr, currency, lines,
            lines.Sum(line => line.ListPrice), lines.Sum(line => line.PatientShare), lines.Sum(line => line.PayerShare));
    }

    private async Task<ContractTierSummary> ResolveTierAsync(QuotePricesQuery query, CancellationToken cancellationToken)
    {
        if (query.ContractTierId is { } tierId)
        {
            var tier = await payers.FindTierAsync(tierId, cancellationToken)
                ?? throw new DomainException(PricingCodes.TierNotFound, "Contract tier not found.", DomainFault.NotFound);
            if (query.PayerId is { } payerId && payerId != tier.PayerId)
            {
                throw new DomainException(PricingCodes.TierPayerMismatch, "The contract tier does not belong to the payer.", DomainFault.Validation);
            }

            return tier;
        }

        if (query.PayerId is { } onlyPayerId)
        {
            _ = await payers.FindPayerAsync(onlyPayerId, cancellationToken)
                ?? throw new DomainException(PricingCodes.PayerNotFound, "Payer not found.", DomainFault.NotFound);
            var tiers = await payers.ListTiersAsync(onlyPayerId, cancellationToken);
            var active = tiers.Where(tier => tier.IsActive).ToList();
            return tiers.FirstOrDefault(tier => tier.IsDefault)
                ?? (active.Count == 1 ? active[0] : throw new DomainException(PricingCodes.TierRequired, "Choose a contract tier of the payer.", DomainFault.Validation));
        }

        var cash = await payers.FindDefaultAsync(cancellationToken)
            ?? throw new DomainException(PricingCodes.PayerNotFound, "The cash payer is not provisioned yet.", DomainFault.NotFound);
        return await payers.FindTierAsync(cash.ContractTierId, cancellationToken)
            ?? throw new DomainException(PricingCodes.TierNotFound, "Contract tier not found.", DomainFault.NotFound);
    }
}
