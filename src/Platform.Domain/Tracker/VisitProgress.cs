using Platform.Domain.Results;

namespace Platform.Domain.Tracker;

// Lab Tracker (change request LIS-CR-006, intake LIS-INT-TRK-001): one row per visit of the laboratory day that follows it
// through registration → samples → results → payment. The rules below derive the stage, the next step, the time left and
// the attention flags of a visit from counts and clocks the read port returns; they are the only definition of them.

/// <summary>The stage of a visit, in workflow order (the order the <c>stage</c> sort follows); Cancelled sorts last.</summary>
public enum TrackerStage
{
    AwaitingSample = 0,
    InLab = 1,
    AwaitingVerification = 2,
    Ready = 3,
    Delivered = 4,
    Cancelled = 5,
}

/// <summary>What the visit needs next; the name says which module does it (samples, results, billing).</summary>
public enum TrackerNextStep
{
    None = 0,
    CollectSample = 1,
    RecollectSample = 2,
    EnterResults = 3,
    ReviewResults = 4,
    VerifyResults = 5,
    TakePayment = 6,
    PrintReport = 7,
}

/// <summary>The "needs attention" filters of the tracker.</summary>
public enum TrackerAttention
{
    Delayed = 0,
    Panic = 1,
    Rejected = 2,
    ReadyUnpaid = 3,
}

/// <summary>The desk presets: Reception (awaiting sample or a patient share to collect), Verification, Front desk (ready).</summary>
public enum TrackerView
{
    Reception = 0,
    Verification = 1,
    FrontDesk = 2,
}

/// <summary>
/// The live samples of a visit (Cancelled and Replaced excluded) by status; <paramref name="CollectedOutside"/> counts the
/// live samples collected outside the laboratory (LIS-CR-008 C-02), whatever their status.
/// </summary>
public sealed record VisitSampleCounts(int Total, int Ordered, int Collected, int Received, int Rejected, int CollectedOutside = 0)
{
    public static VisitSampleCounts None { get; } = new(0, 0, 0, 0, 0);

    /// <summary>The samples cell shows the "ext" tag: at least one live sample was collected outside (LIS-CR-008 C-02).</summary>
    public bool AnyCollectedOutside => CollectedOutside > 0;
}

/// <summary>Live results (Cancelled excluded) by status on the track.</summary>
public sealed record VisitResultCounts(int Ordered, int Complete, int Reviewed, int Verified, int Printed)
{
    public static VisitResultCounts None { get; } = new(0, 0, 0, 0, 0);

    public int Total => Ordered + Complete + Reviewed + Verified + Printed;

    /// <summary>Results not signed yet (Ordered, Complete, Reviewed).</summary>
    public int Open => Ordered + Complete + Reviewed;
}

/// <summary>The turnaround clock of one unsigned result: its start (collection, else registration) and expected time.</summary>
public readonly record struct ResultClock(DateTimeOffset StartUtc, DateTimeOffset ExpectedUtc);

/// <summary>
/// The facts of one visit the rules read: live top-level lines, live samples, the live results the user may see and every
/// live result of the visit, the clocks of their unsigned results, the visible unsigned panics, and the money still owed by
/// the patient (<see cref="Remaining"/> = patient share − posted payments + refunds, BR-14). A lab-to-lab batch visit is
/// billed through claims and owes nothing at the desk.
/// </summary>
public sealed record VisitProgressFacts(
    int LiveLines,
    VisitSampleCounts Samples,
    VisitResultCounts VisibleResults,
    VisitResultCounts AllResults,
    IReadOnlyList<ResultClock> VisibleOpenClocks,
    IReadOnlyList<ResultClock> AllOpenClocks,
    int VisibleOpenPanics,
    decimal Remaining,
    bool BilledThroughClaims);

/// <summary>
/// What the tracker shows of a visit: stage, next step, minutes to the earliest expected time of an unsigned result
/// (negative = late; null without one), the delay colour (worst of the unsigned results; Done when every result is
/// signed; null without results) and the attention flags.
/// </summary>
public sealed record VisitProgress(
    TrackerStage Stage,
    TrackerNextStep NextStep,
    int? MinutesLeft,
    DelayState? DelayState,
    bool Delayed,
    bool Panic,
    bool Rejected,
    bool ReadyUnpaid,
    bool OwesPatientShare)
{
    public bool Has(TrackerAttention attention) => attention switch
    {
        TrackerAttention.Delayed => Delayed,
        TrackerAttention.Panic => Panic,
        TrackerAttention.Rejected => Rejected,
        TrackerAttention.ReadyUnpaid => ReadyUnpaid,
        _ => false,
    };

    /// <summary>The desk presets: Reception = awaiting sample or a patient share to collect (a cancelled visit excluded); Verification; Front desk = Ready.</summary>
    public bool In(TrackerView view) => view switch
    {
        TrackerView.Reception => Stage == TrackerStage.AwaitingSample || (OwesPatientShare && Stage != TrackerStage.Cancelled),
        TrackerView.Verification => Stage == TrackerStage.AwaitingVerification,
        TrackerView.FrontDesk => Stage == TrackerStage.Ready,
        _ => false,
    };
}

