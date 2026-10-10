namespace Platform.Domain.Registration;

/// <summary>
/// BR-13 "Turnaround clock" (LDM §3.4 Duration / Test Days, §6): the expected result moment is the start plus the
/// turnaround hours, counting only the hours that fall on the days the test runs. Days are the local days of the
/// registering desk (<c>utcOffsetMinutes</c>, east positive). A start on a day the test does not run begins counting at
/// the next running day's midnight. Pure and deterministic.
/// </summary>
public static class TurnaroundCalculator
{
    /// <summary>Bit 0 = Sunday … bit 6 = Saturday (<see cref="DayOfWeek"/> order), as the catalogue stores test days.</summary>
    public const int EveryDay = 127;

    private const int MaxDaysScanned = 3 * 366 * 7;

    public static DateTimeOffset ExpectedAt(DateTimeOffset startUtc, int hours, int testDaysMask, int utcOffsetMinutes)
    {
        if (hours <= 0)
        {
            return startUtc.ToUniversalTime();
        }

        var mask = testDaysMask & EveryDay;
        if (mask == 0)
        {
            mask = EveryDay;   // a test that runs on no day is a configuration gap; count every day rather than never
        }

        var offset = TimeSpan.FromMinutes(Math.Clamp(utcOffsetMinutes, -840, 840));
        var cursor = startUtc.ToOffset(offset);
        var remaining = TimeSpan.FromHours(hours);

        for (var scanned = 0; scanned < MaxDaysScanned; scanned++)
        {
            var dayEnd = new DateTimeOffset(cursor.Date.AddDays(1), offset);
            if (RunsOn(mask, cursor.DayOfWeek))
            {
                var available = dayEnd - cursor;
                if (remaining <= available)
                {
                    return (cursor + remaining).ToUniversalTime();
                }

                remaining -= available;
            }

            cursor = dayEnd;
        }

        return (startUtc + TimeSpan.FromHours(hours)).ToUniversalTime();
    }

    /// <summary>The latest of several expected moments, or <see langword="null"/> when there is none.</summary>
    public static DateTimeOffset? Latest(IEnumerable<DateTimeOffset?> moments)
    {
        ArgumentNullException.ThrowIfNull(moments);
        DateTimeOffset? latest = null;
        foreach (var moment in moments)
        {
            if (moment is { } value && (latest is null || value > latest.Value))
            {
                latest = value;
            }
        }

        return latest;
    }

    private static bool RunsOn(int mask, DayOfWeek day) => (mask & (1 << (int)day)) != 0;
}
