namespace Platform.Domain.Catalogue;

/// <summary>
/// A patient's age in completed days, months and years, the units range bands are written in (assumption Q7).
/// <see cref="Between"/> counts calendar units from the date of birth (precise); <see cref="FromDays"/> approximates
/// months and years from a day count with average lengths (for callers that only know days).
/// </summary>
public readonly record struct PatientAge
{
    public const double DaysPerYear = 365.2425;
    public const double DaysPerMonth = DaysPerYear / 12d;

    private PatientAge(int days, int months, int years)
    {
        Days = days;
        Months = months;
        Years = years;
    }

    public int Days { get; }

    public int Months { get; }

    public int Years { get; }

    public static PatientAge FromDays(int days)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(days);
        return new PatientAge(days, (int)Math.Floor(days / DaysPerMonth), (int)Math.Floor(days / DaysPerYear));
    }

    /// <summary>Completed calendar units between the date of birth and <paramref name="onDate"/> (a birthday on the 31st completes its month on the last day of shorter months).</summary>
    public static PatientAge Between(DateOnly birthDate, DateOnly onDate)
    {
        if (onDate < birthDate)
        {
            throw new ArgumentOutOfRangeException(nameof(onDate), "The date is before the date of birth.");
        }

        var months = ((onDate.Year - birthDate.Year) * 12) + onDate.Month - birthDate.Month;
        var lastDayOfMonth = onDate.Day == DateTime.DaysInMonth(onDate.Year, onDate.Month);
        if (onDate.Day < birthDate.Day && !lastDayOfMonth)
        {
            months--;
        }

        return new PatientAge(onDate.DayNumber - birthDate.DayNumber, months, months / 12);
    }

    public int In(RangeAgeUnit unit) => unit switch
    {
        RangeAgeUnit.Days => Days,
        RangeAgeUnit.Months => Months,
        _ => Years,
    };
}

/// <summary>BR-08: the reference range of a test resolves by sex, age and patient condition.</summary>
public interface IReferenceRangeResolver
{
    RangeBand? Resolve(LabTest test, GenderRestriction sex, PatientAge age, Guid? patientConditionId);

    RangeBand? Resolve(LabTest test, GenderRestriction sex, int ageDays, Guid? patientConditionId);

    /// <summary>The same rule over any band shape (the read port's records, for the Results module).</summary>
    TBand? Resolve<TBand>(IEnumerable<TBand> bands, GenderRestriction sex, PatientAge age, Guid? patientConditionId)
        where TBand : class, IReferenceBand;
}

/// <summary>
/// The domain service of BR-08 (LDM §2.5, §3.3). A band applies when its sex is <c>Any</c> or the patient's, the age
/// in the band's unit lies in <c>[AgeFrom, AgeTo)</c> and its condition is empty or the patient's. Among the bands
/// that apply: the patient's condition beats a general band, a sex-specific band beats <c>Any</c>, then the narrowest
/// age span wins, then the display order. No band → <see langword="null"/> (the result is not flagged). Stateless.
/// </summary>
public sealed class ReferenceRangeResolver : IReferenceRangeResolver
{
    public static ReferenceRangeResolver Instance { get; } = new();

    public RangeBand? Resolve(LabTest test, GenderRestriction sex, PatientAge age, Guid? patientConditionId)
    {
        ArgumentNullException.ThrowIfNull(test);
        return Resolve(test.RangeBands, sex, age, patientConditionId);
    }

    public RangeBand? Resolve(LabTest test, GenderRestriction sex, int ageDays, Guid? patientConditionId) =>
        Resolve(test, sex, PatientAge.FromDays(ageDays), patientConditionId);

    public TBand? Resolve<TBand>(IEnumerable<TBand> bands, GenderRestriction sex, PatientAge age, Guid? patientConditionId)
        where TBand : class, IReferenceBand
    {
        ArgumentNullException.ThrowIfNull(bands);
        var condition = patientConditionId == Guid.Empty ? null : patientConditionId;

        return bands
            .Where(band => band.Sex == GenderRestriction.Any || band.Sex == sex)
            .Where(band => band.PatientConditionId is null || band.PatientConditionId == condition)
            .Where(band => age.In(band.AgeUnit) >= band.AgeFrom && age.In(band.AgeUnit) < band.AgeTo)
            .OrderByDescending(band => band.PatientConditionId is not null)
            .ThenByDescending(band => band.Sex != GenderRestriction.Any)
            .ThenBy(band => (band.AgeTo - band.AgeFrom) * RangeBand.DaysPerUnit(band.AgeUnit))
            .ThenBy(band => band.DisplayOrder)
            .FirstOrDefault();
    }
}
