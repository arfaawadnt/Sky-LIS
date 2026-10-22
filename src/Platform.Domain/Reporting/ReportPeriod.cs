using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Reporting;

/// <summary>
/// The period of a report in whole laboratory-calendar days (intake LIS-INT-REP-001 §14b): "today" and the day
/// boundaries follow the laboratory's zone (<c>ILaboratoryTimeZone</c>; UTC when none is known), the end day is inclusive,
/// and the member's special right <c>ReportDaysLimit</c> (CP-37, change request 2026-10-13 item 3) bounds how far back
/// the period may start.
/// </summary>
public static class ReportPeriod
{
    /// <summary>The laboratory-calendar day of an instant.</summary>
    public static DateOnly LocalDay(DateTimeOffset instantUtc, TimeZoneInfo? zone) =>
        DateOnly.FromDateTime(zone is null ? instantUtc.UtcDateTime : TimeZoneInfo.ConvertTime(instantUtc, zone).DateTime);

    /// <summary>
    /// The half-open UTC window <c>[from 00:00, (to + 1 day) 00:00)</c> of whole laboratory days. REP-002 when the period
    /// ends before it starts or spans more than <see cref="ReportingCodes.MaxReportSpanDays"/> days.
    /// </summary>
    public static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) Window(DateOnly from, DateOnly to, TimeZoneInfo? zone)
    {
        if (to < from)
        {
            throw new DomainException(ReportingCodes.ReportPeriodInvalid, "The period must start before it ends.", DomainFault.Validation);
        }

        if (to.DayNumber - from.DayNumber >= ReportingCodes.MaxReportSpanDays)
        {
            throw new DomainException(ReportingCodes.ReportPeriodInvalid, $"The period may cover at most {ReportingCodes.MaxReportSpanDays} days.", DomainFault.Validation);
        }

        return (StartOfDayUtc(from, zone), StartOfDayUtc(to.AddDays(1), zone));
    }

    /// <summary>REP-001 (refused) when the period starts before the member's earliest allowed day (<see langword="null"/> = no limit).</summary>
    public static void RequireWithinLimit(DateOnly from, DateOnly? earliestAllowed)
    {
        if (earliestAllowed is { } earliest && from < earliest)
        {
            throw new DomainException(ReportingCodes.ReportPeriodBeyondLimit, $"Your role may report from {earliest:yyyy-MM-dd} onwards only.", DomainFault.Refused);
        }
    }

    /// <summary>The instant of a laboratory wall-clock time (the kind is ignored; UTC when no zone is known).</summary>
    public static DateTimeOffset InstantUtc(DateTime local, TimeZoneInfo? zone)
    {
        var wall = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var offset = zone?.GetUtcOffset(wall) ?? TimeSpan.Zero;
        return new DateTimeOffset(wall, offset).ToUniversalTime();
    }

    private static DateTimeOffset StartOfDayUtc(DateOnly day, TimeZoneInfo? zone) => InstantUtc(day.ToDateTime(TimeOnly.MinValue), zone);
}
