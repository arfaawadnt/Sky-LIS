using Platform.SharedKernel.Abstractions;

namespace Platform.Domain.UnitTests.Abstractions;

public sealed class SystemClockTests
{
    [Fact]
    public void The_system_clock_reports_utc()
    {
        var now = new SystemClock().UtcNow;

        Assert.Equal(TimeSpan.Zero, now.Offset);
        Assert.True(now.Year >= 2026);
    }
}
