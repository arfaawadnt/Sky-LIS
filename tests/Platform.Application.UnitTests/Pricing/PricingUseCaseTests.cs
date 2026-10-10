using Platform.Application.Pricing.Commands;
using Platform.Application.Pricing.Queries;
using Platform.Contracts.Common;
using Platform.Contracts.Pricing;
using Platform.Domain.Pricing;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.UnitTests.Pricing;

/// <summary>The decisions the Pricing handlers take before and around the aggregates (intake LIS-INT-PRC-001 §7, §16).</summary>
public sealed class PricingUseCaseTests
{
    private static readonly DateTimeOffset Now = PricingWorld.Now;

    [Fact]
    public async Task S_P1_ok_and_dup_Create_payer_cleans_the_keys_and_refuses_duplicates_PRC_100_PRC_102()
    {
        var world = new PricingWorld();
        var handler = new CreatePayerHandler(world.Payers, world.UnitOfWork, world.Clock);

        var created = await handler.Handle(new CreatePayerCommand("AXA", "أكسا", " axa ", "insurance", true, " AXA.Portal "), CancellationToken.None);
        var payer = Assert.Single(world.Payers.Staged);
        Assert.Equal((created.Id, "AXA", PayerType.Insurance, "axa.portal"), (payer.Id, payer.SearchKey, payer.Type, payer.WebUsername));
        Assert.Equal(1, world.UnitOfWork.Commits);
        world.Payers.Commit();

        var key = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new CreatePayerCommand("Other", "أخرى", "AXA", "Credit", false, null), CancellationToken.None));
        Assert.Equal((PricingCodes.SearchKeyExists, DomainFault.Conflict), (key.Code, key.Fault));
        var web = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new CreatePayerCommand("Other", "أخرى", "OTH", "Credit", true, "axa.portal"), CancellationToken.None));
        Assert.Equal(PricingCodes.WebUsernameExists, web.Code);
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task S_P8_idem_Ensure_default_payer_creates_once_with_a_free_key_and_answers_the_existing_ids_after()
    {
        var world = new PricingWorld();
        world.Payers.Items.Add(Payer.Create(new LocalizedText("Cash scheme", "نقدي"), "CASH", PayerType.Cash, false, null, Now));
        var handler = new EnsureDefaultPayerHandler(world.Payers, world.UnitOfWork, world.Clock);

        var first = await handler.Handle(new EnsureDefaultPayerCommand(), CancellationToken.None);
        Assert.True(first.Created);
        var cash = Assert.Single(world.Payers.Staged);
        Assert.Equal(("CASH-2", true), (cash.SearchKey, cash.IsDefault));
        Assert.Equal(cash.Tiers.Single().Id, first.ContractTierId);
        world.Payers.Commit();

        var second = await handler.Handle(new EnsureDefaultPayerCommand(), CancellationToken.None);
        Assert.Equal((first.PayerId, first.ContractTierId, false), (second.PayerId, second.ContractTierId, second.Created));
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task P8_A_concurrent_first_call_that_loses_the_unique_index_answers_the_winner()
    {
        var world = new PricingWorld();
        var winner = Payer.CreateDefaultCash("CASH", Now);
        world.UnitOfWork.FailNext = new DomainException(ProblemCodes.UniqueViolation, "duplicate", DomainFault.Conflict);
        world.UnitOfWork.OnFailure = () => world.Payers.Items.Add(winner);

        var ids = await new EnsureDefaultPayerHandler(world.Payers, world.UnitOfWork, world.Clock).Handle(new EnsureDefaultPayerCommand(), CancellationToken.None);

        Assert.Equal((winner.Id, false), (ids.PayerId, ids.Created));
    }

    [Fact]
    public async Task S_L3_svc_Upsert_checks_only_services_the_list_does_not_hold_and_refuses_unknown_ones_PRC_015()
    {
        var world = new PricingWorld();
        var (held, fresh, unknown) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var list = BasePriceList.Create("PL26", new LocalizedText("Main", "رئيسية"), 2026, Now);
        list.UpsertLines([new PriceLineInput(held, 100m, true)], Now);
        world.Lists.Items.Add(list);
        world.Catalog.Known.Add(fresh);
        var handler = new UpsertPriceLinesHandler(world.Lists, world.Catalog, world.UnitOfWork, world.Clock);

        var result = await handler.Handle(new UpsertPriceLinesCommand(list.Id, [new PriceLineRequest(held, 110m, true), new PriceLineRequest(fresh, 50m, true)]), CancellationToken.None);
        Assert.Equal(new UpsertPriceLinesResult(1, 1), result);
        Assert.Equal([fresh], Assert.Single(world.Catalog.Calls));

        var refusal = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new UpsertPriceLinesCommand(list.Id, [new PriceLineRequest(unknown, 5m, true)]), CancellationToken.None));
        Assert.Equal((PricingCodes.ServiceUnknown, DomainFault.Validation), (refusal.Code, refusal.Fault));
        Assert.Equal(1, world.UnitOfWork.Commits);

        world.Catalog.Calls.Clear();
        await handler.Handle(new UpsertPriceLinesCommand(list.Id, [new PriceLineRequest(held, 120m, false)]), CancellationToken.None);
        Assert.Empty(world.Catalog.Calls);
    }

    [Fact]
    public async Task L4_Copy_refuses_a_taken_code_PRC_101_and_stages_an_inactive_copy()
    {
        var world = new PricingWorld();
        var source = BasePriceList.Create("PL26", new LocalizedText("Main", "رئيسية"), 2026, Now);
        source.UpsertLines([new PriceLineInput(Guid.CreateVersion7(), 100m, true)], Now);
        world.Lists.Items.Add(source);
        var handler = new CopyBasePriceListHandler(world.Lists, world.UnitOfWork, world.Clock);

        var taken = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new CopyBasePriceListCommand(source.Id, "pl26", "Copy", "نسخة", 2027, "Multiply", 1.1m, 1m), CancellationToken.None));
        Assert.Equal(PricingCodes.ListCodeExists, taken.Code);

        var created = await handler.Handle(new CopyBasePriceListCommand(source.Id, "pl27", "Copy", "نسخة", 2027, "add", 15m, 0.5m), CancellationToken.None);
        var copy = Assert.Single(world.Lists.Staged);
        Assert.Equal((created.Id, "PL27", false, 115m), (copy.Id, copy.Code, copy.IsActive, copy.Lines.Single().Price));
    }

    [Fact]
    public async Task L6_Applying_a_list_refuses_an_inactive_list_PRC_031_and_applies_every_derived_tier_in_one_commit()
    {
        var world = new PricingWorld();
        var service = Guid.CreateVersion7();
        var list = BasePriceList.Create("PL26", new LocalizedText("Main", "رئيسية"), 2026, Now);
        list.UpsertLines([new PriceLineInput(service, 200m, true)], Now);
        world.Lists.Items.Add(list);
        var gold = ContractPricing.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), new DateOnly(2026, 1, 1), null, [new PricingFormulaTerm(list.Id, 0.1m, 0.9m)], Now);
        var silver = ContractPricing.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), new DateOnly(2026, 1, 1), null, [new PricingFormulaTerm(list.Id, 0.3m, 0.7m)], Now);
        world.Contracts.Items.AddRange([gold, silver]);
        var handler = new ApplyBasePriceListHandler(world.Lists, world.Contracts, world.UnitOfWork, world.Clock);

        var inactive = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new ApplyBasePriceListCommand(list.Id), CancellationToken.None));
        Assert.Equal(PricingCodes.NoActiveList, inactive.Code);
        Assert.Equal(PricingCodes.BaseListNotFound, (await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new ApplyBasePriceListCommand(Guid.CreateVersion7()), CancellationToken.None))).Code);

        list.Activate(Now);
        var result = await handler.Handle(new ApplyBasePriceListCommand(list.Id), CancellationToken.None);

        Assert.Equal(new ApplyBasePriceListResult(2, 2), result);
        Assert.Equal(60m, silver.Prices.Single().PatientShare);
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task C1_Set_formula_refuses_unknown_tiers_PRC_405_and_lists_PRC_038_creates_then_updates_the_pricing()
    {
        var world = new PricingWorld();
        var payer = Payer.Create(new LocalizedText("AXA", "أكسا"), "AXA", PayerType.Insurance, false, null, Now);
        var gold = payer.AddTier(new LocalizedText("Gold", "ذهبي"), Now);
        world.Payers.Items.Add(payer);
        var list = BasePriceList.Create("PL26", new LocalizedText("Main", "رئيسية"), 2026, Now);
        world.Lists.Items.Add(list);
        var handler = new SetContractFormulaHandler(world.PayerLookup, world.Lists, world.Contracts, world.UnitOfWork, world.Clock);

        Assert.Equal(PricingCodes.TierNotFound, (await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new SetContractFormulaCommand(Guid.CreateVersion7(), new DateOnly(2026, 1, 1), null, [new PricingFormulaTermRequest(list.Id, 1m, 0m)]), CancellationToken.None))).Code);
        Assert.Equal(PricingCodes.FormulaListUnknown, (await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new SetContractFormulaCommand(gold.Id, new DateOnly(2026, 1, 1), null, [new PricingFormulaTermRequest(Guid.CreateVersion7(), 1m, 0m)]), CancellationToken.None))).Code);

        await handler.Handle(new SetContractFormulaCommand(gold.Id, new DateOnly(2026, 1, 1), null, [new PricingFormulaTermRequest(list.Id, 0.2m, 0.8m)]), CancellationToken.None);
        var pricing = Assert.Single(world.Contracts.Staged);
        Assert.Equal((payer.Id, gold.Id), (pricing.PayerId, pricing.ContractTierId));
        world.Contracts.Items.Add(pricing);

        await handler.Handle(new SetContractFormulaCommand(gold.Id, new DateOnly(2026, 7, 1), new DateOnly(2027, 6, 30), [new PricingFormulaTermRequest(list.Id, 0.5m, 0.5m)]), CancellationToken.None);
        Assert.Single(world.Contracts.Staged);
        Assert.Equal((new DateOnly(2026, 7, 1), 0.5m), (pricing.EffectiveFrom, pricing.Sources.Single().PatientFactor));
        Assert.Equal(2, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task C3_Negotiating_a_service_the_tier_does_not_price_checks_the_catalogue_PRC_015_and_C2_without_pricing_is_PRC_407()
    {
        var world = new PricingWorld();
        var tierId = Guid.CreateVersion7();
        var listId = Guid.CreateVersion7();
        Assert.Equal(PricingCodes.ContractPricingNotFound, (await Assert.ThrowsAsync<DomainException>(() => new ApplyContractPricingHandler(world.Lists, world.Contracts, world.UnitOfWork, world.Clock).Handle(new ApplyContractPricingCommand(tierId), CancellationToken.None))).Code);

        world.Contracts.Items.Add(ContractPricing.Create(Guid.CreateVersion7(), tierId, new DateOnly(2026, 1, 1), null, [new PricingFormulaTerm(listId, 1m, 0m)], Now));
        var handler = new NegotiateContractPriceHandler(world.Contracts, world.Catalog, world.UnitOfWork, world.Clock);
        var unknown = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new NegotiateContractPriceCommand(tierId, Guid.CreateVersion7(), 10m, 90m), CancellationToken.None));
        Assert.Equal(PricingCodes.ServiceUnknown, unknown.Code);

        var known = Guid.CreateVersion7();
        world.Catalog.Known.Add(known);
        await handler.Handle(new NegotiateContractPriceCommand(tierId, known, 10m, 90m), CancellationToken.None);
        world.Catalog.Calls.Clear();
        await handler.Handle(new NegotiateContractPriceCommand(tierId, known, 20m, 80m), CancellationToken.None);   // already priced: no catalogue call
        Assert.Empty(world.Catalog.Calls);
        Assert.Equal(2, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task C6_Copy_refuses_itself_PRC_034_and_a_source_without_pricing_PRC_035_and_creates_the_target()
    {
        var world = new PricingWorld();
        var payer = Payer.Create(new LocalizedText("AXA", "أكسا"), "AXA", PayerType.Insurance, false, null, Now);
        var gold = payer.AddTier(new LocalizedText("Gold", "ذهبي"), Now);
        var silver = payer.AddTier(new LocalizedText("Silver", "فضي"), Now);
        world.Payers.Items.Add(payer);
        var handler = new CopyContractPricingHandler(world.PayerLookup, world.Contracts, world.UnitOfWork, world.Clock);

        Assert.Equal(PricingCodes.CopySameTier, (await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new CopyContractPricingCommand(gold.Id, gold.Id, null), CancellationToken.None))).Code);
        Assert.Equal(PricingCodes.SourceNotConfigured, (await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new CopyContractPricingCommand(silver.Id, gold.Id, null), CancellationToken.None))).Code);

        world.Contracts.Items.Add(ContractPricing.Create(payer.Id, gold.Id, new DateOnly(2026, 1, 1), null, [new PricingFormulaTerm(Guid.CreateVersion7(), 0.2m, 0.8m)], Now));
        await handler.Handle(new CopyContractPricingCommand(silver.Id, gold.Id, null), CancellationToken.None);

        var copy = Assert.Single(world.Contracts.Staged);
        Assert.Equal((silver.Id, 0.2m), (copy.ContractTierId, copy.Sources.Single().PatientFactor));
    }

    [Fact]
    public async Task S_Q_mismatch_The_quote_resolves_the_tier_and_refuses_mismatches_PRC_036_and_ambiguity_PRC_037()
    {
        var world = new PricingWorld();
        var cash = Payer.CreateDefaultCash("CASH", Now);
        var axa = Payer.Create(new LocalizedText("AXA", "أكسا"), "AXA", PayerType.Insurance, false, null, Now);
        var gold = axa.AddTier(new LocalizedText("Gold", "ذهبي"), Now);
        world.Payers.Items.AddRange([cash, axa]);
        var handler = new QuotePricesHandler(world.PayerLookup, world.Prices, world.Settings);
        var (s1, s2) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        var quote = await handler.Handle(new QuotePricesQuery(null, gold.Id, [s1, s2, s1]), CancellationToken.None);
        Assert.Equal((axa.Id, gold.Id, "EGP", 2), (quote.PayerId, quote.ContractTierId, quote.Currency, quote.Lines.Count));
        Assert.Equal((200m, 30m, 180m), (quote.TotalListPrice, quote.TotalPatientShare, quote.TotalPayerShare));
        Assert.Equal([s1, s2], world.Prices.Calls.Single().ServiceIds);

        Assert.Equal(PricingCodes.TierPayerMismatch, (await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new QuotePricesQuery(cash.Id, gold.Id, [s1]), CancellationToken.None))).Code);
        axa.AddTier(new LocalizedText("Silver", "فضي"), Now);
        Assert.Equal(PricingCodes.TierRequired, (await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new QuotePricesQuery(axa.Id, null, [s1]), CancellationToken.None))).Code);

        var cashQuote = await handler.Handle(new QuotePricesQuery(null, null, [s1]), CancellationToken.None);
        Assert.Equal((cash.Id, cash.Tiers.Single().Id), (cashQuote.PayerId, cashQuote.ContractTierId));
        var byPayer = await handler.Handle(new QuotePricesQuery(cash.Id, null, [s1]), CancellationToken.None);
        Assert.Equal(cash.Tiers.Single().Id, byPayer.ContractTierId);
    }

    [Fact]
    public async Task The_page_of_a_tier_without_a_formula_says_so_with_the_payer_and_tier()
    {
        var world = new PricingWorld();
        var axa = Payer.Create(new LocalizedText("AXA", "أكسا"), "AXA", PayerType.Insurance, false, null, Now);
        var gold = axa.AddTier(new LocalizedText("Gold", "ذهبي"), Now);
        world.Payers.Items.Add(axa);
        var handler = new GetContractPricingHandler(world.PayerLookup, world.Contracts, world.Settings, world.Clock);

        var detail = await handler.Handle(new GetContractPricingQuery(gold.Id), CancellationToken.None);
        Assert.Equal((false, "AXA", "Gold", "EGP", false), (detail.Exists, detail.PayerNameEn, detail.TierNameEn, detail.Currency, detail.InEffect));

        world.Contracts.Items.Add(ContractPricing.Create(axa.Id, gold.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), [new PricingFormulaTerm(Guid.CreateVersion7(), 1m, 0m)], Now));
        var configured = await handler.Handle(new GetContractPricingQuery(gold.Id), CancellationToken.None);
        Assert.True(configured.Exists && configured.InEffect);

        var review = new GetContractPricingReviewHandler(world.PayerLookup, world.Contracts, world.Settings, world.User, world.Clock);
        Assert.Equal("Accounts Officer", (await review.Handle(new GetContractPricingReviewQuery(gold.Id), CancellationToken.None)).PrintedBy);
    }
}
