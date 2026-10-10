using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Patients;

/// <summary>
/// An age in years, months and days (the "D M Y" of LDM §4.2). <see cref="Between"/> is the one calendar rule used
/// for the computed age, for the age ↔ date-of-birth sync and for the estimate of a date of birth from a stated age.
/// </summary>
public readonly record struct PatientAge(int Years, int Months, int Days)
{
    public const int MaxYears = 150;

    public static PatientAge Zero => new(0, 0, 0);

    public bool IsValid => Years is >= 0 and <= MaxYears && Months is >= 0 and <= 11 && Days is >= 0 and <= 31;

    /// <summary>
    /// The age on <paramref name="on"/> of someone born on <paramref name="birth"/>; zero when the birth is later. Whole
    /// months are counted while the monthly anniversary (clamped to the month's last day: a 29 February birthday falls
    /// on 28 February in other years) is not after <paramref name="on"/>; the rest are days.
    /// </summary>
    public static PatientAge Between(DateOnly birth, DateOnly on)
    {
        if (birth >= on)
        {
            return Zero;
        }

        var totalMonths = ((on.Year - birth.Year) * 12) + on.Month - birth.Month;
        if (birth.AddMonths(totalMonths) > on)
        {
            totalMonths--;
        }

        var anniversary = birth.AddMonths(totalMonths);
        return new PatientAge(totalMonths / 12, totalMonths % 12, on.DayNumber - anniversary.DayNumber);
    }

    /// <summary>The date of birth implied by this age stated on <paramref name="on"/>.</summary>
    public DateOnly BirthDateOn(DateOnly on) => on.AddYears(-Years).AddMonths(-Months).AddDays(-Days);

    public static PatientAge Require(int years, int months, int days)
    {
        var age = new PatientAge(years, months, days);
        if (!age.IsValid)
        {
            throw new DomainException(PatientCodes.AgeInvalid, $"The age must be 0–{MaxYears} years, 0–11 months and 0–31 days.", DomainFault.Validation);
        }

        return age;
    }

    public override string ToString() => $"{Years}y {Months}m {Days}d";
}
