using Platform.Domain.Pricing;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Pricing;

/// <summary>Base price list scenarios of intake LIS-INT-PRC-001 §16 (L1–L5, INV-PRC-005/006).</summary>
public sealed class BasePriceListTests
{
    private static readonly DateTimeOffset Now = FixedClock.Instant;
    private static readonly Guid Cbc = Guid.CreateVersion7();
    private static readonly Guid Lipid = Guid.CreateVersion7();
    private static readonly Guid Tsh = Guid.CreateVersion7();

    private static BasePriceList Pl26()
    {
        var list = BasePriceList.Create(" pl26 ", new LocalizedText("Main price list 2026", "قائمة 2026"), 2026, Now);
        list.UpsertLines([new PriceLineInput(Cbc, 180m, true), new PriceLineInput(Lipid, 300m, true), new PriceLineInput(Tsh, 90.10m, false)], Now);
        list.ClearDomainEvents();
        return list;
    }

    [Fact]
    public void L1_Create_is_inactive_and_raises_BasePriceListCreated()
    {
        var list = BasePriceList.Create(" pl26 ", new LocalizedText("Main price list 2026", "قائمة 2026"), 2026, Now);

        Assert.Equal(("PL26", 2026, false), (list.Code, list.Year, list.IsActive));
        var raised = Assert.IsType<BasePriceListCreated>(Assert.Single(list.DomainEvents));
        Assert.Equal((list.Id, (Guid?)null, 0), (raised.ListId, raised.CopiedFromListId, raised.LineCount));
    }

    [Theory]
    [InlineData("", 2026, PricingCodes.ListCodeInvalid)]
    [InlineData("WAY-TOO-LONG-CODE-17", 2026, PricingCodes.ListCodeInvalid)]
    [InlineData("PL", 1999, PricingCodes.YearInvalid)]
    [InlineData("PL", 2101, PricingCodes.YearInvalid)]
    public void L1_Invalid_code_or_year_is_refused(string code, int year, string expected)
    {
        var refusal = Assert.Throws<DomainException>(() => BasePriceList.Create(code, new LocalizedText("L", "ق"), year, Now));

        Assert.Equal(expected, refusal.Code);
        Assert.Equal(DomainFault.Validation, refusal.Fault);
    }

    [Fact]
    public void S_L3_ok_Upsert_adds_and_changes_and_reports_the_counts()
    {
        var list = Pl26();

        var changes = list.UpsertLines([new PriceLineInput(Cbc, 190m, true), new PriceLineInput(Lipid, 300m, true), new PriceLineInput(Guid.CreateVersion7(), 50m, true)], Now);

        Assert.Equal(new PriceLineChanges(1, 1), changes);
        Assert.Equal(4, list.Lines.Count);
        Assert.Equal(190m, list.Lines.Single(line => line.ServiceId == Cbc).Price);
        var raised = Assert.IsType<BasePriceListPricesChanged>(Assert.Single(list.DomainEvents));
        Assert.Equal((1, 1), (raised.Added, raised.Changed));
    }

