using Platform.Domain.Catalogue;

namespace Platform.Domain.Results;

/// <summary>The delay colour of a test on the worklist (BR-13): on time, at risk, late, or done (verified).</summary>
public enum DelayState
{
    OnTime = 0,
    Warning = 1,
    Late = 2,
    Done = 3,
}

/// <summary>
/// The turnaround clock of BR-13 (LDM §3.4 "Duration", "Test Days"; §6 worklist colours): the expected result time is the
/// start (collection, else registration) plus the test's duration counting only the days the test runs; the colour is
/// the share of the duration consumed against the tenant thresholds <c>delayWarnPercent</c> / <c>delayLatePercent</c>.
/// Days are UTC days (intake §16 Q13). Pure functions.
/// </summary>
public static class TurnaroundClock
{
    /// <summary>The longest walk (a year and a half of days), a guard against a test that runs on no day.</summary>
    private const int MaxDays = 550;

    /// <summary>Start + <paramref name="durationHours"/> counted only on the weekdays of <paramref name="testDays"/> (bit 0 = Sunday; 0 or 127 = every day).</summary>
    public static DateTimeOffset ExpectedAt(DateTimeOffset startUtc, int durationHours, int testDays)
    {
        var start = startUtc.ToUniversalTime();
        if (durationHours <= 0)
        {
            return start;
        }

        var days = testDays & LabTestTiming.EveryDay;
        if (days is 0 or LabTestTiming.EveryDay)
        {
            return start.AddHours(durationHours);
        }

        var remaining = TimeSpan.FromHours(durationHours);
        var cursor = start;
        for (var guard = 0; guard < MaxDays && remaining > TimeSpan.Zero; guard++)
        {
            var nextMidnight = new DateTimeOffset(cursor.Year, cursor.Month, cursor.Day, 0, 0, 0, TimeSpan.Zero).AddDays(1);
            if ((days & (1 << (int)cursor.DayOfWeek)) != 0)
            {
                var available = nextMidnight - cursor;
                if (available >= remaining)
                {
                    return cursor + remaining;
                }

                remaining -= available;
            }

            cursor = nextMidnight;
        }

        return cursor + remaining;
    }

    /// <summary>
    /// The whole percent of the duration consumed at <paramref name="atUtc"/> (0 when the clock has not started; 100 for a
    /// zero duration once due). Rounded down, so "consumed ≥ threshold" holds exactly when the threshold instant
    /// <c>start + (expected − start) × threshold / 100</c> is reached: the row colour, the worklist's Delayed counter and
    /// filter and the dashboard read the same "late" (validation v0.5.0 F-15).
    /// </summary>
    public static decimal ConsumedPercent(DateTimeOffset startUtc, DateTimeOffset expectedUtc, DateTimeOffset atUtc)
    {
        var total = expectedUtc - startUtc;
        var elapsed = atUtc - startUtc;
        if (elapsed <= TimeSpan.Zero)
        {
            return 0m;
        }

        if (total <= TimeSpan.Zero)
        {
            return 100m;
        }

        var percent = elapsed.Ticks * 100m / total.Ticks;   // exact in decimal: a threshold instant reads its whole percent
        return Math.Floor(Math.Min(percent, 99999m));
    }

    /// <summary>
    /// True when an open test is late at <paramref name="atUtc"/> by the tenant's <paramref name="latePercent"/>: the one
    /// definition behind the Late colour, "Delayed only" and the Delayed counter (the repository's SQL states the same
    /// predicate; F-15).
    /// </summary>
    public static bool IsLate(DateTimeOffset startUtc, DateTimeOffset expectedUtc, DateTimeOffset atUtc, int latePercent) =>
        State(ConsumedPercent(startUtc, expectedUtc, atUtc), false, int.MaxValue, latePercent) == DelayState.Late;

    /// <summary>The colour of a test: Done once verified, else Late at or above <paramref name="latePercent"/>, Warning at or above <paramref name="warnPercent"/>.</summary>
    public static DelayState State(decimal consumedPercent, bool done, int warnPercent, int latePercent)
    {
        if (done)
        {
            return DelayState.Done;
        }

        if (consumedPercent >= latePercent)
        {
            return DelayState.Late;
        }

        return consumedPercent >= warnPercent ? DelayState.Warning : DelayState.OnTime;
    }
}
