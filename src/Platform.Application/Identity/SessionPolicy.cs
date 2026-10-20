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

    /// <summary>The ceiling of <see cref="RefreshReuseGrace"/>; a configured value above it is clamped.</summary>
    public static readonly TimeSpan MaxRefreshReuseGrace = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Validation 2026-10-11 C-02: how long after a rotation the <b>immediately previous</b> refresh token of a browser session
    /// is still answered with the session that replaced it, instead of being treated as reuse. The tabs of one browser share
    /// the refresh cookie, so two tabs refreshing at the same moment present the same token twice; the second request must
    /// not revoke the family. Outside the window, or for an older generation, presenting a rotated token is reuse (02 §4.3).
    /// <c>Lis:Security:Session:RefreshReuseGrace</c>; zero disables it; clamped to <see cref="MaxRefreshReuseGrace"/>.
    /// </summary>
    public TimeSpan RefreshReuseGrace { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary><see cref="RefreshReuseGrace"/> bounded to [0, <see cref="MaxRefreshReuseGrace"/>].</summary>
    public TimeSpan EffectiveRefreshReuseGrace =>
        RefreshReuseGrace <= TimeSpan.Zero ? TimeSpan.Zero : RefreshReuseGrace > MaxRefreshReuseGrace ? MaxRefreshReuseGrace : RefreshReuseGrace;

    /// <summary>One-time codes of the platform's own flows (CP-02, CP-03): digits, lifetime and attempts (02 §4.5 as a capability).</summary>
    public int OtpLength { get; init; } = 6;

    public TimeSpan OtpLifetime { get; init; } = TimeSpan.FromMinutes(5);

    public int OtpMaxAttempts { get; init; } = 5;

    /// <summary>Minimum length of a password set through recovery (SRS CP-02/CP-03 say 8; 02 §6.1 recommends 12; tenant-configurable later through <c>SettingKeys.PasswordMinLength</c>).</summary>
    public int PasswordMinLength { get; init; } = 8;

    public const int PasswordMaxLength = 128;
}

/// <summary>A user's active memberships across tenants (D-06), in laboratories that are usable (INV-PLT-003). Elevated read: sign-in picks the membership of the tenant its address names (LIS-CR-005); Organization checks sole tenancy.</summary>
public sealed record MembershipSummary(Guid TenantId, Guid MembershipId, string DisplayNameEn, string DisplayNameAr, string Language, IReadOnlyList<Guid> BranchIds);

public interface IMembershipDirectory
{
    Task<IReadOnlyList<MembershipSummary>> ListActiveForUserAsync(Guid platformUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the user holds an active membership in a client laboratory that is not usable (deactivated, or trial
    /// ended: INV-PLT-003). Sign-in names that refusal <c>CLI-020</c> to the laboratory's own members only; when
    /// <paramref name="tenantId"/> is given only that laboratory is considered (the one the sign-in address names, LIS-CR-005).
    /// </summary>
    Task<bool> HasInactiveClientMembershipAsync(Guid platformUserId, Guid? tenantId, CancellationToken cancellationToken);
}

/// <summary>LIS-CR-005: the client tenant a laboratory address names, and whether its users may sign in now (Active, or Trial before its end: INV-PLT-003).</summary>
public sealed record LaboratoryAddress(Guid TenantId, bool IsUsable);

/// <summary>
/// LIS-CR-005: resolves a laboratory address (<c>saas.tenants.portal_slug</c>) at sign-in, before any tenant is known. Reads the
/// platform table only (no RLS, no elevation). <see langword="null"/> for an unknown address and for the platform tenant, which
/// has no laboratory address (its staff sign in at the Super Admin address).
/// </summary>
public interface ILaboratoryAddresses
{
    Task<LaboratoryAddress?> FindAsync(string slug, CancellationToken cancellationToken);
}
