using Microsoft.Extensions.Logging;
using Platform.Application.Abstractions.Notifications;

namespace Platform.Infrastructure.Notifications;

/// <summary>
/// The in-code templates of the platform's own flows (SA-20 lists them; the platform module's editable templates
/// replace this source by convention). English and Arabic; a missing language falls back to English.
/// </summary>
public sealed class DefaultNotificationTemplateSource : INotificationTemplateSource
{
    private static readonly Dictionary<(string Code, string Language), NotificationTemplate> Templates = new[]
    {
        new NotificationTemplate(NotificationTemplateCodes.Otp, "en", "Your verification code", "Your Sky LIS verification code is {code}. It is valid for {minutes} minutes."),
        new NotificationTemplate(NotificationTemplateCodes.Otp, "ar", "رمز التحقق", "رمز التحقق الخاص بك في Sky LIS هو {code}. صالح لمدة {minutes} دقائق."),
        new NotificationTemplate(NotificationTemplateCodes.PasswordRecovery, "en", "Password recovery", "Use {code} to reset your Sky LIS password. The code expires in {minutes} minutes. Ignore this message if you did not request it."),
        new NotificationTemplate(NotificationTemplateCodes.PasswordRecovery, "ar", "استعادة كلمة المرور", "استخدم الرمز {code} لإعادة تعيين كلمة مرور Sky LIS. ينتهي الرمز خلال {minutes} دقائق. تجاهل هذه الرسالة إن لم تطلبها."),
        new NotificationTemplate(NotificationTemplateCodes.PortalUrl, "en", "Your Sky LIS portal", "{labName}: your portal is ready at {url}. Sign in with the username {username}."),
        new NotificationTemplate(NotificationTemplateCodes.PortalUrl, "ar", "بوابة Sky LIS الخاصة بك", "{labName}: بوابتك جاهزة على {url}. سجّل الدخول باسم المستخدم {username}."),
        new NotificationTemplate(NotificationTemplateCodes.PanicValue, "en", "Panic value", "Panic value for {patient} ({accession}): {test} = {value}."),
        new NotificationTemplate(NotificationTemplateCodes.PanicValue, "ar", "قيمة حرجة", "قيمة حرجة للمريض {patient} ({accession}): {test} = {value}."),

        // LIS-CR-012 (C-16): subscription reminders, sent by SMS and e-mail to the laboratory's manager
        new NotificationTemplate(NotificationTemplateCodes.SubscriptionExpiry, "en", "Your Sky LIS trial is ending", "{labName}: your Sky LIS trial ends in {days} day(s). Top up {amount} to keep working without interruption: {url}"),
        new NotificationTemplate(NotificationTemplateCodes.SubscriptionExpiry, "ar", "الفترة التجريبية لـ Sky LIS على وشك الانتهاء", "{labName}: تنتهي الفترة التجريبية في Sky LIS خلال {days} يوم. اشحن المحفظة بمبلغ {amount} لمواصلة العمل دون انقطاع: {url}"),
        new NotificationTemplate(NotificationTemplateCodes.WalletLow, "en", "Your Sky LIS wallet is running low", "{labName}: your Sky LIS wallet balance is low. Top up {amount} within {days} day(s) to avoid an interruption: {url}"),
        new NotificationTemplate(NotificationTemplateCodes.WalletLow, "ar", "رصيد محفظة Sky LIS منخفض", "{labName}: رصيد محفظة Sky LIS منخفض. اشحن {amount} خلال {days} يوم لتجنب انقطاع الخدمة: {url}"),
        new NotificationTemplate(NotificationTemplateCodes.SubscriptionGrace, "en", "Your Sky LIS subscription is about to stop", "{labName}: the grace period of your Sky LIS subscription ends in {days} day(s). After that only your administrators can sign in, to the wallet. Top up {amount}: {url}"),
        new NotificationTemplate(NotificationTemplateCodes.SubscriptionGrace, "ar", "اشتراك Sky LIS على وشك التوقف", "{labName}: تنتهي فترة السماح لاشتراك Sky LIS خلال {days} يوم. بعدها يستطيع المسؤولون فقط الدخول إلى المحفظة. اشحن {amount}: {url}"),
    }.ToDictionary(template => (template.Code, template.Language));

    public Task<NotificationTemplate?> FindAsync(string code, string language, CancellationToken cancellationToken)
    {
        var lang = language is not null && language.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";
        return Task.FromResult(Templates.GetValueOrDefault((code ?? string.Empty, lang)) ?? Templates.GetValueOrDefault((code ?? string.Empty, "en")));
    }
}

/// <summary>
/// The default sender: renders the template and writes one structured log line with the channel, template, language
/// and a masked recipient, never the body and never a secret placeholder. A gateway adapter (SMS, e-mail) replaces
/// it by convention; the functional test host replaces it with a recording sender.
/// </summary>
public sealed partial class LoggingNotificationSender(INotificationTemplateSource templates, ILogger<LoggingNotificationSender> logger) : INotificationSender
{
    public async Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var template = await templates.FindAsync(message.TemplateCode, message.Language, cancellationToken).ConfigureAwait(false);
        var rendered = template?.Render(message.Placeholders);   // rendered for parity with a real gateway; not logged
        LogSent(logger, message.Channel, message.TemplateCode, message.Language, Mask(message.To), rendered?.Length ?? 0, template is null);
    }

    /// <summary>Keeps the last three characters: enough to recognise a recipient in a log, not enough to contact them.</summary>
    public static string Mask(string recipient)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        return recipient.Length <= 3 ? "***" : new string('*', recipient.Length - 3) + recipient[^3..];
    }

    [LoggerMessage(EventId = 7200, Level = LogLevel.Information, Message = "notification {Channel} template={TemplateCode} lang={Language} to={Recipient} bodyLength={BodyLength} templateMissing={TemplateMissing}")]
    private static partial void LogSent(ILogger logger, NotificationChannel channel, string templateCode, string language, string recipient, int bodyLength, bool templateMissing);
}
