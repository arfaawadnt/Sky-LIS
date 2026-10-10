using Platform.Domain.Pricing;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Pricing;

/// <summary>Contract pricing scenarios of intake LIS-INT-PRC-001 §16 (C1–C6, BR-07, INV-PRC-008…012).</summary>
public sealed class ContractPricingTests
{
    private static readonly DateTimeOffset Now = FixedClock.Instant;
    private static readonly DateOnly From = new(2026, 7, 1);
    private static readonly DateOnly To = new(2027, 6, 30);
    private static readonly Guid Payer = Guid.CreateVersion7();
    private static readonly Guid Tier = Guid.CreateVersion7();
    private static readonly Guid ListA = Guid.CreateVersion7();
    private static readonly Guid ListB = Guid.CreateVersion7();
    private static readonly Guid S1 = Guid.CreateVersion7();
    private static readonly Guid S2 = Guid.CreateVersion7();
    private static readonly Guid S3 = Guid.CreateVersion7();

    /// <summary>A: S1 = 100 (S2 = 200 is an inactive line, so absent); B: S2 = 300, S3 = 50.</summary>
    private static Dictionary<Guid, PriceListSnapshot> Lists(bool aActive = true, bool bActive = true) => new()
    {
        [ListA] = new PriceListSnapshot(ListA, "A", aActive, new Dictionary<Guid, decimal> { [S1] = 100m }),
        [ListB] = new PriceListSnapshot(ListB, "B", bActive, new Dictionary<Guid, decimal> { [S2] = 300m, [S3] = 50m }),
    };

    private static ContractPricing Configured() =>
        ContractPricing.Create(Payer, Tier, From, To, [new PricingFormulaTerm(ListA, 0.1m, 0.9m), new PricingFormulaTerm(ListB, 0.2m, 0.8m)], Now);

    private static ContractPrice Price(ContractPricing pricing, Guid serviceId) => pricing.Prices.Single(row => row.ServiceId == serviceId);

    [Fact]
    public void C1_Create_records_the_formula_in_priority_order_and_prices_nothing()
    {
        var pricing = Configured();

        Assert.Equal([(1, ListA), (2, ListB)], pricing.Sources.OrderBy(source => source.Priority).Select(source => (source.Priority, source.BasePriceListId)));
        Assert.Empty(pricing.Prices);
        Assert.Null(pricing.AppliedAtUtc);
        var raised = Assert.IsType<ContractFormulaSet>(Assert.Single(pricing.DomainEvents));
        Assert.Equal((Tier, 2), (raised.ContractTierId, raised.ListCount));
    }

    [Fact]
    public void S_C1_size_Zero_or_four_lists_are_refused_PRC_008()
    {
        Assert.Equal(PricingCodes.FormulaSizeInvalid, Assert.Throws<DomainException>(() => ContractPricing.Create(Payer, Tier, From, To, [], Now)).Code);
        PricingFormulaTerm[] four = [new(Guid.CreateVersion7(), 1, 0), new(Guid.CreateVersion7(), 1, 0), new(Guid.CreateVersion7(), 1, 0), new(Guid.CreateVersion7(), 1, 0)];
        Assert.Equal(PricingCodes.FormulaSizeInvalid, Assert.Throws<DomainException>(() => ContractPricing.Create(Payer, Tier, From, To, four, Now)).Code);
    }

    [Fact]
    public void S_C1_size_A_list_twice_PRC_009_a_factor_of_11_PRC_007_and_inverted_dates_PRC_010_are_refused()
    {
        Assert.Equal(PricingCodes.FormulaDuplicateList, Assert.Throws<DomainException>(() => ContractPricing.Create(Payer, Tier, From, To, [new(ListA, 1, 0), new(ListA, 0.5m, 0.5m)], Now)).Code);
        Assert.Equal(PricingCodes.FactorInvalid, Assert.Throws<DomainException>(() => ContractPricing.Create(Payer, Tier, From, To, [new(ListA, 11m, 0)], Now)).Code);
        Assert.Equal(PricingCodes.FactorInvalid, Assert.Throws<DomainException>(() => ContractPricing.Create(Payer, Tier, From, To, [new(ListA, 0.12345m, 0)], Now)).Code);
        Assert.Equal(PricingCodes.EffectiveDatesInvalid, Assert.Throws<DomainException>(() => ContractPricing.Create(Payer, Tier, To, From, [new(ListA, 1, 0)], Now)).Code);
    }

