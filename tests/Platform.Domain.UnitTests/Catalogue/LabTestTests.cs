using Platform.Domain.Catalogue;
using Platform.SharedKernel.Primitives;
using static Platform.Domain.UnitTests.Catalogue.CatalogueAFixtures;

namespace Platform.Domain.UnitTests.Catalogue;

/// <summary>Scenarios S-T13…S-T19 of intake LIS-INT-CAT-001: the test and its configuration per type (INV-CAT-006…009).</summary>
public sealed class LabTestTests
{
    [Fact]
    public void T13_Create_a_test_with_the_default_timing()
    {
        var test = Test(code: " glu ");

        Assert.Equal(("GLU", LabTestType.Numeric, true), (test.Code, test.Type, test.IsActive));
        Assert.Equal(LabTestTiming.Default, test.Timing);
        Assert.True(test.Timing.RunsOn(DayOfWeek.Friday));
        Assert.Empty(test.FormulaReferences);
        Assert.Equal(LabTestType.Numeric, Assert.IsType<LabTestCreated>(Assert.Single(test.DomainEvents)).Type);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    public void T13_Decimals_out_of_range_are_refused_CAT_050(int decimals)
    {
        Assert.Equal(LabTestCodes.DecimalsOutOfRange, Assert.Throws<DomainException>(() => LabTest.Create("GLU", TestDefinition() with { Decimals = decimals }, Now)).Code);
    }

    [Fact]
    public void T13_A_condition_needs_its_sample_type_CAT_030_and_a_group_is_required_CAT_041()
    {
        Assert.Equal(LabTestCodes.ConditionMismatch, Assert.Throws<DomainException>(() => LabTest.Create("GLU", TestDefinition() with { SampleConditionId = Guid.CreateVersion7() }, Now)).Code);
        Assert.Equal(LabTestCodes.ClinicalGroupRequired, Assert.Throws<DomainException>(() => LabTest.Create("GLU", TestDefinition() with { ClinicalGroupId = Guid.Empty }, Now)).Code);
    }

    [Fact]
    public void T15_Replace_range_bands_keeps_the_order_and_bumps_the_revision()
    {
        var test = Test();
        test.ClearDomainEvents();

        test.ReplaceRangeBands([Band(0, 18, low: 60m, high: 100m), Band(18, 150, low: 70m, high: 110m, panicLow: 40m, panicHigh: 400m, reportableLow: 10m, reportableHigh: 600m)], Now);

        Assert.Equal([1, 2], test.RangeBands.Select(band => band.DisplayOrder).ToArray());
        Assert.All(test.RangeBands, band => Assert.Equal(test.Id, band.LabTestId));
        Assert.Equal(1, test.Revision);
        Assert.Equal(2, Assert.IsType<LabTestRangesChanged>(Assert.Single(test.DomainEvents)).BandCount);
    }

    [Fact]
    public void T15_Bands_on_a_list_test_are_refused_CAT_051()
    {
        Assert.Equal(LabTestCodes.RangeBandsNotAllowed, Assert.Throws<DomainException>(() => Test(LabTestType.OptionalList).ReplaceRangeBands([Band(0, 150)], Now)).Code);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(10, 10)]
    [InlineData(0, 151)]
    public void T15_A_malformed_age_interval_is_refused_CAT_052(int from, int to)
    {
        Assert.Equal(LabTestCodes.RangeAgeInvalid, Assert.Throws<DomainException>(() => Test().ReplaceRangeBands([Band(from, to)], Now)).Code);
    }

    [Fact]
    public void T15_Limits_out_of_order_are_refused_CAT_053()
    {
        var test = Test();

        Assert.Equal(LabTestCodes.RangeLimitsInvalid, Assert.Throws<DomainException>(() => test.ReplaceRangeBands([Band(0, 150, low: 110m, high: 70m)], Now)).Code);
        Assert.Equal(LabTestCodes.RangeLimitsInvalid, Assert.Throws<DomainException>(() => test.ReplaceRangeBands([Band(0, 150, panicLow: 80m)], Now)).Code);
        Assert.Equal(LabTestCodes.RangeLimitsInvalid, Assert.Throws<DomainException>(() => test.ReplaceRangeBands([Band(0, 150, panicHigh: 100m)], Now)).Code);
        Assert.Equal(LabTestCodes.RangeLimitsInvalid, Assert.Throws<DomainException>(() => test.ReplaceRangeBands([Band(0, 150, panicLow: 40m, reportableLow: 50m)], Now)).Code);
        Assert.Empty(test.RangeBands);
    }

    [Fact]
    public void T15_Overlapping_bands_of_the_same_sex_and_condition_are_refused_CAT_054()
    {
        var test = Test();

        Assert.Equal(LabTestCodes.RangeBandsOverlap, Assert.Throws<DomainException>(() => test.ReplaceRangeBands([Band(0, 18), Band(17, 150)], Now)).Code);
        Assert.Equal(LabTestCodes.RangeBandsOverlap, Assert.Throws<DomainException>(() => test.ReplaceRangeBands([Band(0, 24, RangeAgeUnit.Months), Band(1, 150)], Now)).Code);

        // different sex, or a condition band over a general band, may share ages (the more specific wins, BR-08)
        test.ReplaceRangeBands([Band(0, 150, sex: GenderRestriction.Male), Band(0, 150, sex: GenderRestriction.Female), Band(0, 150), Band(0, 150, conditionId: Guid.CreateVersion7())], Now);
        Assert.Equal(4, test.RangeBands.Count);
    }

    [Fact]
    public void T15_A_band_with_nothing_to_show_is_refused_CAT_055_and_non_numeric_bands_are_text_only()
    {
        Assert.Equal(LabTestCodes.RangeBandEmpty, Assert.Throws<DomainException>(() => Test().ReplaceRangeBands([Band(0, 150, low: null, high: null)], Now)).Code);

        var nonNumeric = Test(LabTestType.NonNumeric);
        Assert.Equal(LabTestCodes.RangeLimitsInvalid, Assert.Throws<DomainException>(() => nonNumeric.ReplaceRangeBands([Band(0, 150, text: "Negative")], Now)).Code);
        Assert.Equal(LabTestCodes.RangeBandEmpty, Assert.Throws<DomainException>(() => nonNumeric.ReplaceRangeBands([Band(0, 150, low: null, high: null)], Now)).Code);

        nonNumeric.ReplaceRangeBands([Band(0, 150, low: null, high: null, text: " Negative ")], Now);
        Assert.Equal("Negative", Assert.Single(nonNumeric.RangeBands).TextRange);
    }

    [Fact]
    public void T16_Replace_list_values_with_one_placeholder_default()
    {
        var test = Test(LabTestType.RestrictedList);

        test.ReplaceListValues([Value(".", isDefault: true, isPlaceholder: true), Value("Negative"), Value("Positive", isNormal: false, isCritical: true, colour: "#c8102e")], Now);

        Assert.Equal([".", "Negative", "Positive"], test.ListValues.Select(value => value.Value).ToArray());
        Assert.Equal("#C8102E", test.ListValues[2].Colour);
        Assert.True(test.ListValues[0].IsDefault && test.ListValues[0].IsPlaceholder);
    }

    [Fact]
    public void T16_BR_19_a_default_that_reads_as_a_result_is_refused_CAT_058_and_two_defaults_CAT_059()
    {
        var test = Test(LabTestType.OptionalList);

        Assert.Equal(LabTestCodes.DefaultNotPlaceholder, Assert.Throws<DomainException>(() => test.ReplaceListValues([Value("Negative", isDefault: true)], Now)).Code);
        Assert.Equal(LabTestCodes.MultipleDefaults, Assert.Throws<DomainException>(() => test.ReplaceListValues([Value(".", true, true), Value("-", true, true)], Now)).Code);
        Assert.Equal(LabTestCodes.PlaceholderCritical, Assert.Throws<DomainException>(() => test.ReplaceListValues([Value(".", isPlaceholder: true, isCritical: true)], Now)).Code);
    }

    [Fact]
    public void T16_List_values_are_distinct_and_well_formed_CAT_057_and_only_on_list_tests_CAT_056()
    {
        var test = Test(LabTestType.OptionalList);

        Assert.Equal(LabTestCodes.ListValueInvalid, Assert.Throws<DomainException>(() => test.ReplaceListValues([Value("Negative"), Value("NEGATIVE")], Now)).Code);
        Assert.Equal(LabTestCodes.ListValueInvalid, Assert.Throws<DomainException>(() => test.ReplaceListValues([Value("Negative", colour: "red")], Now)).Code);
        Assert.Equal(LabTestCodes.ListValueInvalid, Assert.Throws<DomainException>(() => test.ReplaceListValues([Value(" ")], Now)).Code);
        Assert.Equal(LabTestCodes.ListValuesNotAllowed, Assert.Throws<DomainException>(() => Test().ReplaceListValues([Value("Negative")], Now)).Code);
    }

    [Fact]
    public void T17_Set_a_formula_keeps_the_normalised_text_and_the_distinct_references()
    {
        var test = Test(LabTestType.Formula, "LDL");
        test.ClearDomainEvents();

        test.SetFormula("[CHOL] - [HDL] - [TG] / 5", ["chol", "HDL", "TG", "CHOL"], Now);

        Assert.Equal("[CHOL] - [HDL] - [TG] / 5", test.FormulaExpression);
        Assert.Equal(["CHOL", "HDL", "TG"], test.FormulaReferences);
        Assert.IsType<LabTestFormulaChanged>(Assert.Single(test.DomainEvents));

        test.SetFormula(null, [], Now);
        Assert.Null(test.FormulaExpression);
        Assert.Empty(test.FormulaReferences);
    }

    [Fact]
    public void T17_A_formula_on_another_type_CAT_062_and_a_self_reference_CAT_065_are_refused()
    {
        Assert.Equal(LabTestCodes.FormulaNotAllowed, Assert.Throws<DomainException>(() => Test().SetFormula("[A] + 1", ["A"], Now)).Code);
        Assert.Equal(LabTestCodes.FormulaSelfReference, Assert.Throws<DomainException>(() => Test(LabTestType.Formula, "LDL").SetFormula("[LDL] + 1", ["LDL"], Now)).Code);
    }

    [Fact]
    public void T18_Paragraphs_belong_to_formatted_text_tests_CAT_068_and_are_well_formed_CAT_069()
    {
        var histology = Test(LabTestType.FormattedText, "HISTO");
        histology.ReplaceParagraphs([new ParagraphSpec("Macroscopic", "Received …"), new ParagraphSpec("Microscopic", null)], Now);
        Assert.Equal(["Macroscopic", "Microscopic"], histology.Paragraphs.Select(paragraph => paragraph.Title).ToArray());

        Assert.Equal(LabTestCodes.ParagraphInvalid, Assert.Throws<DomainException>(() => histology.ReplaceParagraphs([new ParagraphSpec(" ", "x")], Now)).Code);
        Assert.Equal(LabTestCodes.ParagraphsNotAllowed, Assert.Throws<DomainException>(() => Test().ReplaceParagraphs([new ParagraphSpec("Title", null)], Now)).Code);
    }

    [Theory]
    [InlineData(-1, 0, null, null, null, 127)]
    [InlineData(24, 25, null, null, null, 127)]
    [InlineData(24, 0, 0, null, null, 127)]
    [InlineData(24, 0, null, 0d, null, 127)]
    [InlineData(24, 0, null, null, 0, 127)]
    [InlineData(24, 0, null, null, null, 0)]
    [InlineData(24, 0, null, null, null, 128)]
    public void T19_Timing_out_of_range_is_refused_CAT_070(int duration, int minimum, int? frequency, double? delta, int? window, int days)
    {
        var timing = new LabTestTiming(duration, minimum, frequency, FrequencyCheckAction.Prevent, (decimal?)delta, window, days);

        Assert.Equal(LabTestCodes.TimingInvalid, Assert.Throws<DomainException>(() => Test().SetTiming(timing, Now)).Code);
    }

    [Fact]
    public void T19_Set_timing_with_weekdays_frequency_and_delta()
    {
        var test = Test();
        var weekdays = (1 << (int)DayOfWeek.Sunday) | (1 << (int)DayOfWeek.Monday) | (1 << (int)DayOfWeek.Tuesday) | (1 << (int)DayOfWeek.Wednesday) | (1 << (int)DayOfWeek.Thursday);

        test.SetTiming(new LabTestTiming(48, 24, 30, FrequencyCheckAction.Prevent, 25m, 90, weekdays), Now);

        Assert.Equal((48, 24, 30, FrequencyCheckAction.Prevent, 25m, 90), (test.DurationHours, test.MinDurationHours, test.FrequencyCheckDays!.Value, test.FrequencyAction, test.DeltaPercent!.Value, test.DeltaWindowDays!.Value));
        Assert.False(test.Timing.RunsOn(DayOfWeek.Friday));
        Assert.True(test.Timing.RunsOn(DayOfWeek.Sunday));
    }

    [Fact]
    public void T14_A_type_change_is_refused_while_configuration_of_the_old_type_exists_CAT_071()
    {
        var test = Test();
        test.ReplaceRangeBands([Band(0, 150)], Now);

        Assert.Equal(LabTestCodes.TypeChangeBlocked, Assert.Throws<DomainException>(() => test.Change(TestDefinition(LabTestType.OptionalList), Now)).Code);
        Assert.Equal(LabTestCodes.TypeChangeBlocked, Assert.Throws<DomainException>(() => test.Change(TestDefinition(LabTestType.NonNumeric), Now)).Code);

        test.Change(TestDefinition(LabTestType.Formula) with { ClinicalGroupId = test.ClinicalGroupId }, Now);   // bands stay valid for a formula
        Assert.Equal(LabTestType.Formula, test.Type);

        test.ReplaceRangeBands([], Now);
        test.Change(TestDefinition(LabTestType.RestrictedList), Now);
        Assert.Equal(LabTestType.RestrictedList, test.Type);
    }

    [Fact]
    public void Deactivate_and_reactivate_are_idempotent()
    {
        var test = Test();
        test.ClearDomainEvents();

        test.Deactivate(Now);
        test.Deactivate(Now);
        Assert.False(test.IsActive);
        test.Reactivate(Now);

        Assert.Collection(test.DomainEvents, first => Assert.IsType<LabTestDeactivated>(first), second => Assert.IsType<LabTestReactivated>(second));
    }
}
