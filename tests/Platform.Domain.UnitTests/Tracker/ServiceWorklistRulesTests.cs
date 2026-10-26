using Platform.Domain.Results;
using Platform.Domain.Samples;
using Platform.Domain.Tracker;

namespace Platform.Domain.UnitTests.Tracker;

/// <summary>The worklist columns of the Lab Tracker's service rows (change request of 2026-10-26).</summary>
public sealed class ServiceWorklistRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private static ResultClock Clock(int startHoursAgo, int expectedInHours) => new(Now.AddHours(-startHoursAgo), Now.AddHours(expectedInHours));

    [Fact]
    public void An_open_line_reads_the_clock_that_has_consumed_most_of_its_turnaround()
    {
        // 2 h of 10 h consumed (20 %) and expected first; 9 h of 10 h consumed (90 %) expected later: the second colours the line
        var early = Clock(2, 8);
        var pressing = new ResultClock(Now.AddHours(-9), Now.AddHours(1));

        var turnaround = ServiceWorklistRules.TurnaroundOf([early, pressing], null, Now, 80, 100)!;

        Assert.Equal((pressing.ExpectedUtc, 0L, 90m, DelayState.Warning), (turnaround.ExpectedAtUtc, turnaround.DelaySeconds, turnaround.ConsumedPercent, turnaround.State));
    }

    [Fact]
    public void A_late_line_counts_the_seconds_past_the_expected_time()
    {
        var late = new ResultClock(Now.AddHours(-3), Now.AddMinutes(-30));

        var turnaround = ServiceWorklistRules.TurnaroundOf([late], null, Now, 80, 100)!;

        Assert.Equal((1800L, DelayState.Late), (turnaround.DelaySeconds, turnaround.State));
        Assert.True(turnaround.ConsumedPercent > 100m);
    }

    [Fact]
    public void A_signed_line_is_measured_at_its_verification_and_reads_done()
    {
        var signed = new SignedResultClock(Now.AddHours(-10), Now.AddHours(-2), Now.AddHours(-1));

        var turnaround = ServiceWorklistRules.TurnaroundOf([], signed, Now, 80, 100)!;

        Assert.Equal((signed.ExpectedUtc, 3600L, DelayState.Done), (turnaround.ExpectedAtUtc, turnaround.DelaySeconds, turnaround.State));
        Assert.Null(ServiceWorklistRules.TurnaroundOf([], null, Now, 80, 100));
    }

    [Theory]
    [InlineData(1, 1, 0, 0, 0, ResultStatus.Ordered)]
    [InlineData(0, 1, 1, 0, 0, ResultStatus.Complete)]
    [InlineData(0, 0, 1, 2, 0, ResultStatus.Reviewed)]
    [InlineData(0, 0, 0, 1, 1, ResultStatus.Verified)]
    [InlineData(0, 0, 0, 0, 2, ResultStatus.Printed)]
    public void The_status_of_a_line_is_its_least_advanced_result(int ordered, int complete, int reviewed, int verified, int printed, ResultStatus expected)
    {
        Assert.Equal(expected, ServiceWorklistRules.StatusOf(new VisitResultCounts(ordered, complete, reviewed, verified, printed)));
    }

    [Fact]
    public void The_sample_status_of_a_line_puts_a_rejection_first_then_the_least_advanced_sample()
    {
        Assert.Equal(SampleStatus.Rejected, ServiceWorklistRules.SampleStatusOf(new VisitSampleCounts(3, 1, 0, 1, 1)));
        Assert.Equal(SampleStatus.Ordered, ServiceWorklistRules.SampleStatusOf(new VisitSampleCounts(2, 1, 0, 1, 0)));
        Assert.Equal(SampleStatus.Collected, ServiceWorklistRules.SampleStatusOf(new VisitSampleCounts(2, 0, 1, 1, 0)));
        Assert.Equal(SampleStatus.Received, ServiceWorklistRules.SampleStatusOf(new VisitSampleCounts(2, 0, 0, 2, 0)));
        Assert.Null(ServiceWorklistRules.SampleStatusOf(VisitSampleCounts.None));
        Assert.Null(ServiceWorklistRules.StatusOf(VisitResultCounts.None));
    }
}
