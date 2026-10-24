using Platform.Domain.PlatformAdmin;

namespace Platform.Domain.UnitTests.PlatformAdmin;

/// <summary>
/// Silent clients (change request LIS-CR-007 A-01; intake LIS-INT-PLT-001 §18): last activity = the later of the last
/// sign-in and the last registration; silent = older than the window, or none and the client older than the window;
/// inactive clients never; the window is 1…90 days.
/// </summary>
public sealed class ClientSilenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void S_SIL_1_A_client_with_a_sign_in_today_is_not_silent()
    {
        Assert.False(ClientSilence.IsSilent(TenantStatus.Active, Now.AddDays(-60), Now.AddHours(-2), Now, ClientSilence.DefaultDays));
    }

    [Fact]
    public void S_SIL_2_A_client_whose_last_activity_is_four_days_old_is_silent_for_three_days_but_not_for_seven()
    {
        var last = Now.AddDays(-4);

        Assert.True(ClientSilence.IsSilent(TenantStatus.Active, Now.AddDays(-60), last, Now, 3));
        Assert.True(ClientSilence.IsSilent(TenantStatus.Trial, Now.AddDays(-60), last, Now, 3));
        Assert.False(ClientSilence.IsSilent(TenantStatus.Active, Now.AddDays(-60), last, Now, 7));
    }

    [Fact]
    public void S_SIL_3_A_client_without_activity_is_silent_only_once_it_is_older_than_the_window()
    {
        Assert.False(ClientSilence.IsSilent(TenantStatus.Trial, Now.AddDays(-1), null, Now, 3));
        Assert.True(ClientSilence.IsSilent(TenantStatus.Trial, Now.AddDays(-4), null, Now, 3));
    }

    [Fact]
    public void S_SIL_4_An_inactive_client_is_never_silent()
    {
        Assert.False(ClientSilence.IsSilent(TenantStatus.Inactive, Now.AddDays(-60), Now.AddDays(-30), Now, 3));
        Assert.False(ClientSilence.IsSilent(TenantStatus.Inactive, Now.AddDays(-60), null, Now, 3));
    }

    [Fact]
    public void S_SIL_5_The_window_is_strict_activity_exactly_at_the_cutoff_is_not_older()
    {
        var cutoff = ClientSilence.Cutoff(Now, 3);

        Assert.Equal(Now.AddDays(-3), cutoff);
        Assert.False(ClientSilence.IsSilent(TenantStatus.Active, Now.AddDays(-60), cutoff, Now, 3));
        Assert.True(ClientSilence.IsSilent(TenantStatus.Active, Now.AddDays(-60), cutoff.AddTicks(-1), Now, 3));
    }

    [Fact]
    public void S_SIL_6_Last_activity_is_the_later_of_the_sign_in_and_the_registration()
    {
        var signIn = Now.AddDays(-5);
        var registration = Now.AddDays(-2);

        Assert.Equal(registration, ClientSilence.LastActivity(signIn, registration));
        Assert.Equal(registration, ClientSilence.LastActivity(registration, signIn));
        Assert.Equal(signIn, ClientSilence.LastActivity(signIn, null));
        Assert.Equal(registration, ClientSilence.LastActivity(null, registration));
        Assert.Null(ClientSilence.LastActivity(null, null));

        // A recent registration keeps a client whose users last signed in long ago off the list.
        Assert.False(ClientSilence.IsSilent(TenantStatus.Active, Now.AddDays(-60), ClientSilence.LastActivity(signIn, registration), Now, 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(91)]
    public void S_SIL_7_A_window_outside_1_to_90_days_is_refused(int days)
    {
        Assert.False(ClientSilence.IsValidDays(days));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClientSilence.Cutoff(Now, days));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClientSilence.IsSilent(TenantStatus.Active, Now.AddDays(-60), null, Now, days));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(90)]
    public void S_SIL_7_A_window_from_1_to_90_days_is_accepted(int days) => Assert.True(ClientSilence.IsValidDays(days));

    [Fact]
    public void S_SIL_8_Days_silent_count_whole_days_since_the_last_activity_or_the_creation()
    {
        Assert.Equal(4, ClientSilence.DaysSilent(Now.AddDays(-60), Now.AddDays(-4).AddHours(-3), Now));
        Assert.Equal(10, ClientSilence.DaysSilent(Now.AddDays(-10), null, Now));
        Assert.Equal(0, ClientSilence.DaysSilent(Now.AddDays(-10), Now.AddMinutes(5), Now));
    }
}
