namespace Platform.Domain.PlatformAdmin;

/// <summary>
/// Silent clients (change request LIS-CR-007 A-01, SA-03 / SA-04): a client the Super Admin should call because nobody
/// has used the laboratory for a while. The last activity of a client is the latest of its users' last sign-in and its
/// last registration; a client is silent when that activity is older than <c>days</c> days, or when it has none at all
/// and was created more than <c>days</c> days ago. Inactive clients are never silent (they are blocked on purpose).
/// The rule is pure: the reader brings the timestamps, the handlers bring the clock.
/// </summary>
public static class ClientSilence
{
    /// <summary>The dashboard tile's window (A-01: "Silent for 3+ days").</summary>
    public const int DefaultDays = 3;

    public const int MinDays = 1;

    public const int MaxDays = 90;

    /// <summary>The windows the Clients page offers (the API takes any value from <see cref="MinDays"/> to <see cref="MaxDays"/>).</summary>
    public static readonly IReadOnlyList<int> OfferedDays = [3, 7, 14, 30];

    public static bool IsValidDays(int days) => days is >= MinDays and <= MaxDays;

    /// <summary>The start of the window: activity strictly before it is "older than <paramref name="days"/> days".</summary>
    public static DateTimeOffset Cutoff(DateTimeOffset nowUtc, int days)
    {
        if (!IsValidDays(days))
        {
            throw new ArgumentOutOfRangeException(nameof(days), days, $"The window is {MinDays} to {MaxDays} days.");
        }

        return nowUtc.AddDays(-days);
    }

    /// <summary>The latest of the two activities, null when there is none.</summary>
    public static DateTimeOffset? LastActivity(DateTimeOffset? lastSignInUtc, DateTimeOffset? lastRegistrationUtc) =>
        (lastSignInUtc, lastRegistrationUtc) switch
        {
            ({ } signIn, { } registration) => signIn >= registration ? signIn : registration,
            ({ } signIn, null) => signIn,
            (null, { } registration) => registration,
            _ => null,
        };

    /// <summary>
    /// True when the client is Trial or Active and its last activity is before <see cref="Cutoff"/>, or it has no activity
    /// and was created before the cutoff (a client created yesterday is not silent yet).
    /// </summary>
    public static bool IsSilent(TenantStatus status, DateTimeOffset createdAtUtc, DateTimeOffset? lastActivityUtc, DateTimeOffset nowUtc, int days)
    {
        var cutoff = Cutoff(nowUtc, days);
        if (status == TenantStatus.Inactive)
        {
            return false;
        }

        return lastActivityUtc is { } last ? last < cutoff : createdAtUtc < cutoff;
    }

    /// <summary>Whole days since the last activity (or since the creation when there is none); never negative.</summary>
    public static int DaysSilent(DateTimeOffset createdAtUtc, DateTimeOffset? lastActivityUtc, DateTimeOffset nowUtc)
    {
        var since = lastActivityUtc ?? createdAtUtc;
        var days = (int)Math.Floor((nowUtc - since).TotalDays);
        return Math.Max(days, 0);
    }
}
