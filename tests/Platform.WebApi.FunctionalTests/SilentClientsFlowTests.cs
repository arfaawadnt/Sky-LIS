using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Npgsql;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;
using Platform.SharedKernel.MultiTenancy;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// LIS-CR-007 A-01 over the real API: a client whose user signed in today is not silent, one whose last sign-in is four days
/// old is (for three days, not for seven), an inactive client never is; the dashboard counts them for the default window
/// of three days; the CSV is UTF-8 with a byte-order mark; a window of 0 or 91 days is a 400; the list is
/// <c>platform.view</c> in the platform tenant only. Clients and sessions are aged with SQL through the owner connection.
/// </summary>
public sealed class SilentClientsFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Silent = "/api/v1/platform/clients/silent";
    private const string Dashboard = "/api/v1/platform/dashboard";

    [DatabaseFact]
    public async Task A_client_signed_in_today_is_not_silent_and_one_quiet_for_four_days_is()
    {
        var admin = await AdminClientAsync();

        var busy = Guid.CreateVersion7();
        var (busyUser, _, _) = await factory.SeedUserAsync(tenantId: busy, permissions: ["registration.view"], displayName: "Busy Reception");
        var quiet = Guid.CreateVersion7();
        var (quietUser, _, _) = await factory.SeedUserAsync(tenantId: quiet, permissions: ["registration.view"], displayName: "Quiet Reception");
        var closed = Guid.CreateVersion7();
        await factory.SeedUserAsync(tenantId: closed, permissions: ["registration.view"]);

        // Every client is a month old; the busy one signs in today, the quiet one signed in four days ago, the closed one is inactive.
        foreach (var tenant in new[] { busy, quiet, closed })
        {
            await OwnerSqlAsync("UPDATE saas.tenants SET created_at_utc = created_at_utc - interval '30 days' WHERE id = @id", tenant);
        }

        await SignedInClientAsync(busyUser);
        await SignedInClientAsync(quietUser);
        await OwnerSqlAsync("UPDATE identity.refresh_sessions SET issued_at_utc = issued_at_utc - interval '4 days' WHERE tenant_id = @id", quiet);
        await OwnerSqlAsync("UPDATE saas.tenants SET status = 'Inactive' WHERE id = @id", closed);

        var list = (await admin.GetFromJsonAsync<SilentClientList>($"{Silent}?days=3"))!;
        Assert.Equal(3, list.Days);
        Assert.DoesNotContain(list.Items, item => item.Id == busy);
        Assert.DoesNotContain(list.Items, item => item.Id == closed);
        var row = Assert.Single(list.Items, item => item.Id == quiet);
        Assert.Equal(("Functional Laboratory", "Active", 4), (row.NameEn, row.Status, row.SilentDays));
        Assert.EndsWith($"/lab/{ApiFactory.SlugOf(quiet)}", row.PortalUrl, StringComparison.Ordinal);
        Assert.NotNull(row.LastSignInAtUtc);
        Assert.Null(row.LastRegistrationAtUtc);
        Assert.Equal(row.LastSignInAtUtc, row.LastActivityAtUtc);
        Assert.Equal(factory.Clock.UtcNow.AddDays(-4), row.LastSignInAtUtc!.Value, TimeSpan.FromMinutes(1));

        // Default window is three days; a seven-day window clears the quiet client.
        var byDefault = (await admin.GetFromJsonAsync<SilentClientList>(Silent))!;
        Assert.Contains(byDefault.Items, item => item.Id == quiet);
        var week = (await admin.GetFromJsonAsync<SilentClientList>($"{Silent}?days=7"))!;
        Assert.DoesNotContain(week.Items, item => item.Id == quiet);

        // The dashboard tile counts silent clients for three days by default (other suites' clients may be counted too: at least the quiet one).
        var dashboard = (await admin.GetFromJsonAsync<PlatformDashboard>(Dashboard))!;
        Assert.Equal(3, dashboard.SilentDays);
        Assert.True(dashboard.SilentClients >= 1, $"silent clients: {dashboard.SilentClients}");
        Assert.InRange(dashboard.SilentTrialClients, 0, dashboard.SilentClients);
        var weekDashboard = (await admin.GetFromJsonAsync<PlatformDashboard>($"{Dashboard}?silentDays=7"))!;
        Assert.Equal(7, weekDashboard.SilentDays);

        // The CSV: BOM, headers in the asked language, the quiet client's address, not the busy one's.
        var export = await admin.GetAsync($"{Silent}/export?days=3&language=ar");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("text/csv", export.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("silent-clients-3d-", export.Content.Headers.ContentDisposition!.FileName!.Trim('"'), StringComparison.Ordinal);
        var bytes = await export.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes[3..]);
        Assert.StartsWith("العميل (إنجليزي),", text, StringComparison.Ordinal);
        Assert.Contains(ApiFactory.SlugOf(quiet), text, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiFactory.SlugOf(busy), text, StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task A_window_outside_1_to_90_days_is_a_400_and_the_list_is_platform_view_in_the_platform_tenant_only()
    {
        var admin = await AdminClientAsync();
        foreach (var path in new[] { $"{Silent}?days=0", $"{Silent}?days=91", $"{Silent}/export?days=0", $"{Dashboard}?silentDays=0", $"{Silent}/export?language=fr" })
        {
            var refused = await admin.GetAsync(path);
            Assert.True(refused.StatusCode == HttpStatusCode.BadRequest, $"{path} answered {(int)refused.StatusCode}");
            Assert.Equal(ProblemCodes.ValidationFailed, await AuthClient.CodeOf(refused));
        }

        var (clerk, _, _) = await factory.SeedUserAsync(tenantId: TenantIds.Platform, permissions: ["tenancy.view"]);
        var noKey = await (await SignedInClientAsync(clerk)).GetAsync(Silent);
        Assert.Equal(HttpStatusCode.Forbidden, noKey.StatusCode);
        Assert.Equal(ProblemCodes.Forbidden, await AuthClient.CodeOf(noKey));

        var (labAdmin, _, _) = await factory.SeedUserAsync(tenantId: Guid.CreateVersion7(), permissions: ["platform.view", "platform.manage"]);
        var lab = await SignedInClientAsync(labAdmin);
        foreach (var path in new[] { Silent, $"{Silent}/export" })
        {
            var refused = await lab.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Contains(await AuthClient.CodeOf(refused), new[] { ProblemCodes.Forbidden, "PLT-403" });
        }
    }

    private static async Task OwnerSqlAsync(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }

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
