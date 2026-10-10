using Platform.Domain.Catalogue;
using static Platform.Domain.UnitTests.Catalogue.CatalogueAFixtures;

namespace Platform.Domain.UnitTests.Catalogue;

/// <summary>Scenario S-BR08 of intake LIS-INT-CAT-001: the band that applies resolves by sex, age and patient condition.</summary>
public sealed class ReferenceRangeResolverTests
{
    private static readonly Guid Pregnant = Guid.CreateVersion7();
    private static readonly ReferenceRangeResolver Resolver = ReferenceRangeResolver.Instance;

    private static LabTest Haemoglobin()
    {
        var test = Test(code: "HGB");
        test.ReplaceRangeBands(
        [
            Band(0, 1, low: 13.5m, high: 19.5m, text: "newborn"),          // 1: any sex, < 1 year
            Band(1, 150, low: 11m, high: 16m, text: "general"),            // 2: any sex, ≥ 1 year
            Band(18, 150, sex: GenderRestriction.Male, low: 13m, high: 17m, text: "adult male"),
            Band(18, 150, sex: GenderRestriction.Female, low: 12m, high: 15m, text: "adult female"),
            Band(14, 50, sex: GenderRestriction.Female, conditionId: Pregnant, low: 11m, high: 14m, text: "pregnant"),
        ], Now);
        return test;
    }

    [Fact]
    public void A_sex_specific_band_beats_a_band_for_any_sex()
    {
        var test = Haemoglobin();

        Assert.Equal("adult male", Resolver.Resolve(test, GenderRestriction.Male, PatientAge.Between(new DateOnly(1990, 5, 1), new DateOnly(2026, 10, 9)), null)!.TextRange);
        Assert.Equal("adult female", Resolver.Resolve(test, GenderRestriction.Female, PatientAge.Between(new DateOnly(1990, 5, 1), new DateOnly(2026, 10, 9)), null)!.TextRange);
    }

    [Fact]
    public void The_patients_condition_beats_a_general_band()
    {
        var test = Haemoglobin();
        var age = PatientAge.Between(new DateOnly(1996, 1, 1), new DateOnly(2026, 10, 9));

        Assert.Equal("pregnant", Resolver.Resolve(test, GenderRestriction.Female, age, Pregnant)!.TextRange);
        Assert.Equal("adult female", Resolver.Resolve(test, GenderRestriction.Female, age, Guid.CreateVersion7())!.TextRange);
    }

    [Fact]
    public void Age_selects_the_band_by_its_unit()
    {
        var test = Haemoglobin();

        Assert.Equal("newborn", Resolver.Resolve(test, GenderRestriction.Male, 10, null)!.TextRange);
        Assert.Equal("newborn", Resolver.Resolve(test, GenderRestriction.Male, 300, null)!.TextRange);
        Assert.Equal("general", Resolver.Resolve(test, GenderRestriction.Male, PatientAge.Between(new DateOnly(2016, 1, 1), new DateOnly(2026, 10, 9)), null)!.TextRange);
    }

    [Fact]
    public void The_narrowest_age_span_wins_among_equally_specific_bands_of_external_data()
    {
        // the aggregate refuses such overlaps (CAT-054); the rule still decides for band data from other sources
        var bands = new[]
        {
            new FakeBand(GenderRestriction.Any, 0, 1, RangeAgeUnit.Years, null, 1),
            new FakeBand(GenderRestriction.Any, 0, 30, RangeAgeUnit.Days, null, 2),
        };

        Assert.Equal(2, Resolver.Resolve(bands, GenderRestriction.Male, PatientAge.FromDays(10), null)!.DisplayOrder);
        Assert.Equal(1, Resolver.Resolve(bands, GenderRestriction.Male, PatientAge.FromDays(100), null)!.DisplayOrder);
    }

    [Fact]
    public void An_unrecorded_sex_reads_only_bands_for_any_sex()
    {
        var test = Haemoglobin();

        Assert.Equal("general", Resolver.Resolve(test, GenderRestriction.Any, PatientAge.Between(new DateOnly(1980, 1, 1), new DateOnly(2026, 10, 9)), null)!.TextRange);
    }

    [Fact]
    public void Nothing_applies_returns_null()
    {
        var test = Test(code: "PSA");
        test.ReplaceRangeBands([Band(40, 150, sex: GenderRestriction.Male, low: 0m, high: 4m)], Now);

        Assert.Null(Resolver.Resolve(test, GenderRestriction.Female, PatientAge.FromDays(20_000), null));
        Assert.Null(Resolver.Resolve(test, GenderRestriction.Male, PatientAge.FromDays(365 * 30), null));
        Assert.Null(Resolver.Resolve(Test(code: "NONE"), GenderRestriction.Male, 1000, null));
    }

    [Fact]
    public void The_upper_age_bound_is_exclusive()
    {
        var test = Test(code: "ALP");
        test.ReplaceRangeBands([Band(0, 18, text: "child", low: null, high: null), Band(18, 150, text: "adult", low: null, high: null)], Now);

        Assert.Equal("child", Resolver.Resolve(test, GenderRestriction.Any, PatientAge.Between(new DateOnly(2008, 10, 10), new DateOnly(2026, 10, 9)), null)!.TextRange);
        Assert.Equal("adult", Resolver.Resolve(test, GenderRestriction.Any, PatientAge.Between(new DateOnly(2008, 10, 9), new DateOnly(2026, 10, 9)), null)!.TextRange);
    }

    [Fact]
    public void Patient_age_counts_completed_calendar_units()
    {
        var age = PatientAge.Between(new DateOnly(2024, 1, 31), new DateOnly(2026, 2, 28));
        Assert.Equal((25, 2), (age.Months, age.Years));   // the 28th is the last day of February: the month is complete

        var notYet = PatientAge.Between(new DateOnly(2024, 3, 15), new DateOnly(2026, 3, 14));
        Assert.Equal((23, 1, 729), (notYet.Months, notYet.Years, notYet.Days));

        var approximated = PatientAge.FromDays(400);
        Assert.Equal((400, 13, 1), (approximated.Days, approximated.Months, approximated.Years));
        Assert.Throws<ArgumentOutOfRangeException>(() => PatientAge.Between(new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void The_generic_overload_resolves_any_band_shape()
    {
        var bands = new[]
        {
            new FakeBand(GenderRestriction.Any, 0, 150, RangeAgeUnit.Years, null, 1),
            new FakeBand(GenderRestriction.Female, 0, 150, RangeAgeUnit.Years, null, 2),
        };

        Assert.Equal(2, Resolver.Resolve(bands, GenderRestriction.Female, PatientAge.FromDays(10_000), null)!.DisplayOrder);
        Assert.Equal(1, Resolver.Resolve(bands, GenderRestriction.Male, PatientAge.FromDays(10_000), null)!.DisplayOrder);
    }

    private sealed record FakeBand(GenderRestriction Sex, int AgeFrom, int AgeTo, RangeAgeUnit AgeUnit, Guid? PatientConditionId, int DisplayOrder) : IReferenceBand;
}
