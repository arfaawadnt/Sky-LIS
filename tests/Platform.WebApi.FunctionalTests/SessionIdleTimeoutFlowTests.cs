using System.Net;
using System.Net.Http.Json;
using Npgsql;
using Platform.Contracts.Common;
using Platform.Contracts.Identity;
using Platform.SharedKernel.MultiTenancy;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// Validation F-01: the tenant setting <c>sessionTimeoutMinutes</c> ends an idle browser session on the server. A refresh
/// after the session went unrenewed for longer than the timeout of its tenant is refused <c>401 AUTH-017</c> and the session
/// revoked (<c>Expired</c>, <c>SESSION_REVOKED</c> info <c>idle_timeout</c>, no reuse alarm); a session renewed in time goes on.
/// The settings rows are written before the sign-in: the session read is cached for ten seconds.
/// </summary>
public sealed class SessionIdleTimeoutFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string TimeoutKey = "sessionTimeoutMinutes";

    [DatabaseFact]
    public async Task An_idle_session_past_the_tenant_timeout_is_refused_with_AUTH_017_and_revoked_without_the_reuse_alarm()
    {
        var tenant = Guid.CreateVersion7();
        await SetTimeoutAsync(tenant, "1");
        var (username, _, userId) = await factory.SeedUserAsync(tenantId: tenant);
        var auth = new AuthClient(factory.CreateClient());
        var (body, cookie, _) = await auth.SignInExpectingSuccessAsync(username, ApiFactory.Password);
        Assert.Equal(1, body.Profile!.SessionTimeoutMinutes);

        factory.Clock.Advance(TimeSpan.FromMinutes(2));
        try
        {
            var idle = await auth.RefreshAsync(cookie);
            Assert.Equal((HttpStatusCode.Unauthorized, AuthProblemCodes.SessionIdleTimeout), (idle.StatusCode, await AuthClient.CodeOf(idle)));

            // the session is over: the same cookie again is the ordinary refusal of an ended session (R2-06), not reuse
            var again = await auth.RefreshAsync(cookie);
            Assert.Equal((HttpStatusCode.Unauthorized, ProblemCodes.RefreshTokenRejected), (again.StatusCode, await AuthClient.CodeOf(again)));
        }
        finally
        {
            factory.Clock.Advance(TimeSpan.FromMinutes(-2));
        }

        Assert.Equal("Expired", Assert.Single(await RevocationReasonsOfAsync(userId)));
        var events = await SecurityEventsOfAsync(userId);
        Assert.Contains(("SESSION_REVOKED", "info", "idle_timeout"), events);
        Assert.DoesNotContain(events, row => row.Name == "REFRESH_REUSE_DETECTED");
    }

    [DatabaseFact]
    public async Task A_session_renewed_inside_the_timeout_goes_on_however_long_it_lasts()
    {
        var tenant = Guid.CreateVersion7();
        await SetTimeoutAsync(tenant, "1");
        var (username, _, userId) = await factory.SeedUserAsync(tenantId: tenant);
        var auth = new AuthClient(factory.CreateClient());
        var (_, cookie, _) = await auth.SignInExpectingSuccessAsync(username, ApiFactory.Password);

        var elapsed = TimeSpan.Zero;
        try
        {
            // three renewals 50 s apart: 150 s in all, more than twice the timeout, never 60 s without one
            for (var renewal = 0; renewal < 3; renewal++)
            {
                factory.Clock.Advance(TimeSpan.FromSeconds(50));
                elapsed += TimeSpan.FromSeconds(50);
                var refreshed = await auth.RefreshAsync(cookie);
                Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
                cookie = AuthClient.CookieValue(refreshed.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("__Secure-rt=", StringComparison.Ordinal)));
            }
        }
        finally
        {
            factory.Clock.Advance(-elapsed);
        }

        Assert.DoesNotContain(await SecurityEventsOfAsync(userId), row => row.Detail == "idle_timeout");
        Assert.DoesNotContain("Expired", await RevocationReasonsOfAsync(userId));
    }

    [DatabaseFact]
    public async Task The_timeout_is_the_laboratory_value_else_the_platform_value_else_thirty_minutes()
    {
        // without any row: the default 30 minutes
        var plain = Guid.CreateVersion7();
        var (plainUser, _, _) = await factory.SeedUserAsync(tenantId: plain);
        var auth = new AuthClient(factory.CreateClient());
        var (plainBody, plainCookie, _) = await auth.SignInExpectingSuccessAsync(plainUser, ApiFactory.Password);
        Assert.Equal(30, plainBody.Profile!.SessionTimeoutMinutes);

        // the platform's value for a laboratory that set none, the laboratory's own over it, and the platform's for its own users
        var inheriting = Guid.CreateVersion7();
        var own = Guid.CreateVersion7();
        await SetTimeoutAsync(own, "120");
        await SetTimeoutAsync(TenantIds.Platform, "45");
        try
        {
            var (inheritingUser, _, _) = await factory.SeedUserAsync(tenantId: inheriting);
            var (ownUser, _, _) = await factory.SeedUserAsync(tenantId: own);
            var (platformUser, _, _) = await factory.SeedUserAsync(tenantId: TenantIds.Platform);

            var (inheritingBody, inheritingCookie, _) = await auth.SignInExpectingSuccessAsync(inheritingUser, ApiFactory.Password);
            var (ownBody, ownCookie, _) = await auth.SignInExpectingSuccessAsync(ownUser, ApiFactory.Password);
            var (platformBody, _, _) = await auth.SignInExpectingSuccessAsync(platformUser, ApiFactory.Password);

            Assert.Equal(45, inheritingBody.Profile!.SessionTimeoutMinutes);
            Assert.Equal(120, ownBody.Profile!.SessionTimeoutMinutes);
            Assert.Equal(45, platformBody.Profile!.SessionTimeoutMinutes);

            // 40 idle minutes: past the default, inside 45 and 120
            factory.Clock.Advance(TimeSpan.FromMinutes(40));
            try
            {
                var defaulted = await auth.RefreshAsync(plainCookie);
                Assert.Equal((HttpStatusCode.Unauthorized, AuthProblemCodes.SessionIdleTimeout), (defaulted.StatusCode, await AuthClient.CodeOf(defaulted)));
                Assert.Equal(HttpStatusCode.OK, (await auth.RefreshAsync(inheritingCookie)).StatusCode);
                var renewed = await auth.RefreshAsync(ownCookie);
                Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
                Assert.Equal(120, (await renewed.Content.ReadFromJsonAsync<AuthResponse>())!.Profile!.SessionTimeoutMinutes);
            }
            finally
            {
                factory.Clock.Advance(TimeSpan.FromMinutes(-40));
            }
        }
        finally
        {
            await DeleteTimeoutAsync(TenantIds.Platform);   // the platform row is shared by every test class of this process
        }
    }

    private async Task SetTimeoutAsync(Guid tenantId, string value)
    {
        if (!TenantIds.IsPlatform(tenantId))
        {
            await factory.EnsureClientRecordAsync(tenantId);   // every tenant_id references saas.tenants (DbReviewTenantKeys)
        }

        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO app.tenant_settings (tenant_id, key, value, updated_at_utc) VALUES (@tenant, @key, @value, now()) ON CONFLICT (tenant_id, key) DO UPDATE SET value = excluded.value",
            connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("key", TimeoutKey);
        command.Parameters.AddWithValue("value", value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DeleteTimeoutAsync(Guid tenantId)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("DELETE FROM app.tenant_settings WHERE tenant_id = @tenant AND key = @key", connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("key", TimeoutKey);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<(string Name, string Severity, string? Detail)>> SecurityEventsOfAsync(Guid userId)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT name, severity, detail FROM audit.security_events WHERE actor_user_id = @user ORDER BY at_utc, id", connection);
        command.Parameters.AddWithValue("user", userId);
        var rows = new List<(string, string, string?)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return rows;
    }

    private static async Task<List<string?>> RevocationReasonsOfAsync(Guid userId)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT revocation_reason FROM identity.refresh_sessions WHERE platform_user_id = @user ORDER BY issued_at_utc", connection);
        command.Parameters.AddWithValue("user", userId);
        var reasons = new List<string?>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            reasons.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
        }

        return reasons;
    }
}
