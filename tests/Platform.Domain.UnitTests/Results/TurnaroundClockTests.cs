using Platform.Domain.Results;

namespace Platform.Domain.UnitTests.Results;

/// <summary>BR-13 (intake LIS-INT-RES-001 §16 R-BR13) and BR-11 delta arithmetic.</summary>
public sealed class TurnaroundClockTests
{
    // Bit 0 = Sunday … bit 6 = Saturday: Monday–Friday = 0b0111110.
    private const int Weekdays = 0b0111110;

    private static readonly DateTimeOffset FridayTen = new(2026, 10, 9, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void R_BR13_every_day_adds_the_duration()
    {
        Assert.Equal(FridayTen.AddHours(24), TurnaroundClock.ExpectedAt(FridayTen, 24, 127));
        Assert.Equal(FridayTen.AddHours(30), TurnaroundClock.ExpectedAt(FridayTen, 30, 0));
        Assert.Equal(FridayTen, TurnaroundClock.ExpectedAt(FridayTen, 0, Weekdays));
    }

    [Fact]
    public void R_BR13_days_the_test_does_not_run_are_skipped()
    {
        // 14 h left on Friday, the weekend does not count, 10 h on Monday.
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero), TurnaroundClock.ExpectedAt(FridayTen, 24, Weekdays));

        // Collected on Saturday: the clock starts Monday 00:00.
        var saturday = new DateTimeOffset(2026, 10, 10, 15, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 6, 0, 0, TimeSpan.Zero), TurnaroundClock.ExpectedAt(saturday, 6, Weekdays));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(19, 79)]
    [InlineData(20, 83)]
    [InlineData(24, 100)]
    [InlineData(36, 150)]
    public void R_BR13_consumed_share_of_the_duration(int hoursElapsed, int percent)
    {
        Assert.Equal(percent, TurnaroundClock.ConsumedPercent(FridayTen, FridayTen.AddHours(24), FridayTen.AddHours(hoursElapsed)));
    }

    [Theory]
    [InlineData(50, false, DelayState.OnTime)]
    [InlineData(80, false, DelayState.Warning)]
    [InlineData(100, false, DelayState.Late)]
    [InlineData(150, true, DelayState.Done)]
    public void R_BR13_colour_by_the_tenant_thresholds(int consumed, bool done, DelayState state)
    {
        Assert.Equal(state, TurnaroundClock.State(consumed, done, 80, 100));
    }

    [Fact]
    public void R_BR11_delta_percent_against_the_previous_value()
    {
        var previous = new PreviousResultValue(Guid.CreateVersion7(), 4m, DateTimeOffset.UnixEpoch);
        Assert.Equal((true, 25.0m), ResultValueRules.Delta(5m, previous, 20m));
        Assert.Equal((false, 12.5m), ResultValueRules.Delta(3.5m, previous, 20m));
        Assert.Equal((false, (decimal?)null), ResultValueRules.Delta(5m, null, 20m));
        Assert.Equal((false, (decimal?)null), ResultValueRules.Delta(5m, previous, null));
        Assert.True(ResultValueRules.Delta(1m, new PreviousResultValue(Guid.CreateVersion7(), 0m, DateTimeOffset.UnixEpoch), 20m).Flag);
    }

    [Fact]
    public void R_BR08_range_display_and_flag_without_limits()
    {
        Assert.Equal("3.5 – 5.0", ResolvedRange.Create(3.5m, 5m, null, null, null, null, null)!.Display(1));
        Assert.Equal("< 200", ResolvedRange.Create(null, 200m, null, null, null, null, null)!.Display(0));
        Assert.Equal("Negative", ResolvedRange.Create(null, null, null, null, null, null, " Negative ")!.Display(0));
        Assert.Null(ResolvedRange.Create(null, null, null, null, null, null, " "));
        Assert.Null(ResultValueRules.Flag(4m, ResolvedRange.Create(null, null, null, null, null, null, "Negative")));
        Assert.Null(ResultValueRules.Flag(4m, null));
    }
}
