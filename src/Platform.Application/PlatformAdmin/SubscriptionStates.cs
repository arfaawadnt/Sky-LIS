using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.PlatformAdmin;
using Platform.Domain.Settings;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.Application.PlatformAdmin;

// Change request LIS-CR-012 (C-16, D-3): the subscription state of the clients — reminders, grace, expiry. The rule is
// Domain.PlatformAdmin.SubscriptionTerms; the reader brings the stored columns of the platform tables (saas.tenants,
// saas.wallets, saas.plans, saas.subscription_reminders: no tenant, no RLS, no elevation) and the grace days come from the
// platform tenant's parameters only.

/// <summary>
/// A Trial or Active client with what its subscription state needs: the trial end, the plan fee, the wallet (null fields when the
/// client has no wallet), the manager's contacts for the reminders and the last reminder sent.
/// </summary>
public sealed record ClientSubscriptionRow(
    Guid TenantId,
    string NameEn,
    string NameAr,
    string PortalSlug,
    TenantStatus Status,
    DateTimeOffset? TrialEndsAtUtc,
    string PreferredLanguage,
    string ManagerName,
    string ManagerMobile,
    string? Email,
    decimal MonthlyFee,
    decimal? WalletBalance,
    string? WalletCurrencyCode,
    DateTimeOffset? WalletAtOrBelowZeroSinceUtc,
    DateTimeOffset? WalletNextChargeAtUtc,
    string? LastReminderKey = null,
    DateTimeOffset? LastReminderAtUtc = null)
{
    /// <summary>The wallet figures of the state; null without a wallet.</summary>
    public WalletPosition? Wallet => WalletBalance is { } balance
        ? new WalletPosition(balance, MonthlyFee, WalletCurrencyCode ?? string.Empty, WalletAtOrBelowZeroSinceUtc, WalletNextChargeAtUtc)
        : null;

    public SubscriptionStatus StateAt(DateTimeOffset nowUtc, int graceDays) =>
        SubscriptionTerms.Evaluate(Status, TrialEndsAtUtc, Wallet, graceDays, nowUtc);
}

/// <summary>The platform-wide reads of the subscription state (adapter in Persistence/PlatformAdmin; never elevates).</summary>
public interface ISubscriptionStateReader
{
    /// <summary>Every Trial or Active client (the platform tenant is not a client), with its last reminder.</summary>
    Task<IReadOnlyList<ClientSubscriptionRow>> ListClientsAsync(CancellationToken cancellationToken);

    /// <summary>One client; <see langword="null"/> without a client record (the platform tenant, a development tenant).</summary>
    Task<ClientSubscriptionRow?> FindAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>
/// The state of one client for sign-in (<c>SUB-020</c>): the platform's grace days and the client's figures, read without the
/// caller's tenant (sign-in is anonymous). <see cref="SubscriptionStatus.None"/> for the platform tenant and for a tenant
/// without a client record.
/// </summary>
public interface ISubscriptionStateLookup
{
    Task<SubscriptionStatus> FindAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>The log of sent reminders (<c>saas.subscription_reminders</c>); the run adds, the unit of work commits.</summary>
public interface ISubscriptionReminderRepository : IRepository<SubscriptionReminder>
{
    Task<bool> ExistsAsync(Guid tenantId, string dueKey, CancellationToken cancellationToken);
}

/// <summary>The platform's grace days: the platform tenant's own <c>subscriptionGraceDays</c> (SA-19), else 7.</summary>
public static class SubscriptionGrace
{
    public static async Task<int> DaysAsync(ITenantSettings settings, ICurrentTenantSetter tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(tenantScope);
        using (tenantScope.ForTenant(TenantIds.Platform))
        {
            return await settings.GetAsync(SettingKeys.SubscriptionGraceDays, cancellationToken);
        }
    }
}

/// <summary>The subscription filters of SA-04 and the SA-03 tile (LIS-CR-012).</summary>
public static class SubscriptionFilters
{
    public const string Expiring = "expiring";
    public const string Grace = "grace";
    public const string Expired = "expired";

    /// <summary>The window of "Expiring": the tile counts the clients whose trial, cover or grace ends within this many days.</summary>
    public const int DefaultDays = SubscriptionTerms.WarningDays;

    public const int MinDays = 1;

    public const int MaxDays = 60;

    public static readonly IReadOnlyList<string> All = [Expiring, Grace, Expired];

    public static bool IsKnown(string? state) => state is null || All.Contains(Normalise(state), StringComparer.Ordinal);

    public static string Normalise(string? state) => string.IsNullOrWhiteSpace(state) ? Expiring : state.Trim().ToLowerInvariant();

    /// <summary>
    /// Expiring = Warning or Grace ending within <paramref name="days"/> days (the tile: "trial ending or in grace within 7 days"; a
    /// wallet covering fewer days is included); Grace = every client in grace; Expired = every expired client.
    /// </summary>
    public static bool Matches(string state, SubscriptionStatus status, int days)
    {
        ArgumentNullException.ThrowIfNull(status);
        return Normalise(state) switch
        {
            Expiring => status.Level is SubscriptionLevel.Warning or SubscriptionLevel.Grace && (status.DaysLeft ?? 0) <= days,
            Grace => status.Level == SubscriptionLevel.Grace,
            Expired => status.Level == SubscriptionLevel.Expired,
            _ => false,
        };
    }
}

/// <summary>The clients of a subscription filter, shared by the SA-04 list and the SA-03 tile.</summary>
public static class SubscriptionClientFinder
{
    /// <summary>Every client with its state as of <paramref name="nowUtc"/> (Ok ones included; callers filter).</summary>
    public static async Task<IReadOnlyList<(ClientSubscriptionRow Row, SubscriptionStatus State)>> StatesAsync(ISubscriptionStateReader reader, DateTimeOffset nowUtc, int graceDays, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var rows = await reader.ListClientsAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(row => (row, row.StateAt(nowUtc, graceDays))).ToList();
    }

    /// <summary>The rows of a filter, soonest end first (an expired client by the oldest end), then by name.</summary>
    public static IReadOnlyList<SubscriptionClientItem> Filter(IEnumerable<(ClientSubscriptionRow Row, SubscriptionStatus State)> states, string state, int days, string portalBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(states);
        return states
            .Where(entry => SubscriptionFilters.Matches(state, entry.State, days))
            .OrderBy(entry => entry.State.EndsAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(entry => entry.Row.NameEn, StringComparer.OrdinalIgnoreCase)
            .Select(entry => ToItem(entry.Row, entry.State, portalBaseUrl))
            .ToList();
    }

    public static SubscriptionClientItem ToItem(ClientSubscriptionRow row, SubscriptionStatus state, string portalBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(state);
        return new SubscriptionClientItem(
            row.TenantId,
            row.NameEn,
            row.NameAr,
            Tenant.PortalUrlOf(row.PortalSlug, portalBaseUrl),
            row.Status.ToString(),
            state.Level.ToString(),
            state.Reason.ToString(),
            state.DaysLeft,
            state.EndsAtUtc,
            row.TrialEndsAtUtc,
            row.WalletBalance,
            row.MonthlyFee,
            row.WalletCurrencyCode,
            state.AmountToTopUp,
            row.ManagerName,
            row.ManagerMobile,
            row.LastReminderKey,
            row.LastReminderAtUtc);
    }
}
