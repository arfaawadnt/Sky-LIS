using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Contracts.Samples;
using Platform.Domain.Samples;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Samples;

/// <summary>The acting user of a sample command; an unauthenticated scope is refused (the policy attribute already requires a session).</summary>
internal static class SamplesActor
{
    public static Guid Of(ICurrentUser user) =>
        user.UserId ?? throw new DomainException(ProblemCodes.AuthenticatedActorRequired, "An authenticated user is required.", DomainFault.Unauthenticated);
}

/// <summary>Coded "not found" refusals of the module.</summary>
internal static class SamplesNotFound
{
    public static DomainException Sample() => new(SamplesCodes.SampleNotFound, "Sample not found.", DomainFault.NotFound);

    public static DomainException Visit() => new(SamplesCodes.VisitSamplesNotFound, "No samples were found for this visit.", DomainFault.NotFound);
}

/// <summary>CP-17 "time between stages": the time since the previous step and the three stage durations, computed from the stored steps.</summary>
public static class SampleTimelineCalculator
{
    public static SampleTimeline Build(SampleDetail sample, IReadOnlyList<SampleTimelineStep> steps)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(steps);

        var ordered = steps.OrderBy(step => step.Sequence).ToList();   // the order the steps happened, also when they share an instant
        var withGaps = new List<SampleTimelineStep>(ordered.Count);
        DateTimeOffset? previous = null;
        foreach (var step in ordered)
        {
            withGaps.Add(step with { SincePreviousSeconds = previous is { } at ? Seconds(at, step.AtUtc) : null });
            previous = step.AtUtc;
        }

        var summary = sample.Summary;
        var durations = new SampleStageDurations(
            summary.CollectedAtUtc is { } collected ? Seconds(summary.OrderedAtUtc, collected) : null,
            summary.CollectedAtUtc is { } from && summary.ReceivedAtUtc is { } to ? Seconds(from, to) : null,
            summary.ReceivedAtUtc is { } received ? Seconds(summary.OrderedAtUtc, received) : null);

        return new SampleTimeline(sample, withGaps, durations);
    }

    private static long Seconds(DateTimeOffset from, DateTimeOffset to) => Math.Max(0, (long)Math.Floor((to - from).TotalSeconds));
}

/// <summary>The local day of the caller as a UTC window ("rejected today", intake §16 Q8).</summary>
public static class LocalDay
{
    public const int MaxOffsetMinutes = 14 * 60;

    public static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) Of(DateTimeOffset nowUtc, int utcOffsetMinutes)
    {
        var offset = TimeSpan.FromMinutes(Math.Clamp(utcOffsetMinutes, -MaxOffsetMinutes, MaxOffsetMinutes));
        var local = nowUtc.ToOffset(offset);
        var start = new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, offset).ToUniversalTime();
        return (start, start.AddDays(1));
    }
}