/// <summary>
/// The rules of the Lab Tracker (LIS-CR-006 §0), pure functions. The stage, in this order of precedence:
/// <list type="number">
/// <item><b>Cancelled</b>: no live line;</item>
/// <item><b>AwaitingSample</b>: a live sample is Ordered or Rejected (a replaced sample is no longer live);</item>
/// <item><b>InLab</b>: a result is Ordered, or Complete while results pass through review (<c>reviewBeforeVerify</c>);</item>
/// <item><b>AwaitingVerification</b>: a result is Reviewed, or Complete without review;</item>
/// <item><b>Ready</b>: every result is signed and one is not printed yet;</item>
/// <item><b>Delivered</b>: every result is printed (delivered = printed: no hand-over record exists yet).</item>
/// </list>
/// The results read are the ones the user may see; when the user sees none of the visit's results, every live result of
/// the visit is read instead, so a desk without result rights still follows the visit (assumption, intake Q1). A visit
/// whose results are not created yet (the order policies run just after registration) reads AwaitingSample without live
/// samples and InLab with them.
/// </summary>
public static class VisitProgressRules
{
    public static VisitProgress Of(VisitProgressFacts facts, bool reviewBeforeVerify, DateTimeOffset nowUtc, int warnPercent, int latePercent)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var useVisible = facts.VisibleResults.Total > 0;
        var results = useVisible ? facts.VisibleResults : facts.AllResults;
        var clocks = useVisible ? facts.VisibleOpenClocks : facts.AllOpenClocks;
        var owes = facts.Remaining > 0m && !facts.BilledThroughClaims;

        var stage = StageOf(facts.LiveLines, facts.Samples, results, reviewBeforeVerify);
        var next = NextStepOf(stage, facts.Samples, results, owes);
        var (minutesLeft, delay) = TimeLeft(results, clocks, nowUtc, warnPercent, latePercent);
        var live = stage != TrackerStage.Cancelled;

        return new VisitProgress(
            stage,
            next,
            live ? minutesLeft : null,
            live ? delay : null,
            live && delay == Results.DelayState.Late,
            live && facts.VisibleOpenPanics > 0,
            live && facts.Samples.Rejected > 0,
            stage is TrackerStage.Ready or TrackerStage.Delivered && owes,
            owes);
    }

    public static TrackerStage StageOf(int liveLines, VisitSampleCounts samples, VisitResultCounts results, bool reviewBeforeVerify)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(results);
        if (liveLines <= 0)
        {
            return TrackerStage.Cancelled;
        }

        if (samples.Ordered > 0 || samples.Rejected > 0)
        {
            return TrackerStage.AwaitingSample;
        }

        if (results.Total == 0)
        {
            return samples.Total == 0 ? TrackerStage.AwaitingSample : TrackerStage.InLab;
        }

        if (results.Ordered > 0 || (reviewBeforeVerify && results.Complete > 0))
        {
            return TrackerStage.InLab;
        }

        if (results.Open > 0)
        {
            return TrackerStage.AwaitingVerification;
        }

        return results.Verified > 0 ? TrackerStage.Ready : TrackerStage.Delivered;
    }

    /// <summary>
    /// The next step of a stage; a patient share still owed makes the next step <c>TakePayment</c> once nothing earlier is
    /// open (Ready or Delivered), before printing: a verified visit that is not fully paid is flagged, never blocked.
    /// </summary>
    public static TrackerNextStep NextStepOf(TrackerStage stage, VisitSampleCounts samples, VisitResultCounts results, bool owesPatientShare)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(results);
        return stage switch
        {
            TrackerStage.AwaitingSample => samples.Rejected > 0 ? TrackerNextStep.RecollectSample : TrackerNextStep.CollectSample,
            TrackerStage.InLab => results.Ordered > 0 || results.Total == 0 ? TrackerNextStep.EnterResults : TrackerNextStep.ReviewResults,
            TrackerStage.AwaitingVerification => TrackerNextStep.VerifyResults,
            TrackerStage.Ready => owesPatientShare ? TrackerNextStep.TakePayment : TrackerNextStep.PrintReport,
            TrackerStage.Delivered => owesPatientShare ? TrackerNextStep.TakePayment : TrackerNextStep.None,
            _ => TrackerNextStep.None,
        };
    }

    /// <summary>
    /// Minutes from <paramref name="nowUtc"/> to the earliest expected time of the unsigned results (rounded down, so a
    /// result due 30 s ago reads −1) and the worst delay colour of those results by the turnaround clock (BR-13, the
    /// worklist's own definition: <see cref="TurnaroundClock"/>). Every result signed: no minutes, Done. No results: nothing.
    /// </summary>
    public static (int? MinutesLeft, DelayState? State) TimeLeft(VisitResultCounts results, IReadOnlyList<ResultClock> openClocks, DateTimeOffset nowUtc, int warnPercent, int latePercent)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(openClocks);
        if (openClocks.Count == 0)
        {
            return (null, results.Total > 0 && results.Open == 0 ? Results.DelayState.Done : null);
        }

        var earliest = openClocks.Min(clock => clock.ExpectedUtc);
        var minutes = Math.Floor((earliest - nowUtc).TotalMinutes);
        var minutesLeft = (int)Math.Clamp(minutes, int.MinValue, int.MaxValue);
        var worst = Results.DelayState.OnTime;
        foreach (var clock in openClocks)
        {
            var state = TurnaroundClock.State(TurnaroundClock.ConsumedPercent(clock.StartUtc, clock.ExpectedUtc, nowUtc), false, warnPercent, latePercent);
            if (state > worst)
            {
                worst = state;
            }
        }

        return (minutesLeft, worst);
    }
}
