using Platform.SharedKernel.Primitives;

namespace Platform.Domain.PlatformAdmin;

/// <summary>
/// One subscription reminder that was sent to a client (LIS-CR-012): <c>saas.subscription_reminders</c>, platform data (no
/// tenant scope, no RLS), unique on (client, <see cref="DueKey"/>) so the job never sends the same reminder twice — two runs
/// that race insert the same key and the loser's commit fails on the unique index. <see cref="Channels"/> names the channels
/// the message was handed to (<c>Sms</c>, <c>Email</c>, comma separated; <c>none</c> when the client has no address for any).
/// Append-only: a row is never changed.
/// </summary>
public sealed class SubscriptionReminder : AggregateRoot
{
    public const int MaxTemplateCodeLength = 40;
    public const int MaxChannelsLength = 40;
    public const string NoChannel = "none";

    private SubscriptionReminder()
    {
        DueKey = string.Empty;
        TemplateCode = string.Empty;
        Channels = string.Empty;
    }

    private SubscriptionReminder(Guid id, Guid tenantId, SubscriptionReminderKind kind, string dueKey, string templateCode, string channels, DateTimeOffset sentAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        Kind = kind;
        DueKey = dueKey;
        TemplateCode = templateCode;
        Channels = channels;
        SentAtUtc = sentAtUtc;
    }

    /// <summary>The client (<c>saas.tenants.id</c>).</summary>
    public Guid TenantId { get; private set; }

    public SubscriptionReminderKind Kind { get; private set; }

    /// <summary>The once-only key of <see cref="SubscriptionReminders.Due"/> (e.g. <c>trial-3:202611011200</c>).</summary>
    public string DueKey { get; private set; }

    public string TemplateCode { get; private set; }

    public string Channels { get; private set; }

    public DateTimeOffset SentAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>Records a reminder handed to the channels; the key and the template code are required.</summary>
    public static SubscriptionReminder Record(Guid tenantId, DueReminder due, string templateCode, IReadOnlyCollection<string> channels, DateTimeOffset sentAtUtc)
    {
        ArgumentNullException.ThrowIfNull(due);
        ArgumentNullException.ThrowIfNull(channels);
        Require(tenantId != Guid.Empty, PlatformAdminCodes.ClientNotFound, "A reminder belongs to a client.", DomainFault.Validation);
        Require(!string.IsNullOrWhiteSpace(due.Key) && due.Key.Length <= SubscriptionReminders.MaxKeyLength, PlatformAdminCodes.ClientDetailsInvalid, "The reminder key is 1 to 80 characters.", DomainFault.Validation);
        var code = NotificationTemplate.CleanCode(templateCode);
        var channelText = channels.Count == 0 ? NoChannel : string.Join(',', channels.Distinct(StringComparer.Ordinal));
        Require(channelText.Length <= MaxChannelsLength, PlatformAdminCodes.ClientDetailsInvalid, "Too many channels.", DomainFault.Validation);
        return new SubscriptionReminder(Guid.CreateVersion7(), tenantId, due.Kind, due.Key, code, channelText, sentAtUtc);
    }
}
