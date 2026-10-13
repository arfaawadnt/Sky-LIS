using Platform.Infrastructure.Notifications;
using Platform.Infrastructure.Security;

namespace Platform.Application.UnitTests.Notifications;

/// <summary>
/// <see cref="ConfigGuard.InspectNotifications"/>: the transport rules hold in every environment; in Production the
/// pickup directory lies under the reserved data root E:\lis-data (CLAUDE.md §2), where the installer restricts it.
/// </summary>
public sealed class NotificationConfigGuardTests
{
    [Fact]
    public void Nothing_configured_and_the_reserved_folders_pass()
    {
        Assert.Empty(ConfigGuard.InspectNotifications(new NotificationOptions(), production: true));
        Assert.Empty(ConfigGuard.InspectNotifications(new NotificationOptions { PickupDirectory = @"E:\lis-data\mail-pickup" }, production: true));
        Assert.Empty(ConfigGuard.InspectNotifications(new NotificationOptions { PickupDirectory = @"E:\LIS\.local\mail-pickup" }, production: false));
        Assert.Empty(ConfigGuard.InspectNotifications(new NotificationOptions
        {
            Smtp = new SmtpNotificationOptions { Host = "smtp.example.net", UserName = "lis", Password = "secret", From = "Sky LIS <no-reply@example.net>" },
            Sms = new SmsGatewayOptions { Url = "https://sms.example.net/send", ApiKey = "key" },
        }, production: true));
    }

    [Fact]
    public void The_pickup_directory_is_absolute_everywhere_and_under_the_data_root_in_production()
    {
        Assert.Contains(ConfigGuard.InspectNotifications(new NotificationOptions { PickupDirectory = @"mail-pickup" }, production: false), v => v.Contains(NotificationOptions.PickupDirectoryKey + " must be an absolute path", StringComparison.Ordinal));

        foreach (var outside in new[] { @"E:\LIS\.local\mail-pickup", @"C:\inetpub\wwwroot\lis-ui\mail", @"E:\lis-data", @"E:\lis-data\..\elsewhere", @"E:\lis-database\mail" })
        {
            var violations = ConfigGuard.InspectNotifications(new NotificationOptions { PickupDirectory = outside }, production: true);
            Assert.True(violations.Any(v => v.Contains(@"under the LIS data root E:\lis-data", StringComparison.Ordinal)), outside);
        }

        Assert.Empty(ConfigGuard.InspectNotifications(new NotificationOptions { PickupDirectory = @"E:\LIS\.local\mail-pickup" }, production: false));
        Assert.Throws<SecurityConfigurationException>(() => ConfigGuard.EnforceNotifications(new NotificationOptions { PickupDirectory = @"E:\LIS\.local\mail-pickup" }, production: true));
    }

    [Fact]
    public void Smtp_needs_a_sender_tls_off_loopback_and_a_password_with_a_user()
    {
        var violations = ConfigGuard.InspectNotifications(new NotificationOptions
        {
            Smtp = new SmtpNotificationOptions { Host = "smtp.example.net", Port = 0, EnableSsl = false, UserName = "lis", From = "not an address", Timeout = TimeSpan.Zero },
        }, production: false);

        Assert.Contains(violations, v => v.StartsWith(NotificationOptions.SmtpPortKey, StringComparison.Ordinal));
        Assert.Contains(violations, v => v.StartsWith(NotificationOptions.SmtpFromKey, StringComparison.Ordinal));
        Assert.Contains(violations, v => v.StartsWith(NotificationOptions.SmtpEnableSslKey, StringComparison.Ordinal));
        Assert.Contains(violations, v => v.StartsWith(NotificationOptions.SmtpPasswordKey, StringComparison.Ordinal));
        Assert.Contains(violations, v => v.StartsWith(NotificationOptions.SmtpTimeoutKey, StringComparison.Ordinal));

        foreach (var loopback in new[] { "localhost", "127.0.0.1", "::1", "[::1]" })
        {
            Assert.Empty(ConfigGuard.InspectNotifications(new NotificationOptions { Smtp = new SmtpNotificationOptions { Host = loopback, EnableSsl = false, From = "a@b.example" } }, production: true));
        }
    }

    [Fact]
    public void The_sms_gateway_needs_https_a_key_a_header_name_and_a_json_template()
    {
        var violations = ConfigGuard.InspectNotifications(new NotificationOptions
        {
            Sms = new SmsGatewayOptions { Url = "http://sms.example.net/send", ApiKeyHeader = "X Api Key", BodyTemplate = "to={to} text={text}", Timeout = TimeSpan.FromMinutes(10) },
        }, production: false);

        Assert.Contains(violations, v => v.StartsWith(NotificationOptions.SmsUrlKey, StringComparison.Ordinal));
        Assert.Contains(violations, v => v.StartsWith(NotificationOptions.SmsApiKeyKey, StringComparison.Ordinal));
        Assert.Contains(violations, v => v.StartsWith(NotificationOptions.SmsApiKeyHeaderKey, StringComparison.Ordinal));
        Assert.Contains(violations, v => v.StartsWith(NotificationOptions.SmsBodyTemplateKey, StringComparison.Ordinal));
        Assert.Contains(violations, v => v.StartsWith(NotificationOptions.SmsTimeoutKey, StringComparison.Ordinal));

        Assert.Empty(ConfigGuard.InspectNotifications(new NotificationOptions { Sms = new SmsGatewayOptions { Url = "http://127.0.0.1:5199/sms", ApiKey = "k" } }, production: true));
    }

    [Fact]
    public void The_transport_rules_do_not_depend_on_the_environment()
    {
        var options = new NotificationOptions { Smtp = new SmtpNotificationOptions { Host = "smtp.example.net", EnableSsl = false, From = "a@b.example" } };
        Assert.Equal(ConfigGuard.InspectNotifications(options, production: false), ConfigGuard.InspectNotifications(options, production: true));
    }
}
