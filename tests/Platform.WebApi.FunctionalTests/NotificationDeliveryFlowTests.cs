using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Platform.Contracts.Common;
using Platform.Contracts.Identity;
using Platform.Contracts.PlatformAdmin;
using Platform.Infrastructure.Notifications;
using Platform.SharedKernel.MultiTenancy;
using Platform.WebApi.FunctionalTests.Support;
using Platform.TestSupport;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// The flows the validation campaign LIS-VR-002 (§7) could not finish without an SMS or e-mail gateway, completed
/// through the production sender with <c>Lis:Notifications:PickupDirectory</c>: the code is read from the pickup
/// file the way a tester reads it on a host without a gateway. Password recovery (CP-03), the public subscription
/// wizard (CP-02) and the inactive-template fallback (SA-20, TPL-07).
/// </summary>
public sealed partial class NotificationDeliveryFlowTests : IClassFixture<ApiFactory>
{
    private const string RecoveryStartPath = "/api/v1/auth/recovery/start";
    private const string RecoveryCompletePath = "/api/v1/auth/recovery/complete";
    private const string Templates = "/api/v1/platform/notification-templates";
    private const string Subscriptions = "/api/v1/public/subscriptions";
    private static readonly string PickupDirectory = Path.Combine(Path.GetTempPath(), "lis-functional-pickup-" + Guid.CreateVersion7().ToString("N"));

    private static readonly string[] AdminPermissions =
    [
        "platform.view", "platform.manage", "tenancy.view", "tenancy.create", "tenancy.edit", "tenancy.manage", "notifications.view", "notifications.manage",
    ];

    private readonly ApiFactory _factory;

    public NotificationDeliveryFlowTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Overrides[NotificationOptions.PickupDirectoryKey] = PickupDirectory;   // the real sender, not the recording one
    }

    [DatabaseFact]
    public async Task Password_recovery_completes_with_the_code_read_from_the_pickup_directory()
    {
        var mobile = NewGlobalMobile();
        var (username, _, _) = await _factory.SeedUserAsync(mobile: mobile);
        var auth = new AuthClient(_factory.CreateClient());

        var message = await ReadNewMessageAsync(mobile, async () =>
            Assert.Equal(HttpStatusCode.Accepted, (await auth.Http.PostAsJsonAsync(RecoveryStartPath, new StartPasswordRecoveryRequest(mobile, "ar"))).StatusCode));

        Assert.Equal(("Sms", mobile, "password-recovery", "ar"), (message.Header("Channel"), message.Header("To"), message.Header("Template"), message.Header("Language")));
        Assert.Equal("استعادة كلمة المرور", message.Header("Subject"));
        Assert.StartsWith("استخدم الرمز ", message.Body, StringComparison.Ordinal);   // the built-in Arabic text, rendered
        Assert.DoesNotContain("{code}", message.Body, StringComparison.Ordinal);

        const string newPassword = "Picked-Up-Password-2026!";
        var completed = await CompleteRecoveryAsync(auth, username, message.Code, newPassword);
        Assert.Equal(HttpStatusCode.NoContent, completed.StatusCode);

        await new AuthClient(_factory.CreateClient()).SignInExpectingSuccessAsync(username, newPassword);
    }

    [DatabaseFact]
    public async Task A_laboratory_completes_the_subscription_wizard_with_the_code_read_from_the_pickup_directory()
    {
        var admin = await SignedInClientAsync((await _factory.SeedUserAsync(tenantId: TenantIds.Platform, permissions: AdminPermissions, displayName: "Pickup Admin")).Username);
        var (countryId, governorateId, _) = await CreatePlaceAsync(admin);
        var anonymous = _factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Requested-With", "Platform");

        var local = "011" + Random.Shared.Next(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture);
        var e164 = "+20" + local[1..];
        SubscriptionStarted? started = null;
        var otp = await ReadNewMessageAsync(e164, async () =>
        {
            var response = await anonymous.PostAsJsonAsync($"{Subscriptions}/start", new StartSubscriptionRequest("Pickup Lab", "معمل الالتقاط", countryId, governorateId, "5 Nile St.", "01012345678", "LIC-9", "Dr. Amal", local, "owner@pickup.example", "en"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            started = await response.Content.ReadFromJsonAsync<SubscriptionStarted>();
        });
        Assert.Equal("otp", otp.Header("Template"));
        Assert.Equal($"Your Sky LIS verification code is {otp.Code}. It is valid for 5 minutes.", otp.Body);

        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync($"{Subscriptions}/verify", new VerifySubscriptionRequest(started!.SubscriptionId, otp.Code))).StatusCode);

        var username = "pickup.owner." + Guid.CreateVersion7().ToString("N")[^8..];
        SubscriptionCompletedResponse? result = null;
        var portal = await ReadNewMessageAsync(e164, async () =>
        {
            var path = $"{Subscriptions}/complete";
            var challenge = await new AuthClient(anonymous).ChallengeAsync();
            var completed = await anonymous.PostAsJsonAsync(path, new { subscriptionId = started.SubscriptionId, username, password = TestSealer.Seal(challenge, "password", "a long enough password", "POST " + path) });
            Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
            result = await completed.Content.ReadFromJsonAsync<SubscriptionCompletedResponse>();
        });
        Assert.Equal("portal-url", portal.Header("Template"));
        Assert.Contains(result!.PortalUrl, portal.Body, StringComparison.Ordinal);
        Assert.Contains(username, portal.Body, StringComparison.Ordinal);

        await new AuthClient(_factory.CreateClient()).SignInExpectingSuccessAsync(username, "a long enough password", result.TenantId);
    }

    [DatabaseFact]
    public async Task An_active_template_is_sent_and_an_inactive_one_falls_back_to_the_built_in_text()
    {
        var admin = await SignedInClientAsync((await _factory.SeedUserAsync(tenantId: TenantIds.Platform, permissions: AdminPermissions, displayName: "Template Admin")).Username);
        var created = await admin.PostAsJsonAsync(Templates, new NotificationTemplateRequest("password-recovery", "Sms", "TPL-07 custom recovery code {code}, {minutes} minutes.", "رمز مخصص {code} لمدة {minutes} دقائق.", null, null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var templateId = (await created.Content.ReadFromJsonAsync<CreatedResource>())!.Id;

        var mobile = NewGlobalMobile();
        var (username, _, _) = await _factory.SeedUserAsync(mobile: mobile);
        var auth = new AuthClient(_factory.CreateClient());
        async Task StartAsync() => Assert.Equal(HttpStatusCode.Accepted, (await auth.Http.PostAsJsonAsync(RecoveryStartPath, new StartPasswordRecoveryRequest(mobile, "en"))).StatusCode);

        var custom = await ReadNewMessageAsync(mobile, StartAsync);
        Assert.Equal($"TPL-07 custom recovery code {custom.Code}, 5 minutes.", custom.Body);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Templates}/{templateId}/deactivate", null)).StatusCode);

        var fallback = await ReadNewMessageAsync(mobile, StartAsync);
        Assert.Equal($"Use {fallback.Code} to reset your Sky LIS password. The code expires in 5 minutes. Ignore this message if you did not request it.", fallback.Body);
        Assert.Equal("Password recovery", fallback.Header("Subject"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await CompleteRecoveryAsync(auth, username, custom.Code, "Fallback-Password-2026!")).StatusCode);   // superseded
        Assert.Equal(HttpStatusCode.NoContent, (await CompleteRecoveryAsync(auth, username, fallback.Code, "Fallback-Password-2026!")).StatusCode);
    }

    // ----------------------------------------------------------------------------------------------------------- pickup reading

    private sealed record PickedUpMessage(string FileName, IReadOnlyDictionary<string, string> Headers, string Body)
    {
        public string Header(string name) => Headers[name];

        /// <summary>The one-time code in the body: the only run of six digits.</summary>
        public string Code => Assert.Single(SixDigits().Matches(Body)).Value;
    }

    /// <summary>Runs <paramref name="action"/> and returns the one new pickup file for <paramref name="recipient"/> (named by its hash).</summary>
    private static async Task<PickedUpMessage> ReadNewMessageAsync(string recipient, Func<Task> action)
    {
        Directory.CreateDirectory(PickupDirectory);
        var pattern = $"*-{PickupDirectoryTransport.RecipientHash(recipient)}*{PickupDirectoryTransport.Extension}";
        var before = Directory.GetFiles(PickupDirectory, pattern).ToHashSet(StringComparer.OrdinalIgnoreCase);
        await action();
        var path = Assert.Single(Directory.GetFiles(PickupDirectory, pattern), file => !before.Contains(file));

        var text = await File.ReadAllTextAsync(path, Encoding.UTF8);
        var separator = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var headers = text[..separator].Split("\r\n").Select(line => line.Split(": ", 2)).ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
        return new PickedUpMessage(Path.GetFileName(path), headers, text[(separator + 4)..].TrimEnd('\r', '\n'));
    }

    // ----------------------------------------------------------------------------------------------------------- helpers

    private static string NewGlobalMobile() => "+2010" + Random.Shared.NextInt64(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture);

    private static async Task<HttpResponseMessage> CompleteRecoveryAsync(AuthClient auth, string identifier, string code, string newPassword)
    {
        var sealedPassword = TestSealer.Seal(await auth.ChallengeAsync(), "newPassword", newPassword, "POST " + RecoveryCompletePath);
        return await auth.Http.PostAsJsonAsync(RecoveryCompletePath, new { usernameOrMobile = identifier, code, newPassword = sealedPassword });
    }

    /// <summary>A country with a free two-letter code ("K?" and "Z?" are used by no other test), a governorate and a plan.</summary>
    private static async Task<(Guid Country, Guid Governorate, Guid Plan)> CreatePlaceAsync(HttpClient admin)
    {
        Guid? countryId = null;
        for (var attempt = 0; attempt < 52 && countryId is null; attempt++)
        {
            var iso = TestCountryCodes.Next();
            var created = await admin.PostAsJsonAsync("/api/v1/platform/countries", new CountryRequest("Pickupland " + iso, "أرض الالتقاط", iso, "+20", "Pound", "EGP", "ar", "Africa/Cairo", 10, null, null));
            if (created.StatusCode == HttpStatusCode.Created)
            {
                countryId = (await created.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
            }
        }

        Assert.NotNull(countryId);
        var governorate = await admin.PostAsJsonAsync("/api/v1/platform/governorates", new GovernorateRequest(countryId.Value, "Giza", "الجيزة", null, null));
        Assert.Equal(HttpStatusCode.Created, governorate.StatusCode);
        var planCode = "P" + Guid.CreateVersion7().ToString("N")[^8..].ToUpperInvariant();
        var plan = await admin.PostAsJsonAsync("/api/v1/platform/plans", new PlanRequest(planCode, "Pickup plan", "باقة", 10m, "EGP", 1, 5, 100, 1));
        Assert.Equal(HttpStatusCode.Created, plan.StatusCode);
        return (countryId.Value, (await governorate.Content.ReadFromJsonAsync<CreatedResource>())!.Id, (await plan.Content.ReadFromJsonAsync<CreatedResource>())!.Id);
    }

    private async Task<HttpClient> SignedInClientAsync(string username)
    {
        var client = _factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }

    [GeneratedRegex(@"(?<!\d)\d{6}(?!\d)")]
    private static partial Regex SixDigits();
}