    [Fact]
    public void S_C2_ok_Apply_first_list_wins_and_inactive_lines_fall_through()
    {
        var pricing = Configured();
        pricing.ClearDomainEvents();

        var outcome = pricing.Apply(Lists(), Now);

        Assert.Equal(new ContractApplyOutcome(3, 3, 0, 0, 0), outcome);
        Assert.Equal((100m, 10m, 90m, (Guid?)ListA), Shares(Price(pricing, S1)));
        Assert.Equal((300m, 60m, 240m, (Guid?)ListB), Shares(Price(pricing, S2)));
        Assert.Equal((50m, 10m, 40m, (Guid?)ListB), Shares(Price(pricing, S3)));
        Assert.Equal(Now, pricing.AppliedAtUtc);
        var raised = Assert.IsType<ContractPricingApplied>(Assert.Single(pricing.DomainEvents));
        Assert.Equal((3, 3, 0, 0, 0), (raised.Priced, raised.Added, raised.Updated, raised.Removed, raised.ExcludedKept));
    }

    [Fact]
    public void C2_Apply_again_updates_changed_prices_only_and_rounds_half_away_from_zero()
    {
        var pricing = Configured();
        pricing.Apply(Lists(), Now);
        var changed = Lists();
        changed[ListB] = new PriceListSnapshot(ListB, "B", true, new Dictionary<Guid, decimal> { [S2] = 300m, [S3] = 50.25m });

        var outcome = pricing.Apply(changed, Now.AddDays(1));

        Assert.Equal(new ContractApplyOutcome(3, 0, 1, 0, 0), outcome);
        Assert.Equal((50.25m, 10.05m, 40.2m, (Guid?)ListB), Shares(Price(pricing, S3)));   // 50.25 × 0.2 = 10.05; × 0.8 = 40.20
    }

    [Fact]
    public void S_C2_excl_An_excluded_price_survives_Apply_BR_07()
    {
        var pricing = Configured();
        pricing.Apply(Lists(), Now);
        pricing.Negotiate(S1, 15m, 80m, Now);

        var outcome = pricing.Apply(Lists(), Now.AddDays(1));

        Assert.Equal(1, outcome.ExcludedKept);
        var s1 = Price(pricing, S1);
        Assert.True(s1.IsExcluded);
        Assert.Equal((15m, 80m), (s1.PatientShare, s1.PayerShare));
    }

    [Fact]
    public void S_C2_removed_A_derived_row_no_list_holds_any_more_is_removed()
    {
        var pricing = Configured();
        pricing.Apply(Lists(), Now);
        var fewer = Lists();
        fewer[ListB] = new PriceListSnapshot(ListB, "B", true, new Dictionary<Guid, decimal> { [S2] = 300m });

        var outcome = pricing.Apply(fewer, Now);

        Assert.Equal(1, outcome.Removed);
        Assert.DoesNotContain(pricing.Prices, row => row.ServiceId == S3);
    }

    [Fact]
    public void C2_An_inactive_list_takes_no_part()
    {
        var pricing = Configured();

        pricing.Apply(Lists(aActive: false), Now);

        Assert.DoesNotContain(pricing.Prices, row => row.ServiceId == S1);
        Assert.Equal(2, pricing.Prices.Count);
    }

    [Fact]
    public void S_C2_inactive_No_active_list_PRC_031_and_nothing_changes()
    {
        var pricing = Configured();

        var refusal = Assert.Throws<DomainException>(() => pricing.Apply(Lists(aActive: false, bActive: false), Now));

        Assert.Equal((PricingCodes.NoActiveList, DomainFault.Conflict), (refusal.Code, refusal.Fault));
        Assert.Null(pricing.AppliedAtUtc);
        Assert.Empty(pricing.Prices);
    }

