using Platform.Domain.Integration;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Integration;

/// <summary>Scenarios T1–T4 of intake LIS-INT-INT-001 on AnalyserTestMapping: slope/intercept, conversion table, range override, Auto Send.</summary>
public sealed class AnalyserTestMappingTests
{
    private static readonly DateTimeOffset Now = FixedClock.Instant;

    internal static MappingDefinition Definition(
        bool autoSend = false,
        decimal slope = 1m,
        decimal intercept = 0m,
        IReadOnlyList<ConversionSpec>? conversions = null,
        RangeOverride? range = null,
        string code = "WBC",
        int selection = 1) =>
        new(Guid.CreateVersion7(), code, "6690-2", selection, slope, intercept, null, false, null, autoSend, conversions ?? [], range, "Impedance");

    internal static AnalyserTestMapping Mapping(Analyser analyser, bool autoSend = false, decimal slope = 1m, decimal intercept = 0m, IReadOnlyList<ConversionSpec>? conversions = null) =>
        AnalyserTestMapping.Map(analyser, Definition(autoSend, slope, intercept, conversions), Now);

    [Fact]
    public void T1_Map_happy_path_raises_AnalyserTestMapped_and_Auto_Send_defaults_off()
    {
        var analyser = AnalyserTests.Active();

        var mapping = AnalyserTestMapping.Map(analyser, Definition(), Now);

        Assert.Equal((analyser.Id, "WBC", "6690-2", false, true), (mapping.AnalyserId, mapping.AnalyserCode, mapping.HostCode, mapping.AutoSend, mapping.IsActive));
        var raised = Assert.IsType<AnalyserTestMapped>(Assert.Single(mapping.DomainEvents));
        Assert.Equal((mapping.Id, mapping.TestId, false), (raised.MappingId, raised.TestId, raised.AutoSend));
    }

    [Fact]
    public void T1_Map_refused_on_an_inactive_analyser_INT_030()
    {
        var analyser = AnalyserTests.Active();
        analyser.Deactivate(Now);

        var refusal = Assert.Throws<DomainException>(() => AnalyserTestMapping.Map(analyser, Definition(), Now));
        Assert.Equal((IntegrationCodes.AnalyserInactive, DomainFault.Conflict), (refusal.Code, refusal.Fault));
    }

    [Theory]
    [InlineData(0, IntegrationCodes.SlopeInvalid)]
    public void T1_Slope_cannot_be_zero_INT_011(int slope, string code)
    {
        var refusal = Assert.Throws<DomainException>(() => AnalyserTestMapping.Map(AnalyserTests.Active(), Definition(slope: slope), Now));
        Assert.Equal(code, refusal.Code);
    }

    [Fact]
    public void T1_Result_selection_code_and_range_are_validated()
    {
        var analyser = AnalyserTests.Active();
        Assert.Equal(IntegrationCodes.ResultSelectionInvalid, Assert.Throws<DomainException>(() => AnalyserTestMapping.Map(analyser, Definition(selection: 0), Now)).Code);
        Assert.Equal(IntegrationCodes.AnalyserCodeInvalid, Assert.Throws<DomainException>(() => AnalyserTestMapping.Map(analyser, Definition(code: " "), Now)).Code);
        Assert.Equal(IntegrationCodes.RangeOverrideInvalid, Assert.Throws<DomainException>(() => AnalyserTestMapping.Map(analyser, Definition(range: new RangeOverride(10m, 5m, null)), Now)).Code);
        Assert.Equal(IntegrationCodes.ConversionsInvalid, Assert.Throws<DomainException>(() => AnalyserTestMapping.Map(analyser, Definition(conversions: [new("NEG", "Negative"), new("neg", "Negative")]), Now)).Code);
    }

    [Theory]
    [InlineData("12.30", 1, 0, "12.30")]
    [InlineData("100", 0.0555, 0, "5.55")]
    [InlineData("5.5", 18, 0, "99")]
    [InlineData("2", 1.5, -0.25, "2.75")]
    [InlineData("1e2", 2, 0, "200")]
    [InlineData("<0.01", 2, 0, "<0.01")]
    [InlineData(" POS ", 1, 0, "POS")]
    public void Transform_applies_slope_and_intercept_to_numbers_only(string raw, double slope, double intercept, string expected)
    {
        var mapping = AnalyserTestMapping.Map(AnalyserTests.Active(), Definition(slope: (decimal)slope, intercept: (decimal)intercept), Now);
        Assert.Equal(expected, mapping.Transform(raw));
    }

    [Theory]
    [InlineData("5", "Negative")]
    [InlineData("9.99", "Negative")]
    [InlineData("10", "Low positive")]
    [InlineData("50", "Positive")]
    [InlineData("pos", "Positive")]
    [InlineData("<0.01", "Not detected")]
    [InlineData("other", "other")]
    public void Transform_applies_the_first_matching_conversion_row(string raw, string expected)
    {
        var mapping = AnalyserTestMapping.Map(
            AnalyserTests.Active(),
            Definition(conversions: [new("<10", "Negative"), new(">=50", "Positive"), new(">=10", "Low positive"), new("POS", "Positive"), new("<0.01", "Not detected")]),
            Now);

        Assert.Equal(expected, mapping.Transform(raw));
        Assert.Equal([1, 2, 3, 4, 5], mapping.Conversions.Select(row => row.Order).ToArray());
    }

    [Fact]
    public void T2_Comparisons_test_the_converted_number()
    {
        var mapping = AnalyserTestMapping.Map(AnalyserTests.Active(), Definition(slope: 10m, conversions: [new(">=100", "High")]), Now);

        Assert.Equal("High", mapping.Transform("10"));
        Assert.Equal("90", mapping.Transform("9"));
    }

    [Fact]
    public void T3_Auto_Send_switch_raises_its_event_only_on_change()
    {
        var mapping = AnalyserTestMapping.Map(AnalyserTests.Active(), Definition(), Now);
        mapping.ClearDomainEvents();

        mapping.SetAutoSend(false, Now);
        Assert.Empty(mapping.DomainEvents);

        mapping.SetAutoSend(true, Now);
        var raised = Assert.IsType<AnalyserAutoSendChanged>(Assert.Single(mapping.DomainEvents));
        Assert.True(raised.Enabled);

        mapping.ClearDomainEvents();
        mapping.Update(Definition(autoSend: false) with { TestId = mapping.TestId }, Now);
        Assert.False(Assert.IsType<AnalyserAutoSendChanged>(Assert.Single(mapping.DomainEvents)).Enabled);
    }

    [Fact]
    public void T2_Range_override_is_set_and_cleared()
    {
        var mapping = AnalyserTestMapping.Map(AnalyserTests.Active(), Definition(), Now);

        mapping.SetRangeOverride(new RangeOverride(4m, 11m, " 4.0 – 11.0 "));
        Assert.Equal(new RangeOverride(4m, 11m, "4.0 – 11.0"), mapping.RangeOverride);

        mapping.SetRangeOverride(new RangeOverride(null, null, "  "));
        Assert.Null(mapping.RangeOverride);
    }

    [Fact]
    public void T2_Replace_conversions_renumbers_the_rows()
    {
        var mapping = AnalyserTestMapping.Map(AnalyserTests.Active(), Definition(conversions: [new("A", "1")]), Now);

        mapping.ReplaceConversions([new("B", "2"), new("C", "3")]);

        Assert.Equal([("B", 1), ("C", 2)], mapping.Conversions.Select(row => (row.FromValue, row.Order)).ToArray());
    }
}
