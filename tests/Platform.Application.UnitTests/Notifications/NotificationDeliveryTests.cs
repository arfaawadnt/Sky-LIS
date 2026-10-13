using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Application.Abstractions.Notifications;
using Platform.Application.UnitTests.Support;
using Platform.Infrastructure;
using Platform.Infrastructure.Notifications;

namespace Platform.Application.UnitTests.Notifications;

/// <summary>
/// The delivery adapters of <c>Lis:Notifications</c> (validation LIS-VR-002 §7: no gateway on the host blocked the
/// wizard, recovery and the inactive-template fallback): the pickup file, SMTP against a loopback fake server, the
/// HTTP SMS gateway through a fake handler, the routing per channel and the default when nothing is configured.
/// </summary>
public sealed partial class NotificationDeliveryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "lis-unit-pickup-" + Guid.CreateVersion7().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static RenderedNotification Recovery(string to = "+201012345678") =>
        new(NotificationChannel.Sms, to, "password-recovery", "ar", "استعادة كلمة المرور", "استخدم الرمز 123456 لإعادة تعيين كلمة مرور Sky LIS.");

    // ------------------------------------------------------------------------------------------------------- pickup

    [Fact]
    public async Task The_pickup_file_holds_recipient_channel_template_subject_and_body_under_a_stamped_hashed_name()
    {
        var clock = new FixedClock(FixedClock.Instant);
        var transport = new PickupDirectoryTransport(_folder, clock);

        var name = await transport.WriteAsync(Recovery(), CancellationToken.None);

        var hash = PickupDirectoryTransport.RecipientHash("+201012345678");
        Assert.Equal($"20261009T1200000000000Z-sms-{hash}.txt", name);
        Assert.Matches("^[0-9a-f]{12}$", hash);
        Assert.Equal(hash, PickupDirectoryTransport.RecipientHash("  +201012345678 "));   // trimmed, case-folded
        Assert.DoesNotContain("201012345678", name, StringComparison.Ordinal);            // the number itself is not in the name

        var text = await File.ReadAllTextAsync(Path.Combine(_folder, name), Encoding.UTF8);
        Assert.Equal(
            "Channel: Sms\r\nTo: +201012345678\r\nTemplate: password-recovery\r\nLanguage: ar\r\nSubject: استعادة كلمة المرور\r\n"
            + "Created-Utc: 2026-10-09T12:00:00.0000000Z\r\n\r\nاستخدم الرمز 123456 لإعادة تعيين كلمة مرور Sky LIS.\r\n",
            text);
        Assert.Single(Directory.GetFiles(_folder));   // no temporary file left behind
    }

    [Fact]
    public async Task Two_messages_to_one_recipient_in_the_same_instant_get_two_files()
    {
        var transport = new PickupDirectoryTransport(_folder, new FixedClock(FixedClock.Instant));

        var first = await transport.WriteAsync(Recovery(), CancellationToken.None);
        var second = await transport.WriteAsync(Recovery() with { Body = "second" }, CancellationToken.None);

        Assert.NotEqual(first, second);
        Assert.EndsWith("-2.txt", second, StringComparison.Ordinal);
        Assert.Equal(2, Directory.GetFiles(_folder, "*.txt").Length);
        Assert.Empty(Directory.GetFiles(_folder, "*.partial"));
    }

    [Fact]
    public void Header_values_stay_on_one_line()
    {
        var text = PickupDirectoryTransport.Format(Recovery() with { Subject = "a\r\nInjected: yes" }, FixedClock.Instant);
        Assert.Contains("Subject: a  Injected: yes\r\n", text, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------------- SMTP

    [Fact]
    public async Task Smtp_delivers_the_rendered_message_to_the_relay()
    {
        await using var server = await FakeSmtpServer.StartAsync();
        var transport = new SmtpEmailTransport(new SmtpNotificationOptions
        {
            Host = "127.0.0.1",
            Port = server.Port,
            EnableSsl = false,   // loopback relay only (ConfigGuard)
            From = "Sky LIS <no-reply@lis.example.net>",
        });

        await transport.DeliverAsync(new RenderedNotification(NotificationChannel.Email, "owner@shifa.example", "otp", "ar", "Your verification code", "رمز التحقق الخاص بك هو 654321."), CancellationToken.None);

        var session = await server.SessionAsync();
        Assert.Contains("MAIL FROM:<no-reply@lis.example.net>", session.Commands);
        Assert.Contains("RCPT TO:<owner@shifa.example>", session.Commands);
        Assert.Equal("Your verification code", session.Subject);
        Assert.Equal("رمز التحقق الخاص بك هو 654321.", session.Body);
    }

    [Fact]
    public async Task Without_a_subject_the_mail_carries_the_product_name()
    {
        await using var server = await FakeSmtpServer.StartAsync();
        var transport = new SmtpEmailTransport(new SmtpNotificationOptions { Host = "localhost", Port = server.Port, EnableSsl = false, From = "no-reply@lis.example.net" });

        await transport.DeliverAsync(new RenderedNotification(NotificationChannel.Email, "a@b.example", "panic-value", "en", null, "Panic value."), CancellationToken.None);

        Assert.Equal(SmtpEmailTransport.FallbackSubject, (await server.SessionAsync()).Subject);
    }

    // ------------------------------------------------------------------------------------------------------- HTTP SMS

    [Fact]
    public async Task The_sms_gateway_receives_one_json_post_with_the_api_key_header()
    {
        var handler = new RecordingHandler(HttpStatusCode.Accepted);
        using var transport = new HttpSmsTransport(new SmsGatewayOptions
        {
            Url = "https://sms.example.net/v1/messages",
            ApiKeyHeader = "X-Gateway-Key",
            ApiKey = "test-key",
            Sender = "SkyLIS",
            BodyTemplate = """{"recipient":"{to}","message":"{text}","from":"{sender}","meta":{"template":"{template}","lang":"{language}"}}""",
        }, handler);

        await transport.DeliverAsync(Recovery() with { Body = "Use \"123456\" {to}\nnow — استخدم" }, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://sms.example.net/v1/messages", request.Uri);
        Assert.Equal("test-key", request.ApiKey);
        Assert.Equal("application/json", request.ContentType);
        using var json = JsonDocument.Parse(request.Body);
        Assert.Equal("+201012345678", json.RootElement.GetProperty("recipient").GetString());
        Assert.Equal("Use \"123456\" {to}\nnow — استخدم", json.RootElement.GetProperty("message").GetString());   // escaped once, never re-expanded
        Assert.Equal("SkyLIS", json.RootElement.GetProperty("from").GetString());
        Assert.Equal("password-recovery", json.RootElement.GetProperty("meta").GetProperty("template").GetString());
        Assert.Equal("ar", json.RootElement.GetProperty("meta").GetProperty("lang").GetString());
    }

    [Fact]
    public async Task A_refusal_of_the_gateway_is_an_error_carrying_the_status()
    {
        using var transport = new HttpSmsTransport(new SmsGatewayOptions { Url = "https://sms.example.net/send", ApiKey = "k" }, new RecordingHandler(HttpStatusCode.TooManyRequests));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => transport.DeliverAsync(Recovery(), CancellationToken.None));

        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.Equal("HttpRequestException 429", DeliveringNotificationSender.Describe(error));
    }

    [Fact]
    public void The_default_body_template_is_valid_and_a_broken_one_is_not()
    {
        Assert.True(HttpSmsTransport.IsValidTemplate(SmsGatewayOptions.DefaultBodyTemplate));
        Assert.False(HttpSmsTransport.IsValidTemplate("""{"to":{to},"text":"{text}"}"""));   // token outside quotes
        Assert.False(HttpSmsTransport.IsValidTemplate("""{"to":"{to}"}"""));                 // no text
        Assert.False(HttpSmsTransport.IsValidTemplate(string.Empty));
    }

    // ------------------------------------------------------------------------------------------------------- routing

    [Fact]
    public async Task With_only_a_pickup_directory_both_channels_land_in_it_rendered_from_the_template()
    {
        using var transports = new NotificationTransports(new NotificationOptions { PickupDirectory = _folder }, new FixedClock(FixedClock.Instant));
        var sender = new DeliveringNotificationSender(new DefaultNotificationTemplateSource(), transports, NullLogger<DeliveringNotificationSender>.Instance);

        await sender.SendAsync(new NotificationMessage(NotificationChannel.Sms, "+201011112222", NotificationTemplateCodes.Otp, "en", Code("246810")), CancellationToken.None);
        await sender.SendAsync(new NotificationMessage(NotificationChannel.Email, "owner@shifa.example", NotificationTemplateCodes.PortalUrl, "en", new Dictionary<string, string> { ["labName"] = "Al Shifa", ["url"] = "https://shifa.skylis.app", ["username"] = "lab.owner" }), CancellationToken.None);

        var files = Directory.GetFiles(_folder).Select(path => File.ReadAllText(path, Encoding.UTF8)).ToList();
        Assert.Equal(2, files.Count);
        Assert.Contains(files, text => text.Contains("Channel: Sms", StringComparison.Ordinal) && text.Contains("Your Sky LIS verification code is 246810. It is valid for 5 minutes.", StringComparison.Ordinal));
        Assert.Contains(files, text => text.Contains("Channel: Email", StringComparison.Ordinal) && text.Contains("Subject: Your Sky LIS portal", StringComparison.Ordinal)
                                       && text.Contains("Al Shifa: your portal is ready at https://shifa.skylis.app. Sign in with the username lab.owner.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_gateway_takes_its_channel_the_other_channel_falls_back_to_the_pickup_and_a_failure_is_absorbed()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var options = new NotificationOptions { PickupDirectory = _folder, Sms = new SmsGatewayOptions { Url = "https://sms.example.net/send", ApiKey = "k" } };
        using var transports = new NotificationTransports(options, new FixedClock(FixedClock.Instant), handler);
        var sender = new DeliveringNotificationSender(new DefaultNotificationTemplateSource(), transports, NullLogger<DeliveringNotificationSender>.Instance);

        await sender.SendAsync(new NotificationMessage(NotificationChannel.Sms, "+201011112222", NotificationTemplateCodes.Otp, "ar", Code("135790")), CancellationToken.None);
        await sender.SendAsync(new NotificationMessage(NotificationChannel.Email, "owner@shifa.example", NotificationTemplateCodes.Otp, "en", Code("135790")), CancellationToken.None);

        Assert.Single(handler.Requests);
        Assert.Contains("135790", JsonDocument.Parse(handler.Requests[0].Body).RootElement.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(_folder));   // the e-mail

        handler.Status = HttpStatusCode.ServiceUnavailable;
        await sender.SendAsync(new NotificationMessage(NotificationChannel.Sms, "+201011112222", NotificationTemplateCodes.Otp, "ar", Code("000111")), CancellationToken.None);   // no throw
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task A_template_the_source_does_not_know_is_not_delivered()
    {
        using var transports = new NotificationTransports(new NotificationOptions { PickupDirectory = _folder }, new FixedClock(FixedClock.Instant));
        var sender = new DeliveringNotificationSender(new DefaultNotificationTemplateSource(), transports, NullLogger<DeliveringNotificationSender>.Instance);

        await sender.SendAsync(new NotificationMessage(NotificationChannel.Sms, "+201011112222", "no-such-template", "en", Code("1")), CancellationToken.None);

        Assert.False(Directory.Exists(_folder) && Directory.EnumerateFiles(_folder).Any());
    }

    [Fact]
    public void The_logging_sender_stays_the_default_and_a_configured_delivery_replaces_it()
    {
        Assert.IsType<LoggingNotificationSender>(Resolve(new NotificationOptions()));
        Assert.IsType<DeliveringNotificationSender>(Resolve(new NotificationOptions { PickupDirectory = _folder }));
        Assert.IsType<DeliveringNotificationSender>(Resolve(new NotificationOptions { Smtp = new SmtpNotificationOptions { Host = "smtp.example.net", From = "a@b.example" } }));
        Assert.False(new NotificationOptions { PickupDirectory = "  " }.HasDelivery);

        static INotificationSender Resolve(NotificationOptions options)
        {
            var services = new ServiceCollection();
            services.AddSingleton(options);
            services.AddInfrastructure("Host=127.0.0.1;Port=5433;Database=lis_unit;Username=lis_app");
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            return scope.ServiceProvider.GetRequiredService<INotificationSender>();
        }
    }

    private static Dictionary<string, string> Code(string code) => new(StringComparer.Ordinal) { ["code"] = code, ["minutes"] = "5" };

    // ------------------------------------------------------------------------------------------------------- fakes

    private sealed record RecordedRequest(HttpMethod Method, string Uri, string? ApiKey, string? ContentType, string Body);

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = status;

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = request.Headers.TryGetValues("X-Gateway-Key", out var custom) ? custom.Single()
                : request.Headers.TryGetValues("X-Api-Key", out var standard) ? standard.Single() : null;
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.ToString(), key, request.Content!.Headers.ContentType?.MediaType, await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(Status);
        }
    }

    private sealed record SmtpSession(IReadOnlyList<string> Commands, string Subject, string Body);

    /// <summary>A minimal SMTP relay on a loopback port: accepts one message and records the dialogue.</summary>
    private sealed partial class FakeSmtpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task<SmtpSession> _session;

        private FakeSmtpServer(TcpListener listener)
        {
            _listener = listener;
            _session = Task.Run(ServeAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public static Task<FakeSmtpServer> StartAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new FakeSmtpServer(listener));
        }

        public Task<SmtpSession> SessionAsync() => _session.WaitAsync(TimeSpan.FromSeconds(30));

        public ValueTask DisposeAsync()
        {
            _listener.Stop();
            return ValueTask.CompletedTask;
        }

        private async Task<SmtpSession> ServeAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            var commands = new List<string>();
            var data = new StringBuilder();
            await writer.WriteLineAsync("220 fake.lis.local ESMTP");
            while (await reader.ReadLineAsync() is { } line)
            {
                commands.Add(line);
                var verb = line.Split(' ', 2)[0].ToUpperInvariant();
                if (verb is "EHLO" or "HELO")
                {
                    await writer.WriteLineAsync("250 fake.lis.local");
                }
                else if (verb == "DATA")
                {
                    await writer.WriteLineAsync("354 end with <CRLF>.<CRLF>");
                    while (await reader.ReadLineAsync() is { } content && content != ".")
                    {
                        data.Append(content).Append('\n');
                    }

                    await writer.WriteLineAsync("250 queued");
                }
                else if (verb == "QUIT")
                {
                    await writer.WriteLineAsync("221 bye");
                    break;
                }
                else
                {
                    await writer.WriteLineAsync("250 OK");
                }
            }

            return Parse(commands, data.ToString());
        }

        private static SmtpSession Parse(List<string> commands, string data)
        {
            var parts = data.Split("\n\n", 2);
            var headers = Regex.Replace(parts[0], @"\n[ \t]+", " ");   // unfold
            var subjectLine = headers.Split('\n').Single(line => line.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase))["Subject:".Length..].Trim();
            var subject = EncodedWord().Replace(subjectLine, match => Encoding.UTF8.GetString(Convert.FromBase64String(match.Groups["b64"].Value))).Trim();   // RFC 2047: adjacent words join without the space
            var body = Encoding.UTF8.GetString(Convert.FromBase64String(string.Concat(parts[1].Split('\n', StringSplitOptions.RemoveEmptyEntries))));
            return new SmtpSession(commands, subject, body);
        }

        [GeneratedRegex(@"=\?utf-8\?B\?(?<b64>[^?]*)\?=\s*", RegexOptions.IgnoreCase)]
        private static partial Regex EncodedWord();
    }
}
