using System.Globalization;
using Platform.Application.Abstractions.Notifications;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin.Jobs;

// Change request LIS-CR-012 (C-16): subscription reminders. A system job (Lis:Jobs:SubscriptionReminders, hosted by the
// WebApi next to the wallet charge) lists the Trial and Active clients and hands each to SubscriptionReminderRun in its own
// platform-tenant scope. Like the wallet charge this file is outside a Commands folder: the run commits once PER CLIENT (the
// reminder row) so one client's failure never undoes another's (TransitionAtomicityTests covers the Commands folders).

/// <summary>The reminder pass: implemented by the infrastructure job (<c>SubscriptionReminderJob</c>).</summary>
public interface ISubscriptionReminderRun
{
    /// <summary>One pass at the clock's time; serialised in-process, idempotent across processes (the reminder key is unique per client).</summary>
    Task<SubscriptionReminderRunSummary> RunOnceAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reminds ONE client, in the platform tenant's scope (the saas tables carry no tenant and no RLS: no elevation). Rules:
/// <list type="number">
/// <item>The state at the run's time under the platform's grace days (<see cref="SubscriptionTerms.Evaluate"/>) names at most one
/// due reminder (<see cref="SubscriptionReminders.Due"/>): none → skipped <c>nothing-due</c>.</item>
/// <item>Its key already recorded for the client → skipped <c>already-sent</c> (never twice).</item>
/// <item>Otherwise the reminder row is recorded and committed <b>first</b> — a concurrent run that recorded the same key loses on the
/// unique index (<c>UNIQUE-409</c>) and reads as <c>already-sent</c> — and the message is then handed to SMS (the manager's mobile)
/// and e-mail (the client's e-mail when it has one) through <see cref="INotificationSender"/>, which absorbs a gateway failure.
/// At most once: a crash between the commit and the send loses that one reminder rather than repeating it.</item>
/// </list>
/// Templates: trial → <c>subscription-expiry</c>, wallet → <c>wallet-low</c>, grace → <c>subscription-grace</c>; placeholders
/// <c>labName</c> (alias <c>laboratory</c>), <c>days</c>, <c>amount</c> (to bring the wallet to one month of the fee, with the
/// currency), <c>url</c> (alias <c>link</c>: the laboratory's sign-in address).
/// </summary>
public sealed class SubscriptionReminderRun(
    ISubscriptionReminderRepository reminders,
    INotificationSender sender,
    IPortalAddressOptions portal,
    ICurrentTenant currentTenant,
    IUnitOfWork unitOfWork)
{
    public const string Sent = "Sent";
    public const string Skipped = "Skipped";
    public const string Failed = "Failed";

    public async Task<SubscriptionReminderRunItem> RemindAsync(ClientSubscriptionRow client, int graceDays, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        PlatformAdminScope.Require(currentTenant);
        var state = client.StateAt(nowUtc, graceDays);
        var due = SubscriptionReminders.Due(state, client.Wallet);
        if (due is null)
        {
            return new SubscriptionReminderRunItem(client.TenantId, Skipped, "nothing-due", null, null, []);
        }

        var template = TemplateFor(due.Kind);
        if (await reminders.ExistsAsync(client.TenantId, due.Key, cancellationToken))
        {
            return new SubscriptionReminderRunItem(client.TenantId, Skipped, "already-sent", due.Key, template, []);
        }

        var targets = Targets(client);
        reminders.Add(SubscriptionReminder.Record(client.TenantId, due, template, targets.Select(target => target.Channel.ToString()).ToList(), nowUtc));
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException exception) when (exception.Code == ProblemCodes.UniqueViolation)
        {
            return new SubscriptionReminderRunItem(client.TenantId, Skipped, "already-sent", due.Key, template, []);
        }

        if (targets.Count == 0)
        {
            return new SubscriptionReminderRunItem(client.TenantId, Skipped, "no-contact", due.Key, template, []);
        }

        var placeholders = Placeholders(client, state, due, portal.PortalBaseUrl);
        foreach (var (channel, to) in targets)
        {
            await sender.SendAsync(new NotificationMessage(channel, to, template, client.PreferredLanguage, placeholders), cancellationToken);
        }

        return new SubscriptionReminderRunItem(client.TenantId, Sent, null, due.Key, template, targets.Select(target => target.Channel.ToString()).ToList());
    }

    /// <summary>The template of a reminder kind.</summary>
    public static string TemplateFor(SubscriptionReminderKind kind) => kind switch
    {
        SubscriptionReminderKind.TrialEnding or SubscriptionReminderKind.TrialEnded => NotificationTemplateCodes.SubscriptionExpiry,
        SubscriptionReminderKind.WalletLow or SubscriptionReminderKind.WalletAtZero => NotificationTemplateCodes.WalletLow,
        SubscriptionReminderKind.GraceEnding => NotificationTemplateCodes.SubscriptionGrace,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown reminder kind."),
    };

    /// <summary>The values of the template's placeholders (never secret: no code, no password).</summary>
    public static IReadOnlyDictionary<string, string> Placeholders(ClientSubscriptionRow client, SubscriptionStatus state, DueReminder due, string portalBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(due);
        var arabic = client.PreferredLanguage.StartsWith("ar", StringComparison.OrdinalIgnoreCase);
        var amount = state.AmountToTopUp is { } value
            ? string.Create(CultureInfo.InvariantCulture, $"{value:N2} {state.CurrencyCode}").Trim()
            : string.Empty;
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["labName"] = arabic ? client.NameAr : client.NameEn,
            ["days"] = due.DaysLeft.ToString(CultureInfo.InvariantCulture),
            ["amount"] = amount,
            ["url"] = Tenant.PortalUrlOf(client.PortalSlug, portalBaseUrl),
        };
    }

    /// <summary>The summary of a pass from its rows (the rows that only skipped are counted, not listed).</summary>
    public static SubscriptionReminderRunSummary Summarise(DateTimeOffset runAtUtc, IReadOnlyList<SubscriptionReminderRunItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new SubscriptionReminderRunSummary(
            runAtUtc,
            items.Count,
            items.Count(item => item.Outcome == Sent),
            items.Count(item => item.Outcome == Skipped),
            items.Count(item => item.Outcome == Failed),
            items.Where(item => item.Outcome != Skipped || item.Reason == "no-contact").ToList());
    }

    private static List<(NotificationChannel Channel, string To)> Targets(ClientSubscriptionRow client)
    {
        var targets = new List<(NotificationChannel, string)>(2);
        if (!string.IsNullOrWhiteSpace(client.ManagerMobile))
        {
            targets.Add((NotificationChannel.Sms, client.ManagerMobile));
        }

        if (!string.IsNullOrWhiteSpace(client.Email))
        {
            targets.Add((NotificationChannel.Email, client.Email));
        }

        return targets;
    }
}
