using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;
using Platform.Infrastructure.Persistence;
using Platform.SharedKernel.MultiTenancy;
using Platform.WebApi.FunctionalTests.Support;
using Platform.TestSupport;

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
        Assert.Equal($"{Platform.Infrastructure.Security.ConfigGuard.DefaultPortalBaseUrl}/lab/{slug}", provisioned.PortalUrl);   // LIS-CR-005: {Lis:Platform:PortalBaseUrl}/lab/{slug}
        Assert.Equal("portal-url", factory.Notifications.LastTo(Global(manager))!.TemplateCode);

        var taken = await ProvisionAsync(admin, place, slug, "other.admin." + slug[^8..], NewMobile());
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal("CLI-100", await AuthClient.CodeOf(taken));

        var badMobile = await ProvisionAsync(admin, place, slug + "-x", "third.admin." + slug[^8..], NewMobile(), mobile: "0101234");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badMobile.StatusCode);
        Assert.Equal("GEO-010", await AuthClient.CodeOf(badMobile));
        var badManager = await ProvisionAsync(admin, place, slug + "-y", "fourth.admin." + slug[^8..], "+2010111100");
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "GEO-014"), (badManager.StatusCode, await AuthClient.CodeOf(badManager)));   // C-03

        var page = await admin.GetFromJsonAsync<Paged<ClientListItem>>($"{Clients}?search={slug}&source=Manual");
        var item = Assert.Single(page!.Items);
        Assert.Equal(("Active", "Manual", Global(manager)), (item.Status, item.Source, item.ManagerMobile));

        var noReason = await admin.PostAsJsonAsync($"{Clients}/{provisioned.Id}/deactivate", new DeactivateClientRequest(""));
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"{Clients}/{provisioned.Id}/deactivate", new DeactivateClientRequest("unpaid"))).StatusCode);
        Assert.Equal("Inactive", (await admin.GetFromJsonAsync<ClientDetail>($"{Clients}/{provisioned.Id}"))!.Status);

        // INV-PLT-003: the laboratory's administrator is refused by name (CLI-020), not with the generic "no membership" (UAT dry run D-13).
        var blocked = await new AuthClient(factory.CreateClient()).SignInAsync(provisioned.AdministratorUsername, "a long enough password");
        Assert.Equal((HttpStatusCode.Unauthorized, "CLI-020"), (blocked.StatusCode, await AuthClient.CodeOf(blocked)));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Clients}/{provisioned.Id}/activate", null)).StatusCode);
        await new AuthClient(factory.CreateClient()).SignInExpectingSuccessAsync(provisioned.AdministratorUsername, "a long enough password");
        var twice = await admin.PostAsync($"{Clients}/{provisioned.Id}/activate", null);
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal("CLI-010", await AuthClient.CodeOf(twice));
    }

    [DatabaseFact]
    public async Task A_client_creation_is_filed_in_the_platform_trail_and_in_the_laboratory_trail()
    {
        // Validation 2026-10-11 C-17: SA-09 lists every change made in the Super Admin portal, the client creation included.
        var (username, _, adminId) = await factory.SeedUserAsync(tenantId: TenantIds.Platform, permissions: [.. AdminPermissions, "audit.view"], displayName: "Trail Admin");
        var admin = await SignedInClientAsync(username);
        var place = await CreatePlaceAsync(admin);
        var slug = "trail-" + Guid.CreateVersion7().ToString("N")[^8..];
        var created = await ProvisionAsync(admin, place, slug, "trail.admin." + slug[^8..], NewMobile());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var clientId = (await created.Content.ReadFromJsonAsync<ProvisionedClient>())!.Id;

        var rows = await ProvisioningTrailAsync(clientId);
        Assert.Equal(2, rows.Count);
        var platform = Assert.Single(rows, row => row.Tenant == TenantIds.Platform);
        var laboratory = Assert.Single(rows, row => row.Tenant == clientId);
        Assert.Equal((adminId, place.PlanId.ToString("D")), (platform.Actor, platform.Plan));
        Assert.Equal((adminId, platform.EventId), (laboratory.Actor, laboratory.EventId));

        var chain = (await admin.GetFromJsonAsync<Platform.Contracts.Audit.ChainVerificationResult>("/api/v1/audit/verify-chain"))!;
        Assert.True(chain.Ok, $"the platform chain breaks at {chain.FirstBrokenSequence}");
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
        Assert.Equal("GEO-014", await AuthClient.CodeOf(wrongLength));   // C-03: the manager's mobile, by its own code

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
        Assert.StartsWith($"{Platform.Infrastructure.Security.ConfigGuard.DefaultPortalBaseUrl}/lab/", result.PortalUrl, StringComparison.Ordinal);   // LIS-CR-005
        Assert.Equal("portal-url", factory.Notifications.LastTo(e164)!.TemplateCode);

        var availability = await anonymous.GetFromJsonAsync<UsernameAvailability>($"{Subscriptions}/check-username?username={username}");
        Assert.False(availability!.Available);
        var reuse = await CompleteAsync(anonymous, started.SubscriptionId, username + "2", "a long enough password");
        Assert.Equal("SUB-003", await AuthClient.CodeOf(reuse));

        var detail = await admin.GetFromJsonAsync<ClientDetail>($"{Clients}/{result.TenantId}");
        Assert.Equal(("Trial", "Subscription", username), (detail!.Status, detail.Source, detail.AdministratorUsername));
        var trail = await ProvisioningTrailAsync(result.TenantId);   // C-17: the wizard's client is in the platform trail too
        Assert.Equal(2, trail.Count);
        Assert.Contains(trail, row => row.Tenant == TenantIds.Platform);
        Assert.Contains(trail, row => row.Tenant == result.TenantId);

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
    public async Task Run_charges_now_debits_a_due_wallet_once_and_the_ledger_shows_the_subscription_row()
    {
        var admin = await AdminClientAsync();
        var place = await CreatePlaceAsync(admin);   // plan fee 10 EGP
        var slug = "charge-" + Guid.CreateVersion7().ToString("N")[^8..];
        var provisioned = (await (await ProvisionAsync(admin, place, slug, "charge.admin." + slug[^8..], NewMobile())).Content.ReadFromJsonAsync<ProvisionedClient>())!;
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"{Wallets}/{provisioned.Id}/top-up", new TopUpWalletRequest(100m, "INV-100", null, null))).StatusCode);

        // A manual client's first charge falls a month after provisioning: bring it forward instead of moving the clock (tokens use the machine clock).
        var dueAt = factory.Clock.UtcNow.AddDays(-1);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync($"UPDATE saas.wallets SET next_charge_at_utc = {dueAt} WHERE tenant_id = {provisioned.Id}");
        }

        var run = await admin.PostAsync($"{Wallets}/charge-run", null);
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        var summary = (await run.Content.ReadFromJsonAsync<WalletChargeRunSummary>())!;
        var item = Assert.Single(summary.Items, row => row.TenantId == provisioned.Id);
        Assert.Equal(("Charged", -10m, 90m), (item.Outcome, item.Amount, item.BalanceAfter));
        Assert.True(summary.Charged >= 1);

        var wallet = (await admin.GetFromJsonAsync<WalletDetail>($"{Wallets}/{provisioned.Id}"))!;
        Assert.Equal(90m, wallet.Balance);
        Assert.Equal(dueAt.AddMonths(1).ToUnixTimeSeconds(), wallet.NextChargeAtUtc.ToUnixTimeSeconds());
        var ledger = (await admin.GetFromJsonAsync<Paged<WalletTransactionItem>>($"{Wallets}/{provisioned.Id}/transactions?kind=Subscription"))!;
        var charge = Assert.Single(ledger.Items);
        Assert.Equal((-10m, 90m), (charge.Amount, charge.BalanceAfter));
        Assert.StartsWith($"SUB-{item.Period}-T", charge.Reference, StringComparison.Ordinal);   // SUB-<period>-<plan code>
        Assert.Null(charge.ByUserId);   // a system movement

        var again = (await (await admin.PostAsync($"{Wallets}/charge-run", null)).Content.ReadFromJsonAsync<WalletChargeRunSummary>())!;
        Assert.DoesNotContain(again.Items, row => row.TenantId == provisioned.Id);   // same period: not due any more
        Assert.Single((await admin.GetFromJsonAsync<Paged<WalletTransactionItem>>($"{Wallets}/{provisioned.Id}/transactions?kind=Subscription"))!.Items);

        // tenancy.* is platform-only: a laboratory principal never runs the charges.
        var (labUser, _, _) = await factory.SeedUserAsync(tenantId: provisioned.Id, permissions: ["tenancy.manage"]);
        var refused = await (await SignedInClientAsync(labUser)).PostAsync($"{Wallets}/charge-run", null);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
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

    /// <summary>The <c>TenantProvisioned</c> entries of a client in every trail (read as the owner role, across tenants).</summary>
    private static async Task<List<(Guid Tenant, Guid? Actor, string? Plan, Guid EventId)>> ProvisioningTrailAsync(Guid clientId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            """
            SELECT tenant_id, actor_user_id, payload->>'planId', event_id FROM audit.audit_trail
            WHERE aggregate_type = 'Tenant' AND aggregate_id = @client AND event_type LIKE '%.TenantProvisioned' ORDER BY tenant_id
            """, connection);
        command.Parameters.AddWithValue("client", clientId.ToString("D"));
        var rows = new List<(Guid Tenant, Guid? Actor, string? Plan, Guid EventId)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetGuid(3)));
        }

        return rows;
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
        var iso = TestCountryCodes.Next();
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
