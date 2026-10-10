namespace Platform.Application.Identity;

/// <summary>Session rules of 02 §4.1 and the credential policy of 02 §6.1. Every value is configuration; the defaults are the specification's.</summary>
public sealed class SessionPolicy
{
    /// <summary>The specification maximum (D-01, C-05). A gate fails when any configuration exceeds it.</summary>
    public static readonly TimeSpan MaxAccessTokenLifetime = TimeSpan.FromMinutes(15);

    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan BrowserSlidingLifetime { get; init; } = TimeSpan.FromHours(8);

    public TimeSpan BrowserAbsoluteLifetime { get; init; } = TimeSpan.FromHours(12);

    public TimeSpan MachineSlidingLifetime { get; init; } = TimeSpan.FromDays(30);

    public TimeSpan MachineAbsoluteLifetime { get; init; } = TimeSpan.FromDays(90);

    /// <summary>Consecutive failed credential checks before a lockout; 429s and envelope refusals never count.</summary>
    public int LockoutThreshold { get; init; } = 5;

    public TimeSpan LockoutDuration { get; init; } = TimeSpan.FromMinutes(30);

    public TimeSpan StepUpMaxAge { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>One-time codes of the platform's own flows (CP-02, CP-03): digits, lifetime and attempts (02 §4.5 as a capability).</summary>
    public int OtpLength { get; init; } = 6;

    public TimeSpan OtpLifetime { get; init; } = TimeSpan.FromMinutes(5);

    public int OtpMaxAttempts { get; init; } = 5;

    /// <summary>Minimum length of a password set through recovery (SRS CP-02/CP-03 say 8; 02 §6.1 recommends 12; tenant-configurable later through <c>SettingKeys.PasswordMinLength</c>).</summary>
    public int PasswordMinLength { get; init; } = 8;

    public const int PasswordMaxLength = 128;
}

/// <summary>A user's active memberships across tenants, listed at sign-in to select the tenant (D-06). Elevated read: the only consumer is the sign-in handler.</summary>
public sealed record MembershipSummary(Guid TenantId, Guid MembershipId, string DisplayNameEn, string DisplayNameAr, string Language, IReadOnlyList<Guid> BranchIds);

public interface IMembershipDirectory
{
    Task<IReadOnlyList<MembershipSummary>> ListActiveForUserAsync(Guid platformUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the user holds an active membership in a client laboratory that is not usable (deactivated, or trial
    /// ended: INV-PLT-003). Sign-in names that refusal <c>CLI-020</c> instead of the generic "no membership"; when
    /// <paramref name="tenantId"/> is given only that laboratory is considered (the tenant chosen on the sign-in page).
    /// </summary>
    Task<bool> HasInactiveClientMembershipAsync(Guid platformUserId, Guid? tenantId, CancellationToken cancellationToken);
}
