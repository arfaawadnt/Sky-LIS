using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;
using Platform.SharedKernel.MultiTenancy;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// Platform administration over the real API (intake LIS-INT-PLT-001 §16): the Super Admin pages from the platform
/// tenant, the PLT-403 fence for client tenants, the public subscription wizard end to end with the one-time code
/// and the credential envelope, wallets in both portals, sessions and the integration library import.
/// </summary>
public sealed class PlatformAdminFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Clients = "/api/v1/platform/clients";
    private const string Countries = "/api/v1/platform/countries";
    private const string Governorates = "/api/v1/platform/governorates";
    private const string Plans = "/api/v1/platform/plans";
    private const string Wallets = "/api/v1/platform/wallets";
    private const string Sessions = "/api/v1/platform/sessions";
    private const string Templates = "/api/v1/platform/notification-templates";
    private const string CodeSystems = "/api/v1/platform/library/code-systems";
    private const string Public = "/api/v1/public";
    private const string Subscriptions = "/api/v1/public/subscriptions";

    private static readonly string[] AdminPermissions =
    [
        "platform.view", "platform.manage", "tenancy.view", "tenancy.create", "tenancy.edit", "tenancy.manage",
        "notifications.view", "notifications.manage", "integration.view", "integration.manage",
    ];

    private static int _isoCounter;

    [DatabaseFact]
    public async Task A_platform_administrator_provisions_a_client_and_runs_its_life_cycle()
    {
        var admin = await AdminClientAsync();
        var place = await CreatePlaceAsync(admin);

        var slug = "noor-" + Guid.CreateVersion7().ToString("N")[^8..];
        var manager = NewMobile();
        var created = await ProvisionAsync(admin, place, slug, "noor.admin." + slug[^8..], manager);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var provisioned = (await created.Content.ReadFromJsonAsync<ProvisionedClient>())!;
        Assert.Equal($"https://{slug}.skylis.app", provisioned.PortalUrl);
        Assert.Equal("portal-url", factory.Notifications.LastTo(Global(manager))!.TemplateCode);

        var taken = await ProvisionAsync(admin, place, slug, "other.admin." + slug[^8..], NewMobile());
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal("CLI-100", await AuthClient.CodeOf(taken));

        var badMobile = await ProvisionAsync(admin, place, slug + "-x", "third.admin." + slug[^8..], NewMobile(), mobile: "0101234");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badMobile.StatusCode);
        Assert.Equal("GEO-010", await AuthClient.CodeOf(badMobile));

        var page = await admin.GetFromJsonAsync<Paged<ClientListItem>>($"{Clients}?search={slug}&source=Manual");
        var item = Assert.Single(page!.Items);
        Assert.Equal(("Active", "Manual", Global(manager)), (item.Status, item.Source, item.ManagerMobile));

        var noReason = await admin.PostAsJsonAsync($"{Clients}/{provisioned.Id}/deactivate", new DeactivateClientRequest(""));
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"{Clients}/{provisioned.Id}/deactivate", new DeactivateClientRequest("unpaid"))).StatusCode);
        Assert.Equal("Inactive", (await admin.GetFromJsonAsync<ClientDetail>($"{Clients}/{provisioned.Id}"))!.Status);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Clients}/{provisioned.Id}/activate", null)).StatusCode);
        var twice = await admin.PostAsync($"{Clients}/{provisioned.Id}/activate", null);
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal("CLI-010", await AuthClient.CodeOf(twice));
    }

    [DatabaseFact]
    public async Task Laboratory_users_never_reach_platform_administration()
    {
        // The authorization behaviour decides first (D-04): tenancy.* is platform-only, so a laboratory principal never holds it.
        var (labAdmin, labTenant, _) = await factory.SeedUserAsync(tenantId: Guid.CreateVersion7(), permissions: ["tenancy.view", "tenancy.manage", "integration.view", "notifications.view"]);
        var lab = await SignedInClientAsync(labAdmin);
        var clients = await lab.GetAsync(Clients);
        Assert.Equal(HttpStatusCode.Forbidden, clients.StatusCode);
        Assert.Equal(ProblemCodes.Forbidden, await AuthClient.CodeOf(clients));

        // integration.view and notifications.view are laboratory keys (CP-58…CP-62): the handler's platform-tenant check refuses them (INV-PLT-070).
        foreach (var path in new[] { "/api/v1/platform/library/analyser-models", Templates })
        {
            var refused = await lab.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal("PLT-403", await AuthClient.CodeOf(refused));
        }

        Assert.NotEqual(TenantIds.Platform, labTenant);

        var (outsider, _, _) = await factory.SeedUserAsync(tenantId: TenantIds.Platform, permissions: null);
        var noRole = await (await SignedInClientAsync(outsider)).GetAsync(Clients);
        Assert.Equal(HttpStatusCode.Forbidden, noRole.StatusCode);
        Assert.Equal(ProblemCodes.Forbidden, await AuthClient.CodeOf(noRole));
    }

    [DatabaseFact]
    public async Task A_laboratory_subscribes_through_the_public_wizard()
    {
        var admin = await AdminClientAsync();
        var place = await CreatePlaceAsync(admin);
        var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Requested-With", "Platform");

        var countries = await anonymous.GetFromJsonAsync<List<PublicCountry>>($"{Public}/countries");
        Assert.Contains(countries!, country => country.Id == place.CountryId && country.MobileLength == 10);
        var governorates = await anonymous.GetFromJsonAsync<List<PublicGovernorate>>($"{Public}/governorates?countryId={place.CountryId}");
        Assert.Contains(governorates!, governorate => governorate.Id == place.GovernorateId);

        var managerMobile = NewMobile();
        var wrongLength = await anonymous.PostAsJsonAsync($"{Subscriptions}/start", Start(place, "012345"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongLength.StatusCode);
        Assert.Equal("GEO-010", await AuthClient.CodeOf(wrongLength));

        var started = (await (await anonymous.PostAsJsonAsync($"{Subscriptions}/start", Start(place, managerMobile))).Content.ReadFromJsonAsync<SubscriptionStarted>())!;
        var e164 = Global(managerMobile);
        Assert.EndsWith(managerMobile[^3..], started.MaskedMobile, StringComparison.Ordinal);
        var code = factory.Notifications.LastTo(e164)!.Placeholders["code"];

        var username = "lab.owner." + Guid.CreateVersion7().ToString("N")[^8..];
        var early = await CompleteAsync(anonymous, started.SubscriptionId, username, "a long enough password");
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal("SUB-003", await AuthClient.CodeOf(early));

        var wrong = await anonymous.PostAsJsonAsync($"{Subscriptions}/verify", new VerifySubscriptionRequest(started.SubscriptionId, code == "000000" ? "111111" : "000000"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);
        Assert.Equal("SUB-010", await AuthClient.CodeOf(wrong));

        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync($"{Subscriptions}/verify", new VerifySubscriptionRequest(started.SubscriptionId, code))).StatusCode);

        var containsUsername = await CompleteAsync(anonymous, started.SubscriptionId, username, "x" + username + "x");
        Assert.Equal("CLI-041", await AuthClient.CodeOf(containsUsername));

        var completed = await CompleteAsync(anonymous, started.SubscriptionId, username, "a long enough password");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var result = (await completed.Content.ReadFromJsonAsync<SubscriptionCompletedResponse>())!;
        Assert.StartsWith("https://", result.PortalUrl, StringComparison.Ordinal);
        Assert.Equal("portal-url", factory.Notifications.LastTo(e164)!.TemplateCode);

        var availability = await anonymous.GetFromJsonAsync<UsernameAvailability>($"{Subscriptions}/check-username?username={username}");
        Assert.False(availability!.Available);
        var reuse = await CompleteAsync(anonymous, started.SubscriptionId, username + "2", "a long enough password");
        Assert.Equal("SUB-003", await AuthClient.CodeOf(reuse));

        var detail = await admin.GetFromJsonAsync<ClientDetail>($"{Clients}/{result.TenantId}");
        Assert.Equal(("Trial", "Subscription", username), (detail!.Status, detail.Source, detail.AdministratorUsername));

        var plaintext = await anonymous.PostAsJsonAsync($"{Subscriptions}/complete", new { subscriptionId = started.SubscriptionId, username, password = "plain text password" });
        Assert.Equal(HttpStatusCode.BadRequest, plaintext.StatusCode);
        Assert.Equal(ProblemCodes.CredentialPlaintext, await AuthClient.CodeOf(plaintext));
    }

    [DatabaseFact]
    public async Task Wallets_are_topped_up_by_the_platform_and_requested_by_the_laboratory()
    {
        var admin = await AdminClientAsync();
        var place = await CreatePlaceAsync(admin);
        var slug = "wallet-" + Guid.CreateVersion7().ToString("N")[^8..];
        var provisioned = (await (await ProvisionAsync(admin, place, slug, "wallet.admin." + slug[^8..], NewMobile())).Content.ReadFromJsonAsync<ProvisionedClient>())!;

        var zero = await admin.PostAsJsonAsync($"{Wallets}/{provisioned.Id}/top-up", new TopUpWalletRequest(0m, "INV-0", null, null));
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);

        var (labUser, _, _) = await factory.SeedUserAsync(tenantId: provisioned.Id, permissions: ["wallet.view", "wallet.create"]);
        var lab = await SignedInClientAsync(labUser);
        var request = await lab.PostAsJsonAsync("/api/v1/wallet/top-up-requests", new CreateTopUpRequest(750m, "October"));
        Assert.Equal(HttpStatusCode.Created, request.StatusCode);
        var requestId = (await request.Content.ReadFromJsonAsync<CreatedResource>())!.Id;

        var pending = (await admin.GetFromJsonAsync<WalletDetail>($"{Wallets}/{provisioned.Id}"))!;
        Assert.Equal(requestId, Assert.Single(pending.PendingTopUpRequests).Id);

        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"{Wallets}/{provisioned.Id}/top-up", new TopUpWalletRequest(750m, "INV-750", null, requestId))).StatusCode);
        var mine = (await lab.GetFromJsonAsync<WalletDetail>("/api/v1/wallet"))!;
        Assert.Equal((750m, 0), (mine.Balance, mine.PendingTopUpRequests.Count));
        var history = (await lab.GetFromJsonAsync<Paged<WalletTransactionItem>>("/api/v1/wallet/transactions"))!;
        Assert.Equal(("TopUp", 750m), (history.Items[0].Kind, history.Items[0].BalanceAfter));

        var platformWallet = await admin.GetAsync("/api/v1/wallet");
        Assert.Equal(HttpStatusCode.Forbidden, platformWallet.StatusCode);   // the platform role holds no wallet.view
    }

    [DatabaseFact]
    public async Task Sessions_templates_and_the_code_import_follow_their_rules()
    {
        var admin = await AdminClientAsync();

        var sessions = (await admin.GetFromJsonAsync<List<AdminSessionItem>>(Sessions))!;
        var own = Assert.Single(sessions, session => session.IsCurrent);
        var self = await admin.PostAsync($"{Sessions}/{own.SessionId}/terminate", null);
        Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode);
        Assert.Equal("PLT-010", await AuthClient.CodeOf(self));

        var other = await AdminClientAsync();
        var otherSession = (await other.GetFromJsonAsync<List<AdminSessionItem>>(Sessions))!.Single(session => session.IsCurrent);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Sessions}/{otherSession.SessionId}/terminate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync(Sessions)).StatusCode);

        var unknownPlaceholder = await admin.PostAsJsonAsync(Templates, new NotificationTemplateRequest("welcome-" + Guid.CreateVersion7().ToString("N")[^6..], "Sms", "Hello {customer}", "مرحبا", null, null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknownPlaceholder.StatusCode);
        Assert.Equal("NTF-003", await AuthClient.CodeOf(unknownPlaceholder));

        var codeSystem = (await (await admin.PostAsJsonAsync(CodeSystems, new CodeSystemRequest("Loinc", "2.78-" + Guid.CreateVersion7().ToString("N")[^6..], "tests"))).Content.ReadFromJsonAsync<CreatedResource>())!;
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("code,display\n2345-7,Glucose\n718-7,Hemoglobin\n"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "loinc.csv");
        var imported = await admin.PostAsync($"{CodeSystems}/{codeSystem.Id}/import", form);
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        Assert.Equal(new CodeSystemImportResult(2, 0, 0, 2), await imported.Content.ReadFromJsonAsync<CodeSystemImportResult>());
        var items = (await admin.GetFromJsonAsync<Paged<CodeSystemItemView>>($"{CodeSystems}/{codeSystem.Id}/items?search=gluc"))!;
        Assert.Equal("2345-7", Assert.Single(items.Items).Code);
    }

    /// <summary>A fresh Egyptian mobile (011 + 8 digits): mobiles are unique among platform users.</summary>
    private static string NewMobile() => "011" + Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Global(string localMobile) => "+20" + localMobile[1..];

    private static StartSubscriptionRequest Start(Place place, string managerMobile) =>
        new("Al Shifa Lab", "معمل الشفاء", place.CountryId, place.GovernorateId, "5 Nile St.", "01012345678", "LIC-9", "Dr. Ahmed", managerMobile, "owner@shifa.example", "ar");

    private async Task<HttpResponseMessage> CompleteAsync(HttpClient anonymous, Guid subscriptionId, string username, string password)
    {
        var path = $"{Subscriptions}/complete";
        var challenge = await new AuthClient(anonymous).ChallengeAsync();
        return await anonymous.PostAsJsonAsync(path, new { subscriptionId, username, password = TestSealer.Seal(challenge, "password", password, "POST " + path) });
    }

    private async Task<HttpResponseMessage> ProvisionAsync(HttpClient admin, Place place, string slug, string adminUsername, string managerMobile, string mobile = "01012345678")
    {
        var challenge = await new AuthClient(admin).ChallengeAsync();
        return await admin.PostAsJsonAsync(Clients, new
        {
            nameEn = "Al Noor " + slug,
            nameAr = "معمل النور",
            countryId = place.CountryId,
            governorateId = place.GovernorateId,
            address = "12 Tahrir St.",
            mobile,
            licenceNo = "LIC-1",
            managerName = "Dr. Mona",
            managerMobile,
            email = "info@noor.example",
            planId = place.PlanId,
            portalSlug = slug,
            language = "en",
            adminUsername,
            adminPassword = TestSealer.Seal(challenge, "adminPassword", "a long enough password", "POST " + Clients),
        });
    }

    private async Task<Place> CreatePlaceAsync(HttpClient admin)
    {
        var n = Interlocked.Increment(ref _isoCounter);
        var iso = $"X{(char)('A' + (n % 26))}";
        var country = await admin.PostAsJsonAsync(Countries, new CountryRequest("Testland " + iso, "أرض الاختبار", iso, "+20", "Pound", "EGP", "ar", "Africa/Cairo", 10, null, null));
        Assert.Equal(HttpStatusCode.Created, country.StatusCode);
        var countryId = (await country.Content.ReadFromJsonAsync<CreatedResource>())!.Id;
        var governorateId = (await (await admin.PostAsJsonAsync(Governorates, new GovernorateRequest(countryId, "Cairo", "القاهرة", null, null))).Content.ReadFromJsonAsync<CreatedResource>())!.Id;
        var planCode = "T" + Guid.CreateVersion7().ToString("N")[^8..].ToUpperInvariant();
        var planId = (await (await admin.PostAsJsonAsync(Plans, new PlanRequest(planCode, "Test plan", "باقة", 10m, "EGP", 1, 5, 100, 1))).Content.ReadFromJsonAsync<CreatedResource>())!.Id;
        return new Place(countryId, governorateId, planId);
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var (username, _, _) = await factory.SeedUserAsync(tenantId: TenantIds.Platform, permissions: AdminPermissions, displayName: "Operations Admin");
        return await SignedInClientAsync(username);
    }

    private async Task<HttpClient> SignedInClientAsync(string username)
    {
        var client = factory.CreateClient();
        var (body, _, _) = await new AuthClient(client).SignInExpectingSuccessAsync(username, ApiFactory.Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");
        return client;
    }

    private sealed record Place(Guid CountryId, Guid GovernorateId, Guid PlanId);
}
