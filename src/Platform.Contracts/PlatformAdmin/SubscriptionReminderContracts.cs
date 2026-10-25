namespace Platform.Contracts.PlatformAdmin;

// Change request LIS-CR-012 (C-16): the subscription state of the clients on SA-03 / SA-04 and the reminder run.

/// <summary>
/// One client of a subscription filter (SA-04 "Subscription" = expiring / grace / expired). <paramref name="Level"/>:
/// Ok | Warning | Grace | Expired; <paramref name="Reason"/>: None | Trial | Wallet; <paramref name="DaysLeft"/> whole days to
/// <paramref name="EndsAtUtc"/> (the trial end, the end of the cover, the start or the end of the grace).
/// <paramref name="AmountToTopUp"/> brings the wallet back to one month of the plan fee. The last reminder sent is
/// <paramref name="LastReminderKey"/> (e.g. <c>trial-3:202611011200</c>) at <paramref name="LastReminderAtUtc"/>.
/// </summary>
public sealed record SubscriptionClientItem(
    Guid Id,
    string NameEn,
    string NameAr,
    string PortalUrl,
    string Status,
    string Level,
    string Reason,
    int? DaysLeft,
    DateTimeOffset? EndsAtUtc,
    DateTimeOffset? TrialEndsAtUtc,
    decimal? WalletBalance,
    decimal MonthlyFee,
    string? CurrencyCode,
    decimal? AmountToTopUp,
    string AdministratorName,
    string AdministratorMobile,
    string? LastReminderKey,
    DateTimeOffset? LastReminderAtUtc);

/// <summary>The clients of <paramref name="State"/> (expiring within <paramref name="Days"/> days, grace, expired) as of <paramref name="AsOfUtc"/> under <paramref name="GraceDays"/> days of grace.</summary>
public sealed record SubscriptionClientList(string State, int Days, int GraceDays, DateTimeOffset AsOfUtc, IReadOnlyList<SubscriptionClientItem> Items);

/// <summary>
/// One client of a reminder pass: <paramref name="Outcome"/> Sent | Skipped | Failed, <paramref name="Reason"/> for a skip or a
/// failure (<c>nothing-due</c>, <c>already-sent</c>, <c>no-contact</c>, <c>error</c>), the reminder key and the channels it went to.
/// </summary>
public sealed record SubscriptionReminderRunItem(Guid TenantId, string Outcome, string? Reason, string? Key, string? TemplateCode, IReadOnlyList<string> Channels);

/// <summary>A reminder pass (LIS-CR-012): counts per outcome and only the rows that did something (Sent / Failed).</summary>
public sealed record SubscriptionReminderRunSummary(DateTimeOffset RunAtUtc, int Clients, int Sent, int Skipped, int Failed, IReadOnlyList<SubscriptionReminderRunItem> Items);
