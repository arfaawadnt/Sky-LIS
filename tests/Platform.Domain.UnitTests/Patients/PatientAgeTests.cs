using Platform.Domain.Patients;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Patients;

/// <summary>The calendar rule of the computed age and of the age ↔ date-of-birth sync (LDM §4.2 "Birth Date ↔ age D M Y").</summary>
public sealed class PatientAgeTests
{
    [Theory]
    [InlineData("1990-05-01", "2026-10-09", 36, 5, 8)]
    [InlineData("2026-10-09", "2026-10-09", 0, 0, 0)]
    [InlineData("2026-09-30", "2026-10-09", 0, 0, 9)]
    [InlineData("2024-02-29", "2026-02-28", 2, 0, 0)]
    [InlineData("2024-02-29", "2028-02-29", 4, 0, 0)]
    [InlineData("2024-02-29", "2026-03-01", 2, 0, 1)]
    [InlineData("2026-01-31", "2026-03-01", 0, 1, 1)]
    [InlineData("2000-12-31", "2026-01-01", 25, 0, 1)]
    [InlineData("2027-01-01", "2026-10-09", 0, 0, 0)]
    public void Between_counts_whole_years_months_and_days(string birth, string on, int years, int months, int days) =>
        Assert.Equal(new PatientAge(years, months, days), PatientAge.Between(DateOnly.Parse(birth, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(on, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void A_stated_age_implies_a_birth_date_that_gives_the_same_age_back()
    {
        var on = new DateOnly(2026, 10, 9);
        var stated = new PatientAge(34, 2, 5);

        var birth = stated.BirthDateOn(on);

        Assert.Equal(new DateOnly(1992, 8, 4), birth);
        Assert.Equal(stated, PatientAge.Between(birth, on));
    }

    [Fact]
    public void Require_refuses_an_impossible_age_with_PAT_006()
    {
        Assert.Equal(new PatientAge(150, 11, 31), PatientAge.Require(150, 11, 31));
        var refusal = Assert.Throws<DomainException>(() => PatientAge.Require(0, 12, 0));
        Assert.Equal((PatientCodes.AgeInvalid, DomainFault.Validation), (refusal.Code, refusal.Fault));
    }
}
