using Platform.Domain.Results;
using Platform.Domain.Tracker;

namespace Platform.Domain.UnitTests.Tracker;

/// <summary>The stage, next step, time left and attention rules of the Lab Tracker (LIS-CR-006 §0; intake LIS-INT-TRK-001 §6).</summary>
public sealed class VisitProgressRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static readonly VisitSampleCounts Received = new(2, 0, 0, 2, 0);

    private static VisitResultCounts Results(int ordered = 0, int complete = 0, int reviewed = 0, int verified = 0, int printed = 0) =>
        new(ordered, complete, reviewed, verified, printed);

    private static VisitProgressFacts Facts(
        int liveLines = 2,
        VisitSampleCounts? samples = null,
        VisitResultCounts? visible = null,
        VisitResultCounts? all = null,
        IReadOnlyList<ResultClock>? visibleClocks = null,
        IReadOnlyList<ResultClock>? allClocks = null,
        int openPanics = 0,
        decimal remaining = 0m,
        bool claims = false) =>
        new(liveLines, samples ?? Received, visible ?? VisitResultCounts.None, all ?? visible ?? VisitResultCounts.None, visibleClocks ?? [], allClocks ?? visibleClocks ?? [], openPanics, remaining, claims);

    private static VisitProgress Of(VisitProgressFacts facts, bool review = false) => VisitProgressRules.Of(facts, review, Now, 80, 100);

    [Fact]
    public void TRK_S01_a_visit_without_a_live_line_is_cancelled_and_needs_nothing()
    {
        var progress = Of(Facts(liveLines: 0, samples: new VisitSampleCounts(1, 1, 0, 0, 0), visible: Results(ordered: 1), remaining: 50m));

        Assert.Equal(TrackerStage.Cancelled, progress.Stage);
        Assert.Equal(TrackerNextStep.None, progress.NextStep);
        Assert.Null(progress.MinutesLeft);
        Assert.False(progress.Delayed || progress.Rejected || progress.ReadyUnpaid);
        Assert.False(progress.In(TrackerView.Reception));
    }

    [Fact]
    public void TRK_S02_an_ordered_sample_waits_for_collection_before_anything_else()
    {
        var progress = Of(Facts(samples: new VisitSampleCounts(2, 1, 0, 1, 0), visible: Results(ordered: 1, verified: 1)));

        Assert.Equal(TrackerStage.AwaitingSample, progress.Stage);
        Assert.Equal(TrackerNextStep.CollectSample, progress.NextStep);
        Assert.True(progress.In(TrackerView.Reception));
    }

    [Fact]
    public void TRK_S03_a_rejected_sample_not_replaced_asks_for_a_new_sample_and_is_flagged()
    {
        var progress = Of(Facts(samples: new VisitSampleCounts(2, 0, 0, 1, 1), visible: Results(ordered: 2)));

        Assert.Equal(TrackerStage.AwaitingSample, progress.Stage);
        Assert.Equal(TrackerNextStep.RecollectSample, progress.NextStep);
        Assert.True(progress.Rejected);
        Assert.True(progress.Has(TrackerAttention.Rejected));
    }

    [Fact]
    public void TRK_S04_an_ordered_result_is_in_the_lab_waiting_for_entry()
    {
        var progress = Of(Facts(visible: Results(ordered: 1, complete: 1)));

        Assert.Equal(TrackerStage.InLab, progress.Stage);
        Assert.Equal(TrackerNextStep.EnterResults, progress.NextStep);
    }

    [Fact]
    public void TRK_S05_a_complete_result_waits_for_review_when_review_comes_before_verification()
    {
        var withReview = Of(Facts(visible: Results(complete: 1, verified: 1)), review: true);
        var withoutReview = Of(Facts(visible: Results(complete: 1, verified: 1)), review: false);

        Assert.Equal((TrackerStage.InLab, TrackerNextStep.ReviewResults), (withReview.Stage, withReview.NextStep));
        Assert.Equal((TrackerStage.AwaitingVerification, TrackerNextStep.VerifyResults), (withoutReview.Stage, withoutReview.NextStep));
        Assert.True(withoutReview.In(TrackerView.Verification));
    }

    [Fact]
    public void TRK_S06_a_reviewed_result_waits_for_verification()
    {
        var progress = Of(Facts(visible: Results(reviewed: 1, printed: 2)), review: true);

        Assert.Equal(TrackerStage.AwaitingVerification, progress.Stage);
        Assert.Equal(TrackerNextStep.VerifyResults, progress.NextStep);
    }

    [Fact]
    public void TRK_S07_signed_results_not_all_printed_are_ready_to_print()
    {
        var progress = Of(Facts(visible: Results(verified: 1, printed: 1)));

        Assert.Equal(TrackerStage.Ready, progress.Stage);
        Assert.Equal(TrackerNextStep.PrintReport, progress.NextStep);
        Assert.Equal(DelayState.Done, progress.DelayState);
        Assert.Null(progress.MinutesLeft);
        Assert.True(progress.In(TrackerView.FrontDesk));
        Assert.False(progress.ReadyUnpaid);
    }

    [Fact]
    public void TRK_S08_every_result_printed_is_delivered()
    {
        var progress = Of(Facts(visible: Results(printed: 3)));

        Assert.Equal(TrackerStage.Delivered, progress.Stage);
        Assert.Equal(TrackerNextStep.None, progress.NextStep);
    }

    [Fact]
    public void TRK_S09_a_ready_or_delivered_visit_with_a_patient_share_owed_takes_payment_and_is_flagged_never_blocked()
    {
        var ready = Of(Facts(visible: Results(verified: 2), remaining: 40m));
        var delivered = Of(Facts(visible: Results(printed: 2), remaining: 40m));

        Assert.Equal((TrackerStage.Ready, TrackerNextStep.TakePayment, true), (ready.Stage, ready.NextStep, ready.ReadyUnpaid));
        Assert.Equal((TrackerStage.Delivered, TrackerNextStep.TakePayment, true), (delivered.Stage, delivered.NextStep, delivered.ReadyUnpaid));
    }

    [Fact]
    public void TRK_S10_an_open_step_comes_before_the_payment_and_the_reception_view_still_lists_the_visit()
    {
        var progress = Of(Facts(visible: Results(ordered: 1), remaining: 40m));

        Assert.Equal(TrackerNextStep.EnterResults, progress.NextStep);
        Assert.False(progress.ReadyUnpaid);
        Assert.True(progress.OwesPatientShare);
        Assert.True(progress.In(TrackerView.Reception));
    }

    [Fact]
    public void TRK_S11_a_refund_due_or_a_claims_billed_visit_owes_nothing_at_the_desk()
    {
        var refundDue = Of(Facts(visible: Results(verified: 1), remaining: -10m));
        var claims = Of(Facts(visible: Results(verified: 1), remaining: 100m, claims: true));

        Assert.Equal(TrackerNextStep.PrintReport, refundDue.NextStep);
        Assert.Equal(TrackerNextStep.PrintReport, claims.NextStep);
        Assert.False(refundDue.ReadyUnpaid || claims.ReadyUnpaid);
    }

    [Fact]
    public void TRK_S12_the_time_left_is_the_earliest_expected_time_and_the_colour_the_worst_clock()
    {
        // A: started 2 h ago, due in 30 min (80 % consumed → Warning). B: started 10 min ago, due in 3 h (on time).
        var clocks = new[]
        {
            new ResultClock(Now.AddHours(-2), Now.AddMinutes(30)),
            new ResultClock(Now.AddMinutes(-10), Now.AddHours(3)),
        };
        var progress = Of(Facts(visible: Results(ordered: 2), visibleClocks: clocks));

        Assert.Equal(30, progress.MinutesLeft);
        Assert.Equal(DelayState.Warning, progress.DelayState);
        Assert.False(progress.Delayed);
    }

    [Fact]
    public void TRK_S13_a_clock_past_its_expected_time_reads_negative_minutes_and_is_delayed()
    {
        var clocks = new[] { new ResultClock(Now.AddHours(-3), Now.AddSeconds(-30)) };
        var progress = Of(Facts(visible: Results(complete: 1), visibleClocks: clocks));

        Assert.Equal(-1, progress.MinutesLeft);
        Assert.Equal(DelayState.Late, progress.DelayState);
        Assert.True(progress.Delayed);
        Assert.True(progress.Has(TrackerAttention.Delayed));
    }

    [Fact]
    public void TRK_S14_only_an_unsigned_visible_panic_raises_the_panic_flag()
    {
        Assert.True(Of(Facts(visible: Results(complete: 1), openPanics: 1)).Panic);
        Assert.False(Of(Facts(visible: Results(verified: 1), openPanics: 0)).Panic);
    }

    [Fact]
    public void TRK_S15_without_visible_results_the_visit_is_followed_on_all_its_results()
    {
        var clocks = new[] { new ResultClock(Now.AddHours(-1), Now.AddHours(1)) };
        var progress = Of(Facts(visible: VisitResultCounts.None, all: Results(ordered: 1, verified: 1), allClocks: clocks));

        Assert.Equal(TrackerStage.InLab, progress.Stage);
        Assert.Equal(60, progress.MinutesLeft);
        Assert.Equal(DelayState.OnTime, progress.DelayState);

        // With some visible results only those count: the hidden ordered result does not hold the visit in the lab.
        var partial = Of(Facts(visible: Results(verified: 1), all: Results(ordered: 1, verified: 1), allClocks: clocks));
        Assert.Equal(TrackerStage.Ready, partial.Stage);
        Assert.Null(partial.MinutesLeft);
    }

    [Fact]
    public void TRK_S16_a_visit_whose_results_are_not_created_yet_waits_for_its_sample_or_for_entry()
    {
        Assert.Equal(TrackerStage.AwaitingSample, Of(Facts(samples: VisitSampleCounts.None)).Stage);
        var received = Of(Facts());
        Assert.Equal((TrackerStage.InLab, TrackerNextStep.EnterResults), (received.Stage, received.NextStep));
        Assert.Null(received.DelayState);
    }
}