    [Fact]
    public void S_C3_ok_Negotiate_marks_excluded_and_raises_and_creates_a_missing_row()
    {
        var pricing = Configured();
        pricing.Apply(Lists(), Now);
        pricing.ClearDomainEvents();
        var s9 = Guid.CreateVersion7();

        pricing.Negotiate(S1, 15m, 80m, Now);
        pricing.Negotiate(s9, 20m, 0m, Now);

        Assert.True(Price(pricing, S1).IsExcluded);
        Assert.Equal(100m, Price(pricing, S1).ListPrice);
        var created = Price(pricing, s9);
        Assert.Equal((20m, 20m, 0m, true, (Guid?)null), (created.ListPrice, created.PatientShare, created.PayerShare, created.IsExcluded, created.SourceListId));
        Assert.All(pricing.DomainEvents, raised => Assert.IsType<ContractPriceNegotiated>(raised));
    }

    [Fact]
    public void C3_Negative_or_over_precise_shares_are_refused_PRC_013()
    {
        var pricing = Configured();

        Assert.Equal(PricingCodes.SharesInvalid, Assert.Throws<DomainException>(() => pricing.Negotiate(S1, -1m, 0m, Now)).Code);
        Assert.Equal(PricingCodes.SharesInvalid, Assert.Throws<DomainException>(() => pricing.Negotiate(S1, 1.001m, 0m, Now)).Code);
    }

    [Fact]
    public void S_C4_ok_Clearing_the_exclusion_restores_the_formula_price_now()
    {
        var pricing = Configured();
        pricing.Apply(Lists(), Now);
        pricing.Negotiate(S1, 15m, 80m, Now);
        pricing.ClearDomainEvents();

        pricing.ClearExclusion(S1, Lists(), Now);

        var s1 = Price(pricing, S1);
        Assert.False(s1.IsExcluded);
        Assert.Equal((100m, 10m, 90m, (Guid?)ListA), Shares(s1));
        Assert.IsType<ContractPriceExclusionCleared>(Assert.Single(pricing.DomainEvents));
    }

    [Fact]
    public void C4_Clearing_an_exclusion_no_list_holds_removes_the_row_and_a_derived_row_is_refused_PRC_033()
    {
        var pricing = Configured();
        pricing.Apply(Lists(), Now);
        var s9 = Guid.CreateVersion7();
        pricing.Negotiate(s9, 20m, 0m, Now);

        pricing.ClearExclusion(s9, Lists(), Now);

        Assert.DoesNotContain(pricing.Prices, row => row.ServiceId == s9);
        Assert.Equal(PricingCodes.PriceNotExcluded, Assert.Throws<DomainException>(() => pricing.ClearExclusion(S1, Lists(), Now)).Code);
        Assert.Equal(PricingCodes.ServiceNotPriced, Assert.Throws<DomainException>(() => pricing.ClearExclusion(s9, Lists(), Now)).Code);
    }

    [Fact]
    public void C5_Set_and_clear_an_alert_and_S_C5_none_on_an_unpriced_service_PRC_032()
    {
        var pricing = Configured();
        pricing.Apply(Lists(), Now);

        pricing.SetAlert(S1, "  Prior approval required  ");
        Assert.Equal("Prior approval required", Price(pricing, S1).AlertCondition);
        pricing.SetAlert(S1, " ");
        Assert.Null(Price(pricing, S1).AlertCondition);

        Assert.Equal(PricingCodes.ServiceNotPriced, Assert.Throws<DomainException>(() => pricing.SetAlert(Guid.CreateVersion7(), "x")).Code);
        Assert.Equal(PricingCodes.AlertInvalid, Assert.Throws<DomainException>(() => pricing.SetAlert(S1, new string('x', ContractPricing.MaxAlertLength + 1))).Code);
    }