    [Fact]
    public void S_L3_ok_An_unchanged_batch_raises_nothing()
    {
        var list = Pl26();

        var changes = list.UpsertLines([new PriceLineInput(Cbc, 180.00m, true)], Now);

        Assert.Equal(new PriceLineChanges(0, 0), changes);
        Assert.Empty(list.DomainEvents);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10.005)]
    [InlineData(100000000)]
    public void S_L3_price_Invalid_price_is_refused_PRC_006(double price)
    {
        var list = Pl26();

        var refusal = Assert.Throws<DomainException>(() => list.UpsertLines([new PriceLineInput(Cbc, (decimal)price, true)], Now));

        Assert.Equal(PricingCodes.PriceInvalid, refusal.Code);
        Assert.Equal(180m, list.Lines.Single(line => line.ServiceId == Cbc).Price);
    }

    [Fact]
    public void L3_A_service_twice_in_one_batch_is_refused_PRC_016()
    {
        var list = Pl26();

        var refusal = Assert.Throws<DomainException>(() => list.UpsertLines([new PriceLineInput(Cbc, 1m, true), new PriceLineInput(Cbc, 2m, true)], Now));

        Assert.Equal(PricingCodes.DuplicateServiceLine, refusal.Code);
    }

    [Fact]
    public void S_L4_ok_Copy_multiplies_rounds_to_the_step_and_keeps_the_flags()
    {
        var source = Pl26();

        var copy = BasePriceList.CopyFrom(source, "pl27", new LocalizedText("Main price list 2027", "قائمة 2027"), 2027, CopyOperation.Multiply, 1.1m, 0.5m, Now);

        Assert.Equal(("PL27", false, (Guid?)source.Id), (copy.Code, copy.IsActive, copy.CopiedFromListId));
        Assert.Equal(198m, copy.Lines.Single(line => line.ServiceId == Cbc).Price);     // 180 × 1.1 = 198
        Assert.Equal(330m, copy.Lines.Single(line => line.ServiceId == Lipid).Price);   // 300 × 1.1 = 330
        var tsh = copy.Lines.Single(line => line.ServiceId == Tsh);
        Assert.Equal((99m, false), (tsh.Price, tsh.IsActive));                       // 90.10 × 1.1 = 99.11 → 99.0
        var raised = Assert.IsType<BasePriceListCreated>(Assert.Single(copy.DomainEvents));
        Assert.Equal((source.Id, 3), (raised.CopiedFromListId!.Value, raised.LineCount));
    }

    [Fact]
    public void L4_Copy_adds_an_amount()
    {
        var copy = BasePriceList.CopyFrom(Pl26(), "PL26B", new LocalizedText("Plus 15", "زائد 15"), 2026, CopyOperation.Add, 15m, 0.01m, Now);

        Assert.Equal(195m, copy.Lines.Single(line => line.ServiceId == Cbc).Price);
        Assert.Equal(105.10m, copy.Lines.Single(line => line.ServiceId == Tsh).Price);
    }

    [Fact]
    public void S_L4_neg_A_copy_that_would_price_below_zero_is_refused_PRC_012()
    {
        var refusal = Assert.Throws<DomainException>(() => BasePriceList.CopyFrom(Pl26(), "PLX", new LocalizedText("X", "س"), 2026, CopyOperation.Add, -500m, 1m, Now));

        Assert.Equal(PricingCodes.CopyResultOutOfRange, refusal.Code);
    }

    [Theory]
    [InlineData(CopyOperation.Multiply, 0, 1)]
    [InlineData(CopyOperation.Multiply, 11, 1)]
    [InlineData(CopyOperation.Multiply, 1.1, 0.3)]
    public void L4_Invalid_operation_or_step_is_refused_PRC_011(CopyOperation operation, double value, double step)
    {
        var refusal = Assert.Throws<DomainException>(() => BasePriceList.CopyFrom(Pl26(), "PLX", new LocalizedText("X", "س"), 2026, operation, (decimal)value, (decimal)step, Now));

        Assert.Equal(PricingCodes.CopyOperationInvalid, refusal.Code);
    }

    [Fact]
    public void L5_Activate_and_deactivate_are_idempotent()
    {
        var list = Pl26();

        list.Activate(Now);
        list.Activate(Now);
        list.Deactivate(Now);

        Assert.False(list.IsActive);
        Assert.Collection(list.DomainEvents, first => Assert.IsType<BasePriceListActivated>(first), second => Assert.IsType<BasePriceListDeactivated>(second));
    }

    [Fact]
    public void Snapshot_holds_only_the_active_lines()
    {
        var list = Pl26();
        list.Activate(Now);

        var snapshot = list.Snapshot();

        Assert.True(snapshot.IsActive);
        Assert.Equal(2, snapshot.Prices.Count);
        Assert.False(snapshot.Prices.ContainsKey(Tsh));
    }

    [Theory]
    [InlineData(12.37, 0.25, 12.25)]
    [InlineData(12.38, 0.25, 12.50)]
    [InlineData(12.125, 0.01, 12.13)]
    [InlineData(99.5, 1, 100)]
    public void Rounding_to_a_step_is_half_away_from_zero(double value, double step, double expected) =>
        Assert.Equal((decimal)expected, PriceMath.RoundToStep((decimal)value, (decimal)step));
}
