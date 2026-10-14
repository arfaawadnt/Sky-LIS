namespace Platform.Application.Abstractions.Notifications;

public enum NotificationChannel
{
    Sms = 0,
    Email = 1,
}

/// <summary>
/// One message to one recipient, by template (SRS CP-02, CP-03, SA-20). Placeholders are substituted into the
/// template's body by the sender; a placeholder named <see cref="SecretPlaceholders"/> (the one-time code) is
/// never logged. The platform's notification-template module replaces <see cref="INotificationTemplateSource"/>;
/// a gateway adapter replaces <see cref="INotificationSender"/>; the ports stay.
/// </summary>
public sealed record NotificationMessage(
    NotificationChannel Channel,
    string To,
    string TemplateCode,
    string Language,
    IReadOnlyDictionary<string, string> Placeholders)
{
    /// <summary>Placeholder names whose values are credential material and never reach a log.</summary>
    public static readonly IReadOnlySet<string> SecretPlaceholders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "code", "otp", "password" };
}

/// <summary>Sends a notification through a channel. The default adapter logs (without secrets); a gateway adapter delivers.</summary>
public interface INotificationSender
{
    Task SendAsync(NotificationMessage message, CancellationToken cancellationToken);
}

/// <summary>A rendered template: <c>{placeholder}</c> tokens in <see cref="Body"/> (and <see cref="Subject"/> for e-mail) are replaced at send time.</summary>
public sealed record NotificationTemplate(string Code, string Language, string? Subject, string Body)
{
    /// <summary>Substitutes every <c>{name}</c> token; unknown tokens stay as they are so a missing value is visible, never silent.</summary>
    public string Render(IReadOnlyDictionary<string, string> placeholders)
    {
        ArgumentNullException.ThrowIfNull(placeholders);
        var text = Body;
        foreach (var (name, value) in placeholders)
        {
            text = text.Replace("{" + name + "}", value, StringComparison.Ordinal);
        }

        return text;
    }
}

/// <summary>Looks a template up by code and language, falling back to English. The in-code default set covers the platform's own flows.</summary>
public interface INotificationTemplateSource
{
    Task<NotificationTemplate?> FindAsync(string code, string language, CancellationToken cancellationToken);
}

/// <summary>Template codes the foundation and the first modules send (SA-20 lists them as editable templates).</summary>
public static class NotificationTemplateCodes
{
    /// <summary>A one-time code for any flow (subscription wizard, CP-02). Placeholders: <c>code</c>, <c>minutes</c>.</summary>
    public const string Otp = "otp";

    /// <summary>A one-time code for password recovery (CP-03). Placeholders: <c>code</c>, <c>minutes</c>.</summary>
    public const string PasswordRecovery = "password-recovery";

    /// <summary>The portal address of a new laboratory (CP-02 step 4). Placeholders: <c>labName</c>, <c>url</c>, <c>username</c>.</summary>
    public const string PortalUrl = "portal-url";

    /// <summary>A treasury access request awaits a decision (CP-29). Placeholders: <c>number</c>, <c>requester</c>, <c>treasury</c>.</summary>
    public const string TreasuryRequest = "treasury-request";

    /// <summary>A panic value was verified (CP-20). Placeholders: <c>patient</c>, <c>test</c>, <c>value</c>, <c>accession</c>.</summary>
    public const string PanicValue = "panic-value";

    public static readonly IReadOnlyList<string> All = [Otp, PasswordRecovery, PortalUrl, TreasuryRequest, PanicValue];
}

/// <summary>
/// One-time codes (02 §4.5 as a capability; SRS CP-02/CP-03): issued for a purpose and a target (mobile or e-mail),
/// hashed at rest, valid five minutes, five attempts, consumed on first success. The code is sent through
/// <see cref="INotificationSender"/>; it comes back to the caller only on a host configured to reveal codes
/// (<c>Lis:Notifications:RevealCodes</c>, the interim of a laboratory without an SMS gateway: the subscription wizard
/// shows it on the page, 2026-10-14).
/// </summary>
public interface IOtpService
{
    /// <summary>Issues and sends a code; returns the challenge id and, when the host reveals codes, the code. An earlier live challenge for the same purpose and target is superseded.</summary>
    Task<OtpIssue> IssueAsync(string purpose, string target, string language, CancellationToken cancellationToken, string templateCode = NotificationTemplateCodes.Otp, NotificationChannel channel = NotificationChannel.Sms);

    /// <summary>True once, for the live challenge of this purpose and target; every wrong code counts an attempt and the fifth closes the challenge.</summary>
    Task<bool> VerifyAsync(string purpose, string target, string code, CancellationToken cancellationToken);
}

/// <summary>An issued one-time code: its challenge id and, only when <c>Lis:Notifications:RevealCodes</c> is on, the code.</summary>
public sealed record OtpIssue(Guid ChallengeId, string? RevealedCode);
