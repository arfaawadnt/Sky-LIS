using Platform.Domain.Pricing;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Pricing;

/// <summary>
/// Bulk adjustment of a base price list (CR 2026-10-19 C21, intake LIS-INT-PRC-001 §19 L7): the arithmetic of
/// <see cref="PriceMath.Adjust"/> (amount and percent, every rounding, negative results held at 0) and the aggregate's
/// preview (pure, scoped) and apply (one upsert of the changed rows).
/// </summary>
public sealed class PriceListToolsTests
{
    private static readonly DateTimeOffset Now = FixedClock.Instant;
    private static readonly Guid Cbc = Guid.CreateVersion7();
    private static readonly Guid Lipid = Guid.CreateVersion7();
    private static readonly Guid Tsh = Guid.CreateVersion7();

    private static BasePriceList Pl26()
    {
        var list = BasePriceList.Create("PL26", new LocalizedText("Main price list 2026", "قائمة 2026"), 2026, Now);
        list.UpsertLines([new PriceLineInput(Cbc, 180m, true), new PriceLineInput(Lipid, 300m, true), new PriceLineInput(Tsh, 90.10m, false)], Now);
        list.ClearDomainEvents();
        return list;
    }

    [Theory]
    [InlineData(180, "Percent", 10, "None", 198)]          // + 10 %
    [InlineData(180, "Percent", -10, "None", 162)]         // − 10 %
    [InlineData(100, "Percent", 3.33, "None", 103.33)]     // two decimals kept
    [InlineData(33.33, "Percent", 3.33, "None", 34.44)]    // 34.439889 → rounded to the cent, half away from zero
    [InlineData(90.10, "Amount", 5, "None", 95.10)]        // + amount
    [InlineData(90.10, "Amount", -0.10, "None", 90)]       // − amount
    [InlineData(180.50, "Percent", 10, "Whole", 199)]      // 198.55 → 199 (half away from zero)
    [InlineData(180, "Percent", 10, "Nearest5", 200)]      // 198 → 200
    [InlineData(176, "Percent", 10, "Nearest10", 190)]     // 193.6 → 190
    [InlineData(177.28, "Percent", 10, "Nearest5", 195)]   // 195.008 → 195
    [InlineData(0, "Percent", 50, "Whole", 0)]             // 0 stays 0
    [InlineData(10, "Percent", -100, "None", 0)]           // −100 % prices everything at 0 without clamping
    public void L7_Adjust_applies_the_amount_or_percent_and_the_rounding(decimal price, string mode, decimal value, string rounding, decimal expected)
    {
        var adjusted = PriceMath.Adjust(price, Enum.Parse<PriceAdjustMode>(mode), value, Enum.Parse<PriceRounding>(rounding));

        Assert.Equal(expected, adjusted.Price);
        Assert.False(adjusted.Clamped);
    }

    [Theory]
    [InlineData(180, "Amount", -200, "None")]
    [InlineData(2, "Amount", -2.5, "Whole")]       // −0.5 rounds away from zero to −1
    [InlineData(1, "Amount", -4, "Nearest5")]      // −3 → −0.6 steps → −5
    [InlineData(1, "Amount", -6, "Nearest10")]     // −5 → −0.5 steps, away from zero → −10
    [InlineData(5, "Percent", -100, "Whole")]      // −100 % gives exactly 0: see the fact below
    public void L7_A_negative_result_is_held_at_0_and_reported(decimal price, string mode, decimal value, string rounding)
    {
        var adjusted = PriceMath.Adjust(price, Enum.Parse<PriceAdjustMode>(mode), value, Enum.Parse<PriceRounding>(rounding));

        Assert.Equal(0m, adjusted.Price);
        Assert.Equal(mode == "Amount", adjusted.Clamped);
    }

    [Fact]
    public void L7_A_result_that_rounds_to_exactly_0_is_not_a_clamp()
    {
        Assert.Equal(new AdjustedPrice(0m, false), PriceMath.Adjust(2m, PriceAdjustMode.Amount, -2.4m, PriceRounding.Whole));   // −0.4 → 0
        Assert.Equal(new AdjustedPrice(0m, false), PriceMath.Adjust(2m, PriceAdjustMode.Amount, -2m, PriceRounding.None));
    }

    [Theory]
    [InlineData("Percent", 1000.01)]
    [InlineData("Percent", -100.01)]
    [InlineData("Percent", 10.005)]
    [InlineData("Amount", 100_000_000)]
    [InlineData("Amount", -100_000_000)]
    [InlineData("Amount", 0.001)]
    public void L7_An_adjustment_outside_the_bounds_is_refused_PRC_044(string mode, decimal value)
    {
        var refusal = Assert.Throws<DomainException>(() => PriceMath.Adjust(100m, Enum.Parse<PriceAdjustMode>(mode), value, PriceRounding.None));

        Assert.Equal(PricingCodes.AdjustmentInvalid, refusal.Code);
        Assert.Equal(DomainFault.Validation, refusal.Fault);
    }

