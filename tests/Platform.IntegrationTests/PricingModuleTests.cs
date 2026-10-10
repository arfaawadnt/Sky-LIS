using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Pricing;
using Platform.Contracts.Common;
using Platform.Domain.Pricing;
using Platform.Infrastructure.Persistence;
using Platform.IntegrationTests.Support;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.IntegrationTests;

/// <summary>
/// The engine side of intake LIS-INT-PRC-001: tenant invisibility of every pricing table, the unique indexes (search key,
/// one default payer, list code, one pricing per tier), child rows replaced through the aggregate (cascade of orphans),
/// the projections, and the read ports Registration and Billing use (IPriceLookup, IPayerLookup).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PricingModuleTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cbc = Guid.CreateVersion7();
    private static readonly Guid Lipid = Guid.CreateVersion7();
    private static readonly Guid Tsh = Guid.CreateVersion7();

    [DatabaseFact]
    public async Task Pricing_rows_of_another_tenant_are_invisible()
    {
        var (tenantA, tenantB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var (payerId, tierId) = await CreatePayerWithTierAsync(tenantA, "ISO");
        var listId = await CreateListAsync(tenantA, "ISO26", 2026, active: true, (Cbc, 100m));
        await ConfigureAndApplyAsync(tenantA, payerId, tierId, listId);

        using var scopeB = fixture.AppScope(tenantB);
        var context = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await context.Set<Payer>().IgnoreQueryFilters().CountAsync(payer => payer.Id == payerId));
        Assert.Equal(0, await context.Set<ContractTier>().IgnoreQueryFilters().CountAsync(tier => tier.Id == tierId));
        Assert.Equal(0, await context.Set<BasePriceList>().IgnoreQueryFilters().CountAsync(list => list.Id == listId));
        Assert.Equal(0, await context.Set<PriceLine>().IgnoreQueryFilters().CountAsync(line => line.PriceListId == listId));
        Assert.Equal(0, await context.Set<ContractPricing>().IgnoreQueryFilters().CountAsync(pricing => pricing.ContractTierId == tierId));
        Assert.Equal(0, await context.Set<PricingSource>().IgnoreQueryFilters().CountAsync(source => source.BasePriceListId == listId));
        Assert.Equal(0, await context.Set<ContractPrice>().IgnoreQueryFilters().CountAsync(price => price.ServiceId == Cbc && price.SourceListId == listId));
        Assert.Null(await scopeB.ServiceProvider.GetRequiredService<IPayerRepository>().GetForUpdateAsync(payerId, CancellationToken.None));

        var quote = await scopeB.ServiceProvider.GetRequiredService<IPriceLookup>().QuoteAsync(null, tierId, [Cbc], CancellationToken.None);
        Assert.False(Assert.Single(quote).Priced);
    }

    [DatabaseFact]
    public async Task Unique_indexes_refuse_a_second_search_key_default_payer_list_code_and_tier_pricing()
    {
        var tenant = Guid.CreateVersion7();
        await CreatePayerWithTierAsync(tenant, "DUP");
        var duplicateKey = await Assert.ThrowsAsync<DomainException>(() => CreatePayerWithTierAsync(tenant, "DUP"));
        Assert.Equal(ProblemCodes.UniqueViolation, duplicateKey.Code);

        await AddAsync(tenant, Payer.CreateDefaultCash("CASH", Now));
        var secondDefault = await Assert.ThrowsAsync<DomainException>(() => AddAsync(tenant, Payer.CreateDefaultCash("CASH-2", Now)));
        Assert.Equal(ProblemCodes.UniqueViolation, secondDefault.Code);

        await CreateListAsync(tenant, "PLDUP", 2026, active: false);
        var duplicateCode = await Assert.ThrowsAsync<DomainException>(() => CreateListAsync(tenant, "PLDUP", 2027, active: false));
        Assert.Equal(ProblemCodes.UniqueViolation, duplicateCode.Code);

        var (payerId, tierId) = await CreatePayerWithTierAsync(tenant, "ONE");
        var listId = await CreateListAsync(tenant, "PLONE", 2026, active: true, (Cbc, 50m));
        await AddAsync(tenant, ContractPricing.Create(payerId, tierId, new DateOnly(2026, 1, 1), null, [new PricingFormulaTerm(listId, 1m, 0m)], Now));
        var secondPricing = await Assert.ThrowsAsync<DomainException>(() => AddAsync(tenant, ContractPricing.Create(payerId, tierId, new DateOnly(2026, 1, 1), null, [new PricingFormulaTerm(listId, 1m, 0m)], Now)));
        Assert.Equal(ProblemCodes.UniqueViolation, secondPricing.Code);
    }

    [DatabaseFact]
    public async Task Apply_formula_changes_and_copies_replace_child_rows_in_the_database()
    {
        var tenant = Guid.CreateVersion7();
        var (payerId, tierId) = await CreatePayerWithTierAsync(tenant, "APL");
        var listA = await CreateListAsync(tenant, "APLA", 2026, active: true, (Cbc, 100m), (Lipid, 300m));
        var listB = await CreateListAsync(tenant, "APLB", 2026, active: true, (Lipid, 250m), (Tsh, 80m));
        await ConfigureAndApplyAsync(tenant, payerId, tierId, listA, (listB, 0.5m, 0.5m));

        using (var scope = fixture.AppScope(tenant))
        {
            var contracts = scope.ServiceProvider.GetRequiredService<IContractPricingRepository>();
            var pricing = (await contracts.GetForUpdateByTierAsync(tierId, CancellationToken.None))!;
            Assert.Equal(3, pricing.Prices.Count);
            pricing.Negotiate(Cbc, 5m, 95m, Now);
            pricing.SetFormula(new DateOnly(2026, 1, 1), null, [new PricingFormulaTerm(listB, 1m, 0m)], Now);
            var snapshots = await scope.ServiceProvider.GetRequiredService<IBasePriceListRepository>().SnapshotsAsync([listB], CancellationToken.None);
            var outcome = pricing.Apply(snapshots, Now);
            Assert.Equal((3, 1), (outcome.Priced, outcome.ExcludedKept));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using (var reader = fixture.AppScope(tenant))
        {
            var view = (await reader.ServiceProvider.GetRequiredService<IContractPricingRepository>().GetViewByTierAsync(tierId, CancellationToken.None))!;
            var term = Assert.Single(view.Terms);
            Assert.Equal(("APLB", 1m, 0m), (term.ListCode, term.PatientFactor, term.PayerFactor));
            Assert.Equal(3, view.Prices.Count);
            var cbc = view.Prices.Single(price => price.ServiceId == Cbc);
            Assert.Equal((true, 5m, 95m), (cbc.IsExcluded, cbc.PatientShare, cbc.PayerShare));
            var lipid = view.Prices.Single(price => price.ServiceId == Lipid);
            Assert.Equal((250m, 250m, 0m, "APLB"), (lipid.ListPrice, lipid.PatientShare, lipid.PayerShare, lipid.SourceListCode));
            Assert.Equal(1, await reader.ServiceProvider.GetRequiredService<AppDbContext>().Set<PricingSource>().CountAsync(source => source.ContractPricingId == view.Id));
        }

        // copy onto another tier of the same payer, then again (in-place update of the same unique keys)
        var otherTier = await AddTierAsync(tenant, payerId, "Silver APL");
        for (var round = 0; round < 2; round++)
        {
            using var scope = fixture.AppScope(tenant);
            var contracts = scope.ServiceProvider.GetRequiredService<IContractPricingRepository>();
            var source = (await contracts.GetForUpdateByTierAsync(tierId, CancellationToken.None))!;
            var target = await contracts.GetForUpdateByTierAsync(otherTier, CancellationToken.None);
            if (target is null)
            {
                contracts.Add(ContractPricing.CopyOf(source, payerId, otherTier, 0.5m, Now));
            }
            else
            {
                target.CopyFrom(source, 1m, Now);
            }

            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using var final = fixture.AppScope(tenant);
        var copied = (await final.ServiceProvider.GetRequiredService<IContractPricingRepository>().GetViewByTierAsync(otherTier, CancellationToken.None))!;
        Assert.Equal(3, copied.Prices.Count);
        Assert.Equal(95m, copied.Prices.Single(price => price.ServiceId == Cbc).PayerShare);
    }

    [DatabaseFact]
    public async Task The_quote_prices_cash_from_the_default_list_and_tiers_from_their_applied_prices()
    {
        var tenant = Guid.CreateVersion7();
        await CreateListAsync(tenant, "Q25", 2025, active: true, (Cbc, 160m));
        await CreateListAsync(tenant, "Q26", 2026, active: true, (Cbc, 180m), (Lipid, 300m));
        await CreateListAsync(tenant, "Q27", 2027, active: false, (Cbc, 200m));
        var missing = Guid.CreateVersion7();

        using (var scope = fixture.AppScope(tenant))
        {
            // before the cash payer exists: the default list (highest active year) at 100 % / 0 %
            var lines = await scope.ServiceProvider.GetRequiredService<IPriceLookup>().QuoteAsync(null, null, [Cbc, missing, Cbc], CancellationToken.None);
            Assert.Equal(2, lines.Count);
            Assert.Equal((Cbc, 180m, 180m, 0m, true), (lines[0].ServiceId, lines[0].ListPrice, lines[0].PatientShare, lines[0].PayerShare, lines[0].Priced));
            Assert.False(lines[1].Priced);
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IPayerLookup>().FindDefaultAsync(CancellationToken.None));
        }

        var cash = Payer.CreateDefaultCash("CASH", Now);
        await AddAsync(tenant, cash);
        var (payerId, tierId) = await CreatePayerWithTierAsync(tenant, "QAXA");
        var q26 = await FindListAsync(tenant, "Q26");
        await ConfigureAndApplyAsync(tenant, payerId, tierId, q26);
        using (var scope = fixture.AppScope(tenant))
        {
            var pricing = (await scope.ServiceProvider.GetRequiredService<IContractPricingRepository>().GetForUpdateByTierAsync(tierId, CancellationToken.None))!;
            pricing.SetAlert(Lipid, "Prior approval required");
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using (var scope = fixture.AppScope(tenant))
        {
            var lookup = scope.ServiceProvider.GetRequiredService<IPriceLookup>();
            var payers = scope.ServiceProvider.GetRequiredService<IPayerLookup>();
            var defaults = (await payers.FindDefaultAsync(CancellationToken.None))!;
            Assert.Equal(cash.Id, defaults.PayerId);

            var cashLines = await lookup.QuoteAsync(null, null, [Cbc], CancellationToken.None);
            Assert.Equal(180m, Assert.Single(cashLines).PatientShare);

            var tierLines = await lookup.QuoteAsync(payerId, tierId, [Lipid, Tsh], CancellationToken.None);
            Assert.Equal((300m, 30m, 270m, "Prior approval required", true), (tierLines[0].ListPrice, tierLines[0].PatientShare, tierLines[0].PayerShare, tierLines[0].AlertCondition, tierLines[0].Priced));
            Assert.False(tierLines[1].Priced);

            var payerOnly = await lookup.QuoteAsync(payerId, null, [Cbc], CancellationToken.None);
            Assert.Equal(18m, Assert.Single(payerOnly).PatientShare);   // the payer's only active tier

            var mismatch = await lookup.QuoteAsync(cash.Id, tierId, [Cbc], CancellationToken.None);
            Assert.False(Assert.Single(mismatch).Priced);

            var tier = (await payers.FindTierAsync(tierId, CancellationToken.None))!;
            Assert.Equal((payerId, "Credit", true), (tier.PayerId, tier.PayerType, tier.IsActive));
        }

        // out of the effective dates, then an inactive tier: nothing is priced
        using (var scope = fixture.AppScope(tenant))
        {
            var today = DateOnly.FromDateTime(scope.ServiceProvider.GetRequiredService<IClock>().UtcNow.UtcDateTime);
            var pricing = (await scope.ServiceProvider.GetRequiredService<IContractPricingRepository>().GetForUpdateByTierAsync(tierId, CancellationToken.None))!;
            pricing.SetFormula(today.AddDays(1), null, [new PricingFormulaTerm(q26, 0.1m, 0.9m)], Now);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using (var scope = fixture.AppScope(tenant))
        {
            Assert.False(Assert.Single(await scope.ServiceProvider.GetRequiredService<IPriceLookup>().QuoteAsync(null, tierId, [Cbc], CancellationToken.None)).Priced);
            var payer = (await scope.ServiceProvider.GetRequiredService<IPayerRepository>().GetForUpdateAsync(payerId, CancellationToken.None))!;
            payer.DeactivateTier(tierId, Now);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }
    }

    [DatabaseFact]
    public async Task Projections_join_tiers_lists_and_derived_contracts()
    {
        var tenant = Guid.CreateVersion7();
        var (payerId, tierId) = await CreatePayerWithTierAsync(tenant, "PRJ");
        var listId = await CreateListAsync(tenant, "PRJ26", 2026, active: true, (Cbc, 100m), (Lipid, 200m));
        await ConfigureAndApplyAsync(tenant, payerId, tierId, listId);

        using var scope = fixture.AppScope(tenant);
        var payers = scope.ServiceProvider.GetRequiredService<IPayerRepository>();
        var page = await payers.ListAsync(new PayerFilter("prj", null, true), PageRequest.Normalized(1, 10), CancellationToken.None);
        var item = Assert.Single(page.Items);
        Assert.Equal(("PRJ", "Credit", 1), (item.SearchKey, item.Type, item.Tiers.Count));
        Assert.True(Assert.Single(await payers.ListTierOptionsAsync(true, CancellationToken.None), option => option.Id == tierId).HasPricing);

        var lists = scope.ServiceProvider.GetRequiredService<IBasePriceListRepository>();
        var listPage = await lists.ListAsync(new BasePriceListFilter("prj", 2026, null), PageRequest.Normalized(1, 10), CancellationToken.None);
        var listItem = Assert.Single(listPage.Items);
        Assert.Equal((2, 2, 1), (listItem.LineCount, listItem.ActiveLineCount, listItem.DerivedContractCount));

        var detail = (await lists.GetDetailAsync(listId, CancellationToken.None))!;
        Assert.True(detail.IsCashDefault);
        Assert.Equal(tierId, Assert.Single(detail.DerivedContracts).ContractTierId);
        Assert.Equal(2, (await lists.GetLinesAsync(listId, CancellationToken.None))!.Count);
        Assert.Null(await lists.GetLinesAsync(Guid.CreateVersion7(), CancellationToken.None));

        var contracts = await scope.ServiceProvider.GetRequiredService<IContractPricingRepository>().ListAsync(payerId, CancellationToken.None);
        Assert.Equal((2, 0), (Assert.Single(contracts).PricedCount, contracts[0].ExcludedCount));
        Assert.Single(await scope.ServiceProvider.GetRequiredService<IContractPricingRepository>().ListReferencingForUpdateAsync(listId, CancellationToken.None));
    }

    [DatabaseFact]
    public async Task The_service_catalog_port_reads_app_services_of_the_tenant_only()
    {
        using var scope = fixture.AppScope(Guid.CreateVersion7());
        var known = await scope.ServiceProvider.GetRequiredService<IPricingServiceCatalog>().ExistingServiceIdsAsync([Guid.CreateVersion7(), Guid.CreateVersion7()], CancellationToken.None);
        Assert.Empty(known);
    }

    private async Task AddAsync<TAggregate>(Guid tenantId, TAggregate aggregate)
        where TAggregate : AggregateRoot
    {
        using var scope = fixture.AppScope(tenantId);
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<TAggregate>().Add(aggregate);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    private async Task<(Guid PayerId, Guid TierId)> CreatePayerWithTierAsync(Guid tenantId, string key)
    {
        var payer = Payer.Create(new LocalizedText($"Payer {key}", "جهة"), key, PayerType.Credit, false, null, Now);
        var tier = payer.AddTier(new LocalizedText($"Tier {key}", "فئة"), Now);
        await AddAsync(tenantId, payer);
        return (payer.Id, tier.Id);
    }

    private async Task<Guid> AddTierAsync(Guid tenantId, Guid payerId, string name)
    {
        using var scope = fixture.AppScope(tenantId);
        var payer = (await scope.ServiceProvider.GetRequiredService<IPayerRepository>().GetForUpdateAsync(payerId, CancellationToken.None))!;
        var tier = payer.AddTier(new LocalizedText(name, name), Now);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return tier.Id;
    }

    private async Task<Guid> CreateListAsync(Guid tenantId, string code, int year, bool active, params (Guid ServiceId, decimal Price)[] lines)
    {
        var list = BasePriceList.Create(code, new LocalizedText($"List {code}", "قائمة"), year, Now);
        if (lines.Length > 0)
        {
            list.UpsertLines(lines.Select(line => new PriceLineInput(line.ServiceId, line.Price, true)).ToList(), Now);
        }

        if (active)
        {
            list.Activate(Now);
        }

        await AddAsync(tenantId, list);
        return list.Id;
    }

    private async Task<Guid> FindListAsync(Guid tenantId, string code)
    {
        using var scope = fixture.AppScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<BasePriceList>().Where(list => list.Code == code).Select(list => list.Id).SingleAsync();
    }

    /// <summary>Formula [list ×0.1 / ×0.9] (+ extra terms), effective from 2026-01-01, then Apply.</summary>
    private async Task ConfigureAndApplyAsync(Guid tenantId, Guid payerId, Guid tierId, Guid listId, params (Guid ListId, decimal Patient, decimal Payer)[] extra)
    {
        using var scope = fixture.AppScope(tenantId);
        List<PricingFormulaTerm> terms = [new(listId, 0.1m, 0.9m), .. extra.Select(term => new PricingFormulaTerm(term.ListId, term.Patient, term.Payer))];
        var pricing = ContractPricing.Create(payerId, tierId, new DateOnly(2026, 1, 1), null, terms, Now);
        var snapshots = await scope.ServiceProvider.GetRequiredService<IBasePriceListRepository>().SnapshotsAsync(terms.Select(term => term.BaseListId).ToList(), CancellationToken.None);
        pricing.Apply(snapshots, Now);
        scope.ServiceProvider.GetRequiredService<IContractPricingRepository>().Add(pricing);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }
}