    [Fact]
    public void S_C6_ok_Copy_scales_formula_prices_and_keeps_exclusions_and_alerts()
    {
        var source = Configured();
        source.Apply(Lists(), Now);
        source.Negotiate(S1, 15m, 80m, Now);
        source.SetAlert(S2, "Attach referral letter");
        var otherTier = Guid.CreateVersion7();

        var copy = ContractPricing.CopyOf(source, Payer, otherTier, 0.5m, Now.AddHours(1));

        Assert.Equal(otherTier, copy.ContractTierId);
        Assert.Equal((From, (DateOnly?)To, (DateTimeOffset?)Now.AddHours(1)), (copy.EffectiveFrom, copy.EffectiveTo, copy.AppliedAtUtc));
        Assert.Equal([(0.05m, 0.45m), (0.1m, 0.4m)], copy.Sources.OrderBy(term => term.Priority).Select(term => (term.PatientFactor, term.PayerFactor)));
        var s1 = Price(copy, S1);
        Assert.Equal((7.5m, 40m, true), (s1.PatientShare, s1.PayerShare, s1.IsExcluded));
        Assert.Equal("Attach referral letter", Price(copy, S2).AlertCondition);
        Assert.Equal((300m, 30m, 120m), (Price(copy, S2).ListPrice, Price(copy, S2).PatientShare, Price(copy, S2).PayerShare));
        var raised = Assert.IsType<ContractPricingCopied>(Assert.Single(copy.DomainEvents));
        Assert.Equal((source.Id, Tier, 0.5m), (raised.SourceContractPricingId, raised.SourceTierId, raised.Factor));
    }

    [Fact]
    public void C6_Copy_onto_an_existing_pricing_replaces_its_prices_in_place()
    {
        var source = Configured();
        source.Apply(Lists(), Now);
        var target = ContractPricing.Create(Payer, Guid.CreateVersion7(), From, null, [new PricingFormulaTerm(ListB, 1m, 0m)], Now);
        target.Apply(Lists(), Now);
        var targetS2 = Price(target, S2);
        target.Negotiate(Guid.CreateVersion7(), 5m, 5m, Now);

        target.CopyFrom(source, 1m, Now);

        Assert.Equal(3, target.Prices.Count);
        Assert.Same(targetS2, Price(target, S2));
        Assert.Equal((300m, 60m, 240m, (Guid?)ListB), Shares(Price(target, S2)));
    }

    [Fact]
    public void S_C6_self_Copy_onto_the_same_tier_is_refused_PRC_034_and_factor_bounds_PRC_007()
    {
        var source = Configured();

        Assert.Equal(PricingCodes.CopySameTier, Assert.Throws<DomainException>(() => ContractPricing.CopyOf(source, Payer, Tier, 1m, Now)).Code);
        Assert.Equal(PricingCodes.FactorInvalid, Assert.Throws<DomainException>(() => ContractPricing.CopyOf(source, Payer, Guid.CreateVersion7(), 0m, Now)).Code);
        Assert.Equal(PricingCodes.FactorInvalid, Assert.Throws<DomainException>(() => ContractPricing.CopyOf(source, Payer, Guid.CreateVersion7(), 20m, Now)).Code);
    }

    [Fact]
    public void C2_Apply_without_a_formula_cannot_happen_and_effective_dates_bound_the_quote()
    {
        var pricing = ContractPricing.Create(Payer, Tier, From, To, [new PricingFormulaTerm(ListA, 1m, 0m)], Now);

        Assert.True(pricing.IsInEffect(From));
        Assert.True(pricing.IsInEffect(To));
        Assert.False(pricing.IsInEffect(From.AddDays(-1)));
        Assert.False(pricing.IsInEffect(To.AddDays(1)));

        pricing.SetFormula(From, null, [new PricingFormulaTerm(ListA, 1m, 0m)], Now);
        Assert.True(pricing.IsInEffect(new DateOnly(2099, 1, 1)));
    }

    private static (decimal ListPrice, decimal PatientShare, decimal PayerShare, Guid? SourceListId) Shares(ContractPrice price) =>
        (price.ListPrice, price.PatientShare, price.PayerShare, price.SourceListId);
}
