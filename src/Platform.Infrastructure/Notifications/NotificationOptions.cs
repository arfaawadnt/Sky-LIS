namespace Platform.Infrastructure.Notifications;

/// <summary>
/// <c>Lis:Notifications</c>: how one-time codes and other notifications leave the host (SRS CP-02, CP-03, SA-20).
/// Each channel picks its own delivery from what is configured: e-mail goes to SMTP when <see cref="Smtp"/> has a
/// host, SMS goes to the HTTP gateway when <see cref="Sms"/> has a URL, and a channel without a gateway is written to
/// <see cref="PickupDirectory"/> when one is set (hosts without a gateway). With nothing configured the logging
/// sender stays the default. Secrets (<c>Smtp:Password</c>, <c>Sms:ApiKey</c>) come from the protected production
/// file, user-secrets or <c>Lis__Notifications__…</c> environment variables, never from a committed file.
/// <see cref="Platform.Infrastructure.Security.ConfigGuard.InspectNotifications"/> checks the values at start-up.
/// </summary>
public sealed class NotificationOptions
{
    public const string PickupDirectoryKey = "Lis:Notifications:PickupDirectory";
    public const string SmtpHostKey = "Lis:Notifications:Smtp:Host";
    public const string SmtpPortKey = "Lis:Notifications:Smtp:Port";
    public const string SmtpEnableSslKey = "Lis:Notifications:Smtp:EnableSsl";
    public const string SmtpUserNameKey = "Lis:Notifications:Smtp:UserName";
    public const string SmtpPasswordKey = "Lis:Notifications:Smtp:Password";
    public const string SmtpFromKey = "Lis:Notifications:Smtp:From";
    public const string SmtpTimeoutKey = "Lis:Notifications:Smtp:Timeout";
    public const string SmsUrlKey = "Lis:Notifications:Sms:Url";
    public const string SmsApiKeyHeaderKey = "Lis:Notifications:Sms:ApiKeyHeader";
    public const string SmsApiKeyKey = "Lis:Notifications:Sms:ApiKey";
    public const string SmsSenderKey = "Lis:Notifications:Sms:Sender";
    public const string SmsBodyTemplateKey = "Lis:Notifications:Sms:BodyTemplate";
    public const string SmsTimeoutKey = "Lis:Notifications:Sms:Timeout";

    /// <summary>The reserved production data root of CLAUDE.md §2; in Production the pickup directory lies under it.</summary>
    public const string ProductionDataRoot = @"E:\lis-data";

    /// <summary>
    /// Absolute folder that receives one text file per message (dev <c>E:\LIS\.local\mail-pickup</c>, production
    /// <c>E:\lis-data\mail-pickup</c>); created on demand. The files hold one-time codes: the folder is readable by
    /// SYSTEM, Administrators and the service account only (install-api-service.ps1).
    /// </summary>
    public string? PickupDirectory { get; init; }

    public SmtpNotificationOptions Smtp { get; init; } = new();

    public SmsGatewayOptions Sms { get; init; } = new();

    public bool HasPickupDirectory => !string.IsNullOrWhiteSpace(PickupDirectory);

    /// <summary>True when at least one channel delivers somewhere other than the log.</summary>
    public bool HasDelivery => HasPickupDirectory || Smtp.IsConfigured || Sms.IsConfigured;
}

/// <summary><c>Lis:Notifications:Smtp</c>: e-mail through <c>System.Net.Mail</c> (STARTTLS when <see cref="EnableSsl"/>).</summary>
public sealed class SmtpNotificationOptions
{
    /// <summary>Empty = no SMTP; e-mail falls back to the pickup directory or the log.</summary>
    public string? Host { get; init; }

    /// <summary>587 (submission with STARTTLS) by default. Implicit TLS on 465 is not supported by <c>System.Net.Mail</c>.</summary>
    public int Port { get; init; } = 587;

    /// <summary>STARTTLS. May be false only for a loopback relay (ConfigGuard).</summary>
    public bool EnableSsl { get; init; } = true;

    public string? UserName { get; init; }

    /// <summary>Secret: never in a committed file.</summary>
    public string? Password { get; init; }

    /// <summary>The sender address, optionally with a display name (<c>Sky LIS &lt;no-reply@example.net&gt;</c>).</summary>
    public string? From { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}

/// <summary>
/// <c>Lis:Notifications:Sms</c>: a generic HTTP SMS gateway. Each message is one POST of <see cref="BodyTemplate"/>
/// (JSON) to <see cref="Url"/> with the API key in the header <see cref="ApiKeyHeader"/>. Tokens <c>{to}</c>,
/// <c>{text}</c>, <c>{sender}</c>, <c>{template}</c> and <c>{language}</c> are replaced by JSON-escaped values, so
/// they belong inside JSON string quotes. Any 2xx answer is a success.
/// </summary>
public sealed class SmsGatewayOptions
{
    public const string DefaultBodyTemplate = """{"to":"{to}","text":"{text}"}""";

    /// <summary>Empty = no gateway; SMS falls back to the pickup directory or the log.</summary>
    public string? Url { get; init; }

    public string ApiKeyHeader { get; init; } = "X-Api-Key";

    /// <summary>Secret: never in a committed file.</summary>
    public string? ApiKey { get; init; }

    /// <summary>The sender id the gateway shows, for <c>{sender}</c>.</summary>
    public string? Sender { get; init; }

    public string BodyTemplate { get; init; } = DefaultBodyTemplate;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url);
}
