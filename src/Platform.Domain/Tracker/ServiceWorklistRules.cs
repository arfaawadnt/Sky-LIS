using Platform.Domain.Results;
using Platform.Domain.Samples;

namespace Platform.Domain.Tracker;

/// <summary>The signed result that closes a service line's clock: its start, expected time and verification time.</summary>
public sealed record SignedResultClock(DateTimeOffset StartUtc, DateTimeOffset ExpectedUtc, DateTimeOffset? VerifiedAtUtc);

/// <summary>
/// The worklist reading of a service line's turnaround (BR-13, as CP-18 shows a result): the expected time, the whole seconds
/// past it, the share of the turnaround consumed and the delay colour.
/// </summary>
public sealed record ServiceTurnaround(DateTimeOffset ExpectedAtUtc, long DelaySeconds, decimal ConsumedPercent, DelayState State);

/// <summary>
/// The Lab Tracker's <i>Services</i> rows read like the worklist (change request of 2026-10-26: "the service view shall match
/// the current view of the worklist page"). A line holds one or more results and samples, so each worklist column reads the
/// line's least advanced or most pressing member. Pure functions.
/// </summary>
public static class ServiceWorklistRules
{
    /// <summary>
    /// While a result is unsigned, the clock of the unsigned result that has consumed the largest share of its turnaround
    /// (ties: the earliest expected), measured now, the colour by the tenant thresholds. Once every result is signed, the
    /// clock of <paramref name="lastSigned"/> (the signed result expected last) measured at its verification, colour Done.
    /// Null for a line without results.
    /// </summary>
    public static ServiceTurnaround? TurnaroundOf(IReadOnlyList<ResultClock> openClocks, SignedResultClock? lastSigned, DateTimeOffset nowUtc, int warnPercent, int latePercent)
    {
        ArgumentNullException.ThrowIfNull(openClocks);
        if (openClocks.Count > 0)
        {
            var worst = openClocks
                .Select(clock => (Clock: clock, Consumed: TurnaroundClock.ConsumedPercent(clock.StartUtc, clock.ExpectedUtc, nowUtc)))
                .OrderByDescending(entry => entry.Consumed)
                .ThenBy(entry => entry.Clock.ExpectedUtc)
                .First();
            return new ServiceTurnaround(worst.Clock.ExpectedUtc, SecondsPast(worst.Clock.ExpectedUtc, nowUtc), worst.Consumed, TurnaroundClock.State(worst.Consumed, false, warnPercent, latePercent));
        }

        if (lastSigned is null)
        {
            return null;
        }

        var end = lastSigned.VerifiedAtUtc ?? nowUtc;
        var consumed = TurnaroundClock.ConsumedPercent(lastSigned.StartUtc, lastSigned.ExpectedUtc, end);
        return new ServiceTurnaround(lastSigned.ExpectedUtc, SecondsPast(lastSigned.ExpectedUtc, end), consumed, DelayState.Done);
    }

    /// <summary>The line's test status: the least advanced of its live results; null without one.</summary>
    public static ResultStatus? StatusOf(VisitResultCounts results)
    {
        ArgumentNullException.ThrowIfNull(results);
        return results switch
        {
            { Ordered: > 0 } => ResultStatus.Ordered,
            { Complete: > 0 } => ResultStatus.Complete,
            { Reviewed: > 0 } => ResultStatus.Reviewed,
            { Verified: > 0 } => ResultStatus.Verified,
            { Printed: > 0 } => ResultStatus.Printed,
            _ => null,
        };
    }

    /// <summary>The line's sample status: Rejected while a sample waits for a new one, else the least advanced live sample; null without one.</summary>
    public static SampleStatus? SampleStatusOf(VisitSampleCounts samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        return samples switch
        {
            { Rejected: > 0 } => SampleStatus.Rejected,
            { Ordered: > 0 } => SampleStatus.Ordered,
            { Collected: > 0 } => SampleStatus.Collected,
            { Received: > 0 } => SampleStatus.Received,
            _ => null,
        };
    }

    private static long SecondsPast(DateTimeOffset expectedUtc, DateTimeOffset atUtc) => Math.Max(0L, (long)Math.Floor((atUtc - expectedUtc).TotalSeconds));
}
