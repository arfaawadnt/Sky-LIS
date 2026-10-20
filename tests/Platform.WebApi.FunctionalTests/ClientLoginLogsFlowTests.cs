using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.PlatformAdmin;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;
using Platform.SharedKernel.MultiTenancy;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// SA-08b client login logs over the real API (change request LIS-CR-003 §2): a client user's sign-in and sign-out
/// become one row with its device, the filters and the sort keys are served, the CSV is UTF-8 with a byte-order mark,
/// a reversed window is a 400, and nobody without <c>platform.view</c> in the platform tenant reads the list.
/// </summary>
public sealed class ClientLoginLogsFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string LoginLogs = "/api/v1/platform/login-logs";
    private const string ChromeWindows = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36";
    private const string SafariIphone = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1";

    [DatabaseFact]
    public async Task A_client_users_session_is_one_row_that_ends_SignedOut_with_its_duration_and_device()
    {
        var admin = await AdminClientAsync();
        var tenant = Guid.CreateVersion7();
        var (receptionist, _, receptionistId) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["registration.view"], displayName: "Noor Reception");
        var (cashier, _, _) = await factory.SeedUserAsync(tenantId: tenant, permissions: ["billing.view"], displayName: "Sara Cashier");

        // The receptionist signs in from a Windows desktop, works seven minutes and signs out; the cashier stays signed in on a phone.
        var desktop = factory.CreateClient();
        desktop.DefaultRequestHeaders.UserAgent.ParseAdd(ChromeWindows);
        var (_, cookie, _) = await new AuthClient(desktop).SignInExpectingSuccessAsync(receptionist, ApiFactory.Password);
        var loginAt = factory.Clock.UtcNow;
        factory.Clock.Advance(TimeSpan.FromMinutes(7));
        Assert.Equal(HttpStatusCode.NoContent, (await new AuthClient(desktop).SignOutAsync(cookie)).StatusCode);
        var logoutAt = factory.Clock.UtcNow;

        var phone = factory.CreateClient();
        phone.DefaultRequestHeaders.UserAgent.ParseAdd(SafariIphone);
        await new AuthClient(phone).SignInExpectingSuccessAsync(cashier, ApiFactory.Password);

        var page = (await admin.GetFromJsonAsync<Paged<ClientLoginLogItem>>($"{LoginLogs}?tenantId={tenant}"))!;
        Assert.Equal(2, page.TotalCount);
        var signedOut = Assert.Single(page.Items, item => item.UserId == receptionistId);
        Assert.Equal(("SignedOut", 7, "Computer", "Windows 10", "Chrome 129", receptionist, "Noor Reception", tenant), (signedOut.Status, signedOut.DurationMinutes, signedOut.Device, signedOut.Os, signedOut.Browser, signedOut.Username, signedOut.DisplayName, signedOut.TenantId));
        Assert.Equal(loginAt, signedOut.LoginAtUtc, TimeSpan.FromMilliseconds(1));   // the column keeps microseconds
        Assert.Equal(logoutAt, signedOut.LogoutAtUtc!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Equal("Functional Laboratory", signedOut.ClientNameEn);   // LIS-CR-005: the client record the test host gives every seeded laboratory
        Assert.Null(signedOut.RevocationReason);
        var active = Assert.Single(page.Items, item => item.Username == cashier);
        Assert.Equal(("Active", "Mobile", "iOS 17.5", "Safari 17", null), (active.Status, active.Device, active.Os, active.Browser, active.LogoutAtUtc));

        // Default order: newest login first.
        Assert.Equal([cashier, receptionist], page.Items.Select(item => item.Username));

        // The platform's own sessions stay out unless asked for by id.
        var all = (await admin.GetFromJsonAsync<Paged<ClientLoginLogItem>>($"{LoginLogs}?pageSize=200"))!;
        Assert.DoesNotContain(all.Items, item => item.TenantId == TenantIds.Platform);
        Assert.Contains(all.Items, item => item.UserId == receptionistId);
        var platform = (await admin.GetFromJsonAsync<Paged<ClientLoginLogItem>>($"{LoginLogs}?tenantId={TenantIds.Platform}"))!;
        Assert.NotEmpty(platform.Items);
        Assert.All(platform.Items, item => Assert.Equal(TenantIds.Platform, item.TenantId));

        // Filters: status, search, device (classified after the read), minutes, login and logout windows.
        Assert.Equal([cashier], await UsernamesAsync(admin, $"tenantId={tenant}&status=Active"));
        Assert.Equal([receptionist], await UsernamesAsync(admin, $"tenantId={tenant}&status=SignedOut"));
        Assert.Equal([receptionist], await UsernamesAsync(admin, $"tenantId={tenant}&search=noor%20rec"));
        Assert.Equal([cashier], await UsernamesAsync(admin, $"tenantId={tenant}&device=Mobile"));
        Assert.Equal([receptionist], await UsernamesAsync(admin, $"tenantId={tenant}&device=computer"));
        Assert.Equal([receptionist], await UsernamesAsync(admin, $"tenantId={tenant}&minMinutes=5"));
        Assert.Equal([cashier], await UsernamesAsync(admin, $"tenantId={tenant}&maxMinutes=5"));
        Assert.Equal([cashier], await UsernamesAsync(admin, $"tenantId={tenant}&loginFromUtc={Uri.EscapeDataString(loginAt.AddMinutes(1).ToString("O"))}"));
        Assert.Equal([receptionist], await UsernamesAsync(admin, $"tenantId={tenant}&logoutFromUtc={Uri.EscapeDataString(loginAt.ToString("O"))}&logoutToUtc={Uri.EscapeDataString(logoutAt.ToString("O"))}"));
        Assert.Empty(await UsernamesAsync(admin, $"tenantId={tenant}&logoutToUtc={Uri.EscapeDataString(loginAt.ToString("O"))}"));

        // Every allow-listed sort key is served in both directions; the duration order is checked.
        foreach (var key in PlatformAdminSortKeys.ClientLoginLogs.Names)
        {
            foreach (var direction in new[] { ListSort.Ascending, ListSort.DescendingName })
            {
                var sorted = await admin.GetAsync($"{LoginLogs}?tenantId={tenant}&sort={key}&direction={direction}");
                Assert.Equal(HttpStatusCode.OK, sorted.StatusCode);
            }
        }

        Assert.Equal([cashier, receptionist], await UsernamesAsync(admin, $"tenantId={tenant}&sort=duration&direction=asc"));
        Assert.Equal([receptionist, cashier], await UsernamesAsync(admin, $"tenantId={tenant}&sort=user&direction=asc"));

        // Page 2 of size 1 continues page 1.
        var second = (await admin.GetFromJsonAsync<Paged<ClientLoginLogItem>>($"{LoginLogs}?tenantId={tenant}&sort=login&direction=desc&page=2&pageSize=1"))!;
        Assert.Equal((2L, receptionist), (second.TotalCount, Assert.Single(second.Items).Username));

        // The CSV: UTF-8 with a byte-order mark, the filter applied, an attachment named with the stamp.
        var export = await admin.GetAsync($"{LoginLogs}/export?tenantId={tenant}&status=SignedOut&language=ar");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("text/csv", export.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("client-login-logs-", export.Content.Headers.ContentDisposition!.FileName!.Trim('"'), StringComparison.Ordinal);
        var bytes = await export.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("العميل,", lines[0], StringComparison.Ordinal);
        Assert.Contains(receptionist, lines[1], StringComparison.Ordinal);
        Assert.Contains(",Computer,Windows 10,Chrome 129,7,SignedOut,", lines[1], StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task A_reversed_window_or_an_unknown_name_is_a_400_and_the_list_is_platform_view_in_the_platform_tenant_only()
    {
        var admin = await AdminClientAsync();
        foreach (var query in new[]
                 {
                     "loginFromUtc=2026-10-20T00:00:00Z&loginToUtc=2026-10-19T00:00:00Z",
                     "logoutFromUtc=2026-10-20T00:00:00Z&logoutToUtc=2026-10-19T00:00:00Z",
                     "minMinutes=10&maxMinutes=5",
                     "minMinutes=-1",
                     "device=Watch",
                     "status=Open",
                     "sort=ipAddress",
                     "pageSize=0",
                 })
        {
            var refused = await admin.GetAsync($"{LoginLogs}?{query}");
            Assert.True(refused.StatusCode == HttpStatusCode.BadRequest, $"{query} answered {(int)refused.StatusCode}");
            Assert.Equal(ProblemCodes.ValidationFailed, await AuthClient.CodeOf(refused));
            if (!query.StartsWith("pageSize", StringComparison.Ordinal))   // the export has no page: the paging rule is the list's alone
            {
                var exportRefused = await admin.GetAsync($"{LoginLogs}/export?{query}");
                Assert.True(exportRefused.StatusCode == HttpStatusCode.BadRequest, $"export {query} answered {(int)exportRefused.StatusCode}");
            }
        }

        // A platform user without platform.view, and a laboratory user whatever its role holds (platform.* is platform-only, D-04).
        var (clerk, _, _) = await factory.SeedUserAsync(tenantId: TenantIds.Platform, permissions: ["tenancy.view"]);
        var noKey = await (await SignedInClientAsync(clerk)).GetAsync(LoginLogs);
        Assert.Equal(HttpStatusCode.Forbidden, noKey.StatusCode);
        Assert.Equal(ProblemCodes.Forbidden, await AuthClient.CodeOf(noKey));

        var (labAdmin, _, _) = await factory.SeedUserAsync(tenantId: Guid.CreateVersion7(), permissions: ["platform.view", "platform.manage"]);
        var lab = await SignedInClientAsync(labAdmin);
        foreach (var path in new[] { LoginLogs, $"{LoginLogs}/export" })
        {
            var refused = await lab.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Contains(await AuthClient.CodeOf(refused), new[] { ProblemCodes.Forbidden, "PLT-403" });
        }
    }

    private static async Task<List<string>> UsernamesAsync(HttpClient admin, string query) =>
        (await admin.GetFromJsonAsync<Paged<ClientLoginLogItem>>($"{LoginLogs}?{query}"))!.Items.Select(item => item.Username).ToList();

    private async Task<HttpClient> AdminClientAsync()
    {
        var (username, _, _) = await factory.SeedUserAsync(tenantId: TenantIds.Platform, permissions: ["platform.view", "platform.manage"], displayName: "Operations Admin");
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
}
