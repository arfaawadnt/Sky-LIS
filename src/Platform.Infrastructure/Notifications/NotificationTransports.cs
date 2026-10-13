using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Platform.Application.Abstractions.Notifications;
using Platform.SharedKernel.Abstractions;

namespace Platform.Infrastructure.Notifications;

/// <summary>A message after template rendering: what a gateway (or the pickup file) receives.</summary>
public sealed record RenderedNotification(NotificationChannel Channel, string To, string TemplateCode, string Language, string? Subject, string Body);

/// <summary>One way out of the host for a rendered message. Throws on failure; the sender logs and absorbs it.</summary>
internal interface INotificationTransport
{
    /// <summary>A short name for the log (<c>pickup</c>, <c>smtp</c>, <c>sms-http</c>).</summary>
    string Name { get; }

    Task DeliverAsync(RenderedNotification notification, CancellationToken cancellationToken);
}

/// <summary>
/// The transports configured under <c>Lis:Notifications</c>, one per channel: SMTP for e-mail and the HTTP gateway
/// for SMS when configured, otherwise the pickup directory when one is set, otherwise none (the channel is logged
/// only). A singleton: the HTTP gateway keeps one pooled client for the process.
/// </summary>
internal sealed class NotificationTransports : IDisposable
{
    private readonly HttpSmsTransport? _http;

    public NotificationTransports(NotificationOptions options, IClock clock)
        : this(options, clock, smsHandler: null)
    {
    }

    internal NotificationTransports(NotificationOptions options, IClock clock, HttpMessageHandler? smsHandler)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        var pickup = options.HasPickupDirectory ? new PickupDirectoryTransport(options.PickupDirectory!.Trim(), clock) : null;
        _http = options.Sms.IsConfigured ? new HttpSmsTransport(options.Sms, smsHandler) : null;
        Email = options.Smtp.IsConfigured ? new SmtpEmailTransport(options.Smtp) : pickup;
        Sms = (INotificationTransport?)_http ?? pickup;
    }

    public INotificationTransport? Email { get; }

    public INotificationTransport? Sms { get; }

    public bool Any => Email is not null || Sms is not null;

    public INotificationTransport? For(NotificationChannel channel) => channel switch
    {
        NotificationChannel.Email => Email,
        NotificationChannel.Sms => Sms,
        _ => null,
    };

    public void Dispose() => _http?.Dispose();
}

/// <summary>
/// Hosts without a gateway (validation, a server before its SMS contract): every message becomes one UTF-8 text file
/// <c>&lt;utc-stamp&gt;-&lt;channel&gt;-&lt;recipient-hash&gt;.txt</c> in the pickup directory, holding the channel,
/// recipient, template code, language, subject and body. The file is written under a temporary name and renamed when
/// complete, so a reader never sees half a message; a name already taken gets a <c>-2</c>, <c>-3</c> … suffix. The
/// recipient hash (first 12 hex digits of SHA-256 over the trimmed, lower-case recipient) lets a tester find the
/// messages of one recipient without the name carrying the number itself.
/// </summary>
internal sealed class PickupDirectoryTransport(string directory, IClock clock) : INotificationTransport
{
    public const string Extension = ".txt";
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public string Name => "pickup";

    public string Directory { get; } = directory;

    public async Task DeliverAsync(RenderedNotification notification, CancellationToken cancellationToken)
    {
        await WriteAsync(notification, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes the file; returns its name (without the folder).</summary>
    public async Task<string> WriteAsync(RenderedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        System.IO.Directory.CreateDirectory(Directory);
        var now = clock.UtcNow.ToUniversalTime();
        var stem = string.Create(CultureInfo.InvariantCulture, $"{now:yyyyMMdd'T'HHmmssfffffff'Z'}-{notification.Channel.ToString().ToLowerInvariant()}-{RecipientHash(notification.To)}");
        var temporary = Path.Combine(Directory, $"{stem}.{Guid.CreateVersion7():N}.partial");
        await File.WriteAllTextAsync(temporary, Format(notification, now), Utf8, cancellationToken).ConfigureAwait(false);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                var name = attempt == 1 ? stem + Extension : string.Create(CultureInfo.InvariantCulture, $"{stem}-{attempt}{Extension}");
                var target = Path.Combine(Directory, name);
                try
                {
                    File.Move(temporary, target, overwrite: false);
                    return name;
                }
                catch (IOException) when (attempt < 1000 && File.Exists(target))
                {
                    // same instant, same recipient: take the next suffix
                }
            }
        }
        finally
        {
            File.Delete(temporary);   // no-op once moved
        }
    }

