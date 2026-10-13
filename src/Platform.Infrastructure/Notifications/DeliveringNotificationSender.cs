using System.Globalization;
using Microsoft.Extensions.Logging;
using Platform.Application.Abstractions.Notifications;

namespace Platform.Infrastructure.Notifications;

/// <summary>
/// The sender when <c>Lis:Notifications</c> configures a delivery: renders the template (subject and body) through
/// <see cref="INotificationTemplateSource"/> — so an inactive SA-20 template falls back to the built-in text exactly
/// as for the logging sender (TPL-07) — and hands the result to the transport of the channel. A channel without a
/// transport is logged only, like <see cref="LoggingNotificationSender"/>.
/// <para>
/// A delivery failure is logged at Error (masked recipient, never the body or a code) and absorbed: the callers send
/// after their own commit (provisioning, recovery, the wizard), and a gateway outage must neither undo that work nor
/// answer differently for an existing account (recovery start always answers 202). The person asks for a new code.
/// </para>
/// </summary>
internal sealed partial class DeliveringNotificationSender(
    INotificationTemplateSource templates,
    NotificationTransports transports,
    ILogger<DeliveringNotificationSender> logger) : INotificationSender
{
    public async Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var recipient = LoggingNotificationSender.Mask(message.To);
        var template = await templates.FindAsync(message.TemplateCode, message.Language, cancellationToken).ConfigureAwait(false);
        if (template is null)
        {
            LogTemplateMissing(logger, message.Channel, message.TemplateCode, message.Language, recipient);
            return;
        }

        var rendered = Render(message, template);
        var transport = transports.For(message.Channel);
        if (transport is null)
        {
            LogNotDelivered(logger, message.Channel, message.TemplateCode, template.Language, recipient, rendered.Body.Length);
            return;
        }

        try
        {
            await transport.DeliverAsync(rendered, cancellationToken).ConfigureAwait(false);
            LogDelivered(logger, message.Channel, transport.Name, message.TemplateCode, template.Language, recipient, rendered.Body.Length);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailed(logger, message.Channel, transport.Name, message.TemplateCode, recipient, Describe(exception));
        }
    }

    /// <summary>Body and subject with every placeholder substituted (the subject may carry <c>{labName}</c> too).</summary>
    public static RenderedNotification Render(NotificationMessage message, NotificationTemplate template)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(template);
        var subject = template.Subject is null ? null : (template with { Body = template.Subject }).Render(message.Placeholders);
        return new RenderedNotification(message.Channel, message.To, message.TemplateCode, template.Language, subject, template.Render(message.Placeholders));
    }

    [LoggerMessage(EventId = 7201, Level = LogLevel.Information, Message = "notification {Channel} delivered via {Transport} template={TemplateCode} lang={Language} to={Recipient} bodyLength={BodyLength}")]
    private static partial void LogDelivered(ILogger logger, NotificationChannel channel, string transport, string templateCode, string language, string recipient, int bodyLength);

    [LoggerMessage(EventId = 7202, Level = LogLevel.Information, Message = "notification {Channel} template={TemplateCode} lang={Language} to={Recipient} bodyLength={BodyLength} not delivered: no transport is configured for the channel")]
    private static partial void LogNotDelivered(ILogger logger, NotificationChannel channel, string templateCode, string language, string recipient, int bodyLength);

    [LoggerMessage(EventId = 7203, Level = LogLevel.Warning, Message = "notification {Channel} template={TemplateCode} lang={Language} to={Recipient} not delivered: no template")]
    private static partial void LogTemplateMissing(ILogger logger, NotificationChannel channel, string templateCode, string language, string recipient);

    /// <summary>The kind of failure without the server's text, which may quote the recipient.</summary>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            System.Net.Mail.SmtpException smtp => string.Create(CultureInfo.InvariantCulture, $"{smtp.GetType().Name} {smtp.StatusCode}"),
            HttpRequestException { StatusCode: { } status } http => string.Create(CultureInfo.InvariantCulture, $"{http.GetType().Name} {(int)status}"),
            _ => exception.GetType().Name,
        };
    }

    [LoggerMessage(EventId = 7204, Level = LogLevel.Error, Message = "notification {Channel} via {Transport} template={TemplateCode} to={Recipient} failed: {Reason}")]
    private static partial void LogFailed(ILogger logger, NotificationChannel channel, string transport, string templateCode, string recipient, string reason);
}
