using System.Globalization;
using Platform.Domain.Authorization;

namespace Platform.Domain.PlatformAdmin;

// Change request LIS-CR-012 (reviewer point C-16, decision D-3 on its recommendation): subscription reminders and the grace
// period. The rules here are pure: readers bring the stored columns (client status, trial end, wallet balance, the moment the
// wallet reached zero, the plan fee) and the platform's grace days; handlers, the session principal and the reminder job bring
// the clock. Nothing is stored about the state itself: it is computed wherever it is needed, so it can never go stale.

/// <summary>
/// Refusal codes of the subscription state (LIS-CR-012). <c>SUB-001…SUB-010</c> already belong to the subscription wizard
/// (<see cref="PlatformAdminCodes"/>), so the expiry codes start at <c>SUB-020</c>.
/// </summary>
public static class SubscriptionCodes
{
    /// <summary>
    /// The client's subscription has expired (its grace period is over): a user who is not an administrator of the laboratory
    /// is refused at sign-in (<c>403</c>; reason <c>subscription_expired</c> in <c>SIGN_IN_FAILED</c>) and an open session of such
    /// a user ends (the principal is no longer live).
    /// </summary>
    public const string Expired = "SUB-020";

    /// <summary>
    /// An administrator of an expired client may sign in but reach only the sign-in surface, the own profile, the wallet (top-up
    /// request) and support; every other API request is refused <c>403 SUB-021</c> (<c>SubscriptionExpiredGateMiddleware</c>).
    /// </summary>
    public const string AdministratorOnlyBilling = "SUB-021";
}

/// <summary>
/// Who administers a laboratory for the subscription (D-3): its primary administrator, or a member whose effective privileges hold
/// <c>organization.manage</c> (who could grant itself anything). Only they keep signing in once the subscription expired.
/// </summary>
public static class SubscriptionAdministrators
{
    public static readonly string OrganizationManage = PermissionCatalog.Key(PermissionCatalog.Organization, PermissionAction.Manage);

    public static bool Includes(bool isPrimaryAdministrator, IEnumerable<string> effectivePermissionKeys)
    {
        ArgumentNullException.ThrowIfNull(effectivePermissionKeys);
        return isPrimaryAdministrator || effectivePermissionKeys.Contains(OrganizationManage, StringComparer.Ordinal);
    }
}

/// <summary>How urgent the subscription of a client is (LIS-CR-012).</summary>
public enum SubscriptionLevel
{
    /// <summary>Nothing to say: an active client whose wallet covers at least a week, a trial with more than a week left, a deactivated client (its refusal is <c>CLI-020</c>).</summary>
    Ok = 0,

    /// <summary>The trial ends within a week, or the wallet covers less than a week of the plan fee, or it is at or below zero but not for a whole plan period yet.</summary>
    Warning = 1,

    /// <summary>The trial ended (not activated) or the wallet has been at or below zero for longer than the plan period: the client keeps working for the grace days.</summary>
    Grace = 2,

    /// <summary>The grace is over: only the laboratory's administrators sign in, to the wallet (D-3).</summary>
    Expired = 3,
}

/// <summary>What drives a level other than <see cref="SubscriptionLevel.Ok"/>.</summary>
public enum SubscriptionReason
{
    None = 0,
    Trial = 1,

    /** The wallet covers fewer than <see cref="SubscriptionTerms.WarningDays"/> days of the plan fee (still positive). */
    Wallet = 2,

    /** The wallet is at or below zero (since <see cref="WalletPosition.AtOrBelowZeroSinceUtc"/>). */
    WalletAtZero = 3,
}

/// <summary>
/// The wallet and plan figures the state needs: balance, the plan's monthly fee and the wallet currency, the moment the balance
/// last reached zero or below (null while positive), and the next monthly charge (anchors the low-balance reminders per period).
/// </summary>
public sealed record WalletPosition(decimal Balance, decimal MonthlyFee, string CurrencyCode, DateTimeOffset? AtOrBelowZeroSinceUtc, DateTimeOffset? NextChargeAtUtc = null)
{
    /// <summary>Days of the plan fee the balance covers (balance ÷ (fee ÷ 30)); null without a fee.</summary>
    public decimal? DaysCovered => MonthlyFee > 0 ? Balance / (MonthlyFee / SubscriptionTerms.DaysPerMonth) : null;