    /// <summary>The text of one pickup file: header lines, a blank line, the body.</summary>
    public static string Format(RenderedNotification notification, DateTimeOffset createdUtc)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var text = new StringBuilder();
        text.Append("Channel: ").Append(notification.Channel).Append("\r\n");
        text.Append("To: ").Append(OneLine(notification.To)).Append("\r\n");
        text.Append("Template: ").Append(OneLine(notification.TemplateCode)).Append("\r\n");
        text.Append("Language: ").Append(OneLine(notification.Language)).Append("\r\n");
        text.Append("Subject: ").Append(OneLine(notification.Subject ?? string.Empty)).Append("\r\n");
        text.Append("Created-Utc: ").Append(createdUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)).Append("\r\n");
        text.Append("\r\n");
        text.Append(notification.Body).Append("\r\n");
        return text.ToString();
    }

    public static string RecipientHash(string recipient)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(recipient.Trim().ToLowerInvariant()));
        return Convert.ToHexStringLower(digest)[..12];
    }

    private static string OneLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
}

/// <summary>
/// E-mail through <c>System.Net.Mail.SmtpClient</c> (no extra package): STARTTLS when <c>EnableSsl</c>, credentials
/// when a user name is configured, plain text in UTF-8 with base64 transfer encoding (Arabic survives every relay).
/// A client per message: <c>SmtpClient</c> is not safe for concurrent sends.
/// </summary>
internal sealed class SmtpEmailTransport(SmtpNotificationOptions options) : INotificationTransport
{
    public const string FallbackSubject = "Sky LIS";

    public string Name => "smtp";

    public async Task DeliverAsync(RenderedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        using var message = new MailMessage
        {
            From = new MailAddress(options.From!),
            Subject = string.IsNullOrWhiteSpace(notification.Subject) ? FallbackSubject : notification.Subject,
            SubjectEncoding = Encoding.UTF8,
            HeadersEncoding = Encoding.UTF8,
            Body = notification.Body,
            BodyEncoding = Encoding.UTF8,
            BodyTransferEncoding = TransferEncoding.Base64,
            IsBodyHtml = false,
        };
        message.To.Add(new MailAddress(notification.To.Trim()));

        using var client = new SmtpClient(options.Host!.Trim(), options.Port)
        {
            EnableSsl = options.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Timeout = (int)Math.Clamp(options.Timeout.TotalMilliseconds, 1000, int.MaxValue),
        };
        if (!string.IsNullOrWhiteSpace(options.UserName))
        {
            client.Credentials = new NetworkCredential(options.UserName, options.Password);
        }

        await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// A generic HTTP SMS gateway: one POST of the configured JSON body per message, the API key in the configured
/// header, any 2xx answer is a success. The body is never logged.
/// </summary>
internal sealed partial class HttpSmsTransport : INotificationTransport, IDisposable
{
    private readonly SmsGatewayOptions _options;
    private readonly HttpClient _http;

    public HttpSmsTransport(SmsGatewayOptions options, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _http = handler is null
            ? new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }, disposeHandler: true)
            : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = options.Timeout > TimeSpan.Zero ? options.Timeout : TimeSpan.FromSeconds(15);
    }

    public string Name => "sms-http";

    public async Task DeliverAsync(RenderedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var body = RenderBody(_options.BodyTemplate, notification.To.Trim(), notification.Body, _options.Sender, notification.TemplateCode, notification.Language);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.Url!.Trim(), UriKind.Absolute))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrEmpty(_options.ApiKey))
        {
            request.Headers.TryAddWithoutValidation(_options.ApiKeyHeader, _options.ApiKey);
        }

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(string.Create(CultureInfo.InvariantCulture, $"The SMS gateway answered {(int)response.StatusCode}."), null, response.StatusCode);
        }
    }

    /// <summary>Replaces each token in one pass with its JSON-escaped value (a value containing a token is never expanded again).</summary>
    public static string RenderBody(string template, string to, string text, string? sender, string templateCode, string language)
    {
        ArgumentNullException.ThrowIfNull(template);
        return Token().Replace(template, match => JsonEncodedText.Encode(match.Groups["name"].Value switch
        {
            "to" => to,
            "text" => text,
            "sender" => sender ?? string.Empty,
            "template" => templateCode,
            _ => language,
        }).ToString());
    }

    /// <summary>True when the template renders to valid JSON and carries both <c>{to}</c> and <c>{text}</c> (ConfigGuard).</summary>
    public static bool IsValidTemplate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template) || !template.Contains("{to}", StringComparison.Ordinal) || !template.Contains("{text}", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var _ = JsonDocument.Parse(RenderBody(template, "+201000000000", "Sample \"text\" {to}\n", "LIS", "otp", "en"));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public void Dispose() => _http.Dispose();

    [GeneratedRegex(@"\{(?<name>to|text|sender|template|language)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Token();
}