    [Fact]
    public void L7_A_result_above_the_maximum_price_is_refused_PRC_046_not_capped()
    {
        var refusal = Assert.Throws<DomainException>(() => PriceMath.Adjust(PriceMath.MaxAmount, PriceAdjustMode.Percent, 1m, PriceRounding.None));

        Assert.Equal(PricingCodes.AdjustResultOutOfRange, refusal.Code);
        Assert.Equal(DomainFault.Validation, refusal.Fault);
        Assert.Equal(PriceMath.MaxAmount, PriceMath.Adjust(PriceMath.MaxAmount, PriceAdjustMode.Amount, 0m, PriceRounding.None).Price);
    }

    [Fact]
    public void L7_Preview_prices_every_line_in_list_order_and_changes_nothing()
    {
        var list = Pl26();

        var rows = list.PreviewAdjustment(PriceAdjustMode.Percent, 10m, PriceRounding.Whole, null);

        Assert.Equal([Cbc, Lipid, Tsh], rows.Select(row => row.ServiceId));
        Assert.Equal([198m, 330m, 99m], rows.Select(row => row.NewPrice));
        Assert.Equal([180m, 300m, 90.10m], rows.Select(row => row.OldPrice));
        Assert.Equal([true, true, false], rows.Select(row => row.IsActive));
        Assert.All(rows, row => Assert.True(row.Changed));
        Assert.All(rows, row => Assert.False(row.Clamped));
        Assert.Equal([180m, 300m, 90.10m], list.Lines.Select(line => line.Price));   // pure
        Assert.Empty(list.DomainEvents);
    }

    [Fact]
    public void L7_Preview_scoped_to_selected_services_prices_those_only_and_flags_an_unchanged_row()
    {
        var list = Pl26();

        var rows = list.PreviewAdjustment(PriceAdjustMode.Amount, 0m, PriceRounding.None, new HashSet<Guid> { Lipid, Tsh });

        Assert.Equal([Lipid, Tsh], rows.Select(row => row.ServiceId));
        Assert.All(rows, row => Assert.False(row.Changed));   // + 0 changes no price

        var clamped = list.PreviewAdjustment(PriceAdjustMode.Amount, -100m, PriceRounding.None, new HashSet<Guid> { Tsh });
        var only = Assert.Single(clamped);
        Assert.Equal((Tsh, 90.10m, 0m, true, true), (only.ServiceId, only.OldPrice, only.NewPrice, only.Clamped, only.Changed));
    }

    [Fact]
    public void L7_A_scope_id_the_list_does_not_hold_is_refused_PRC_045()
    {
        var list = Pl26();

        var refusal = Assert.Throws<DomainException>(() => list.PreviewAdjustment(PriceAdjustMode.Percent, 10m, PriceRounding.None, new HashSet<Guid> { Cbc, Guid.CreateVersion7() }));

        Assert.Equal(PricingCodes.AdjustScopeUnknown, refusal.Code);
        Assert.Equal(DomainFault.Validation, refusal.Fault);
    }

    [Fact]
    public void L7_Apply_upserts_the_changed_rows_only_keeps_the_active_flags_and_raises_PricesChanged_once()
    {
        var list = Pl26();
        var rows = list.PreviewAdjustment(PriceAdjustMode.Amount, 20m, PriceRounding.None, new HashSet<Guid> { Cbc, Tsh });
        var unchanged = new PriceAdjustment(Lipid, 300m, 300m, true, false);

        var changes = list.ApplyAdjustment([.. rows, unchanged], Now);

        Assert.Equal(new PriceLineChanges(0, 2), changes);
        Assert.Equal(200m, list.Lines.Single(line => line.ServiceId == Cbc).Price);
        Assert.Equal(300m, list.Lines.Single(line => line.ServiceId == Lipid).Price);
        var tsh = list.Lines.Single(line => line.ServiceId == Tsh);
        Assert.Equal((110.10m, false), (tsh.Price, tsh.IsActive));
        var raised = Assert.IsType<BasePriceListPricesChanged>(Assert.Single(list.DomainEvents));
        Assert.Equal((0, 2), (raised.Added, raised.Changed));
    }

    [Fact]
    public void L7_Apply_with_no_changed_row_writes_nothing_and_raises_nothing()
    {
        var list = Pl26();
        var rows = list.PreviewAdjustment(PriceAdjustMode.Amount, 0m, PriceRounding.None, null);

        var changes = list.ApplyAdjustment(rows, Now);

        Assert.Equal(new PriceLineChanges(0, 0), changes);
        Assert.Empty(list.DomainEvents);
    }
}