    /// <summary>What brings the balance back to one month of the fee (never negative, two decimals).</summary>
    public decimal AmountToOneMonth => Math.Round(Math.Max(0m, MonthlyFee - Balance), 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// The computed subscription state of a client. <see cref="DaysLeft"/> counts whole days (floor) until <see cref="EndsAtUtc"/>:
/// the trial end (Warning, trial), the start of the grace (Warning, wallet at zero), the end of the cover (Warning, wallet low),
/// the end of the grace (Grace, Expired). <see cref="AmountToTopUp"/> brings the wallet back to one month of the plan fee.
/// </summary>
public sealed record SubscriptionStatus(
    SubscriptionLevel Level,
    SubscriptionReason Reason,
    int? DaysLeft,
    DateTimeOffset? EndsAtUtc,
    decimal? AmountToTopUp,
    string? CurrencyCode,
    DateTimeOffset? TrialEndsAtUtc)
{
    public static readonly SubscriptionStatus None = new(SubscriptionLevel.Ok, SubscriptionReason.None, null, null, null, null, null);

    public bool IsExpired => Level == SubscriptionLevel.Expired;

    /// <summary>The sign-in modal (C-16): fewer than <see cref="SubscriptionTerms.ModalDays"/> days left, in grace, or expired.</summary>
    public bool CallsForModal => Level is SubscriptionLevel.Grace or SubscriptionLevel.Expired
        || (Level == SubscriptionLevel.Warning && DaysLeft is { } days && days < SubscriptionTerms.ModalDays);
}

/// <summary>The rule of the subscription state (LIS-CR-012 / D-3).</summary>
public static class SubscriptionTerms
{
    /// <summary>The platform parameter's default (<c>subscriptionGraceDays</c>).</summary>
    public const int DefaultGraceDays = 7;

    public const int MinGraceDays = 0;

    public const int MaxGraceDays = 60;

    /// <summary>A trial ending within this many days, or a wallet covering fewer days, is a <see cref="SubscriptionLevel.Warning"/>.</summary>
    public const int WarningDays = 7;

    /// <summary>The sign-in modal shows when fewer days than this remain.</summary>
    public const int ModalDays = 3;

    /// <summary>The daily share of a monthly fee: fee ÷ 30.</summary>
    public const decimal DaysPerMonth = 30m;

    /// <summary>
    /// The state of a client at <paramref name="nowUtc"/>:
    /// <list type="bullet">
    /// <item>Inactive → <see cref="SubscriptionStatus.None"/> (deactivation is refused by <c>CLI-020</c>, never by this rule).</item>
    /// <item>Trial: more than <see cref="WarningDays"/> days left → Ok; fewer → Warning (days to the end); ended → Grace for the
    /// grace days, then Expired. A trial without an end date is Ok.</item>
    /// <item>Active without a wallet or with a free plan → Ok. Wallet at or below zero → Warning until it has been so for one plan
    /// period (a month from the moment it reached zero), then Grace for the grace days, then Expired. A positive wallet covering
    /// fewer than <see cref="WarningDays"/> days of the fee → Warning (whole days covered).</item>
    /// </list>
    /// <paramref name="graceDays"/> is clamped to <see cref="MinGraceDays"/>…<see cref="MaxGraceDays"/>.
    /// </summary>
    public static SubscriptionStatus Evaluate(TenantStatus status, DateTimeOffset? trialEndsAtUtc, WalletPosition? wallet, int graceDays, DateTimeOffset nowUtc)
    {
        var grace = Math.Clamp(graceDays, MinGraceDays, MaxGraceDays);
        var amount = wallet is { MonthlyFee: > 0 } ? wallet.AmountToOneMonth : (decimal?)null;
        var currency = wallet?.CurrencyCode;
        switch (status)
        {
            case TenantStatus.Trial when trialEndsAtUtc is { } trialEnd:
                if (nowUtc < trialEnd)
                {
                    var left = DaysUntil(nowUtc, trialEnd);
                    return left <= WarningDays
                        ? new SubscriptionStatus(SubscriptionLevel.Warning, SubscriptionReason.Trial, left, trialEnd, amount, currency, trialEnd)
                        : new SubscriptionStatus(SubscriptionLevel.Ok, SubscriptionReason.None, null, null, null, null, trialEnd);
                }

                return AfterEnd(SubscriptionReason.Trial, trialEnd, grace, nowUtc, amount, currency, trialEnd);

            case TenantStatus.Active when wallet is { MonthlyFee: > 0 }:
                if (wallet.Balance <= 0)
                {
                    var overdueAt = (wallet.AtOrBelowZeroSinceUtc ?? nowUtc).AddMonths(1);
                    return nowUtc < overdueAt
                        ? new SubscriptionStatus(SubscriptionLevel.Warning, SubscriptionReason.WalletAtZero, DaysUntil(nowUtc, overdueAt), overdueAt, amount, currency, null)
                        : AfterEnd(SubscriptionReason.WalletAtZero, overdueAt, grace, nowUtc, amount, currency, null);
                }

                var covered = wallet.DaysCovered!.Value;
                if (covered < WarningDays)
                {
                    var coverEnds = nowUtc.AddDays((double)covered);
                    return new SubscriptionStatus(SubscriptionLevel.Warning, SubscriptionReason.Wallet, (int)Math.Floor(covered), coverEnds, amount, currency, null);
                }

                return SubscriptionStatus.None;

            default:
                return SubscriptionStatus.None;
        }
    }

    /// <summary>True when the grace days of the platform parameter are in range.</summary>
    public static bool IsValidGraceDays(int days) => days is >= MinGraceDays and <= MaxGraceDays;

    /// <summary>Whole days (floor) from <paramref name="nowUtc"/> to <paramref name="endUtc"/>; 0 within the last day; never negative.</summary>
    public static int DaysUntil(DateTimeOffset nowUtc, DateTimeOffset endUtc) => Math.Max(0, (int)Math.Floor((endUtc - nowUtc).TotalDays));

    private static SubscriptionStatus AfterEnd(SubscriptionReason reason, DateTimeOffset endedAt, int graceDays, DateTimeOffset nowUtc, decimal? amount, string? currency, DateTimeOffset? trialEnd)
    {
        var graceEnds = endedAt.AddDays(graceDays);
        return nowUtc < graceEnds
            ? new SubscriptionStatus(SubscriptionLevel.Grace, reason, DaysUntil(nowUtc, graceEnds), graceEnds, amount, currency, trialEnd)
            : new SubscriptionStatus(SubscriptionLevel.Expired, reason, 0, graceEnds, amount, currency, trialEnd);
    }
}

/// <summary>The reminders the job sends (LIS-CR-012): trial 7 / 3 / 1 / today / ended, wallet 7 / 3 / 1 and at zero, grace 3 / 1.</summary>
public enum SubscriptionReminderKind
{
    TrialEnding = 0,
    TrialEnded = 1,
    WalletLow = 2,
    WalletAtZero = 3,
    GraceEnding = 4,
}

/// <summary>
/// One reminder that is due: its kind, its threshold in days (0 = today) and the key that makes it once-only per client
/// (<c>saas.subscription_reminders</c> is unique on client + key).
/// </summary>
public sealed record DueReminder(SubscriptionReminderKind Kind, int Threshold, string Key, int DaysLeft);

/// <summary>
/// Which reminder a state calls for. At most one per client and run — the most urgent of the state — so a client whose job
/// did not run for a while gets the current reminder, not a backlog. Keys carry an anchor so the same threshold fires again
/// only for a new trial end, a new zero episode, a new charge period or a new grace end:
/// <list type="bullet">
/// <item>Trial Warning: <c>trial-{7|3|1|0}:{trial end}</c> (0 = it ends today).</item>
/// <item>Wallet Warning: at or below zero <c>wallet-zero:{since}</c>; positive <c>wallet-{7|3|1}:{next charge}</c>.</item>
/// <item>Grace: <c>grace-{3|1}:{grace end}</c> within three days of its end; earlier, the opening reminder of its cause —
/// <c>trial-ended:{trial end}</c> or <c>wallet-zero:{since}</c> (already sent when the episode began).</item>
/// <item>Ok, Expired: none.</item>
/// </list>
/// </summary>
public static class SubscriptionReminders
{
    public const int MaxKeyLength = 80;

    public static DueReminder? Due(SubscriptionStatus status, WalletPosition? wallet)
    {
        ArgumentNullException.ThrowIfNull(status);
        var days = status.DaysLeft ?? 0;
        switch (status.Level)
        {
            case SubscriptionLevel.Warning when status.Reason == SubscriptionReason.Trial && status.TrialEndsAtUtc is { } trialEnd:
                var threshold = days == 0 ? 0 : days <= 1 ? 1 : days <= 3 ? 3 : 7;
                return new DueReminder(SubscriptionReminderKind.TrialEnding, threshold, $"trial-{threshold.ToString(CultureInfo.InvariantCulture)}:{Stamp(trialEnd)}", days);

            case SubscriptionLevel.Warning when status.Reason == SubscriptionReason.WalletAtZero && wallet is not null:
                return AtZero(wallet, status, days);

            case SubscriptionLevel.Warning when status.Reason == SubscriptionReason.Wallet && wallet is not null:
                var low = days < 1 ? 1 : days < 3 ? 3 : 7;
                var period = wallet.NextChargeAtUtc is { } next ? next.ToUniversalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture) : "none";
                return new DueReminder(SubscriptionReminderKind.WalletLow, low, $"wallet-{low.ToString(CultureInfo.InvariantCulture)}:{period}", days);

            case SubscriptionLevel.Grace when status.EndsAtUtc is { } graceEnd:
                if (days <= 3)
                {
                    var graceThreshold = days <= 1 ? 1 : 3;
                    return new DueReminder(SubscriptionReminderKind.GraceEnding, graceThreshold, $"grace-{graceThreshold.ToString(CultureInfo.InvariantCulture)}:{Stamp(graceEnd)}", days);
                }

                if (status.Reason == SubscriptionReason.Trial && status.TrialEndsAtUtc is { } ended)
                {
                    return new DueReminder(SubscriptionReminderKind.TrialEnded, 0, $"trial-ended:{Stamp(ended)}", days);
                }

                return wallet is not null ? AtZero(wallet, status, days) : null;

            default:
                return null;
        }
    }

    private static DueReminder AtZero(WalletPosition wallet, SubscriptionStatus status, int days)
    {
        var since = wallet.AtOrBelowZeroSinceUtc ?? status.EndsAtUtc?.AddMonths(-1) ?? DateTimeOffset.UnixEpoch;
        return new DueReminder(SubscriptionReminderKind.WalletAtZero, 0, $"wallet-zero:{Stamp(since)}", days);
    }

    private static string Stamp(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
}
