using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Platform.Contracts.Common;
using Platform.Contracts.Identity;
using Platform.WebApi.FunctionalTests.Support;

namespace Platform.WebApi.FunctionalTests;

/// <summary>
/// SEC-E01…E06, E09, E11 (02 §8): security headers, envelope refusals by code, token claims, cookie flags, refresh
/// rotation and reuse detection, cookie-only data requests, CSRF on the cookie endpoints, lockout accounting.
/// </summary>
public sealed class AuthFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string[] HeaderPaths = ["/health/live", "/api/v1/auth/me", "/no-such-route"];

    /// <summary>Just past the refresh grace of C-02 (<c>SessionPolicy.RefreshReuseGrace</c>, 5 s by default).</summary>
    private static readonly TimeSpan GraceElapsed = TimeSpan.FromSeconds(6);

    private static int _isoCounter;

    [DatabaseFact]
    public async Task Security_headers_are_present_on_success_and_error_responses()
    {
        var client = factory.CreateClient();
        foreach (var path in HeaderPaths)
        {
            var response = await client.GetAsync(path);
            Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
            Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
            Assert.Contains("max-age=63072000", response.Headers.GetValues("Strict-Transport-Security").Single(), StringComparison.Ordinal);
            Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
            Assert.True(response.Headers.TryGetValues("X-Correlation-Id", out _));
        }
    }

    [DatabaseFact]
    public async Task The_challenge_names_the_key_the_algorithms_and_a_two_minute_nonce()
    {
        var auth = new AuthClient(factory.CreateClient());

        var challenge = await auth.ChallengeAsync();

        Assert.Equal("RSA-OAEP-256", challenge.Algorithm);
        Assert.Equal("A256GCM", challenge.Encryption);
        Assert.Equal("RSA", challenge.PublicKey.Kty);
        Assert.StartsWith("-----BEGIN PUBLIC KEY-----", challenge.PublicKeyPem, StringComparison.Ordinal);
        Assert.Equal(3072 / 8, Base64UrlEncoder.DecodeBytes(challenge.PublicKey.N).Length);
        Assert.InRange((challenge.ExpiresAtUtc - factory.Clock.UtcNow).TotalSeconds, 100, 125);
        Assert.NotEqual(challenge.Nonce, (await auth.ChallengeAsync()).Nonce);
    }

    [DatabaseFact]
    public async Task Sign_in_issues_a_token_with_the_specified_claims_and_the_secure_refresh_cookie()
    {
        var (username, tenant, userId) = await factory.SeedUserAsync();
        var auth = new AuthClient(factory.CreateClient());

        var (body, cookie, setCookie) = await auth.SignInExpectingSuccessAsync(username, ApiFactory.Password);

        Assert.Equal(AuthResponse.StepSignedIn, body.Step);
        Assert.Null(body.RefreshToken);
        Assert.Equal(userId, body.Profile!.UserId);
        Assert.Equal(tenant, body.Profile.TenantId);

        var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(body.AccessToken!.Split('.')[1])).RootElement;
        Assert.StartsWith("v1.", payload.GetProperty("sub").GetString(), StringComparison.Ordinal);
        Assert.Equal(tenant.ToString(), payload.GetProperty("tid").GetString());
        Assert.False(payload.TryGetProperty("email", out _));
        Assert.False(payload.TryGetProperty("name", out _));
        Assert.False(payload.TryGetProperty("role", out _));
        Assert.Equal(900, payload.GetProperty("exp").GetInt64() - payload.GetProperty("iat").GetInt64());

        Assert.Contains("path=/api/v1/auth", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("max-age=", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(33, cookie.IndexOf('.', StringComparison.Ordinal) + 1);

        var me = await auth.MeAsync(body.AccessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var session = (await me.Content.ReadFromJsonAsync<SessionResponse>())!;
        Assert.Equal(userId, session.UserId);
        Assert.Equal("full", session.Scope);
        Assert.Equal(payload.GetProperty("sid").GetString(), session.SessionId.ToString());
    }

    [DatabaseFact]
    public async Task Envelope_refusals_carry_their_codes_and_never_touch_the_lockout_counter()
    {
        var (username, _, userId) = await factory.SeedUserAsync();
        var auth = new AuthClient(factory.CreateClient());

        Assert.Equal(ProblemCodes.CredentialPlaintext, await AuthClient.CodeOf(Assert400(await auth.SignInRawAsync(username, ApiFactory.Password))));

        var challenge = await auth.ChallengeAsync();
        var sealedPassword = TestSealer.Seal(challenge, "password", ApiFactory.Password, "POST " + AuthClient.SignInPath);
        Assert.Equal(ProblemCodes.CredentialEnvelopeMalformed, await AuthClient.CodeOf(Assert400(await auth.SignInRawAsync(username, sealedPassword[..^4] + "AAAA"))));

        var retargeted = TestSealer.Seal(await auth.ChallengeAsync(), "password", ApiFactory.Password, "POST /api/v1/auth/reauthenticate");
        Assert.Equal(ProblemCodes.CredentialRetargeted, await AuthClient.CodeOf(Assert400(await auth.SignInRawAsync(username, retargeted))));

        var ok = await auth.SignInRawAsync(username, sealedPassword);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(ProblemCodes.CredentialReplayed, await AuthClient.CodeOf(Assert400(await auth.SignInRawAsync(username, sealedPassword))));

        var stale = await auth.ChallengeAsync();
        factory.Clock.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(ProblemCodes.CredentialChallengeExpired, await AuthClient.CodeOf(Assert400(await auth.SignInRawAsync(username, TestSealer.Seal(stale, "password", ApiFactory.Password, "POST " + AuthClient.SignInPath)))));
        factory.Clock.Advance(TimeSpan.FromMinutes(-3));

        Assert.Equal(0, (await factory.LoadUserAsync(userId)).FailedPasswordAttempts);
    }

    [DatabaseFact]
    public async Task Refresh_rotates_and_a_reused_token_revokes_the_whole_family()
    {
        var (username, _, _) = await factory.SeedUserAsync();
        var auth = new AuthClient(factory.CreateClient());
        var (first, cookie1, _) = await auth.SignInExpectingSuccessAsync(username, ApiFactory.Password);

        var refreshed = await auth.RefreshAsync(cookie1);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var second = (await refreshed.Content.ReadFromJsonAsync<AuthResponse>())!;
        var cookie2 = AuthClient.CookieValue(refreshed.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("__Secure-rt=", StringComparison.Ordinal)));
        Assert.NotEqual(cookie1, cookie2);
        Assert.NotEqual(first.AccessToken, second.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await auth.MeAsync(second.AccessToken)).StatusCode);

        // C-02: inside the grace window the immediate predecessor is answered; replayed after it, it is reuse as before
        factory.Clock.Advance(GraceElapsed);
        var reuse = await auth.RefreshAsync(cookie1);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        Assert.Equal(ProblemCodes.RefreshTokenRejected, await AuthClient.CodeOf(reuse));

        var collateral = await auth.RefreshAsync(cookie2);
        Assert.Equal(HttpStatusCode.Unauthorized, collateral.StatusCode);

        factory.Clock.Advance(TimeSpan.FromSeconds(11));   // past the principal cache
        Assert.Equal(HttpStatusCode.Unauthorized, (await auth.MeAsync(second.AccessToken)).StatusCode);
        factory.Clock.Advance(TimeSpan.FromSeconds(-11) - GraceElapsed);
    }

    [DatabaseFact]
    public async Task Two_tabs_refreshing_with_the_same_cookie_at_once_both_succeed_and_nothing_is_revoked()
    {
        // C-02: the tabs of one browser share the cookie; both reload together and present the same token
        var (username, _, _) = await factory.SeedUserAsync();
        var auth = new AuthClient(factory.CreateClient());
        var (_, cookie1, _) = await auth.SignInExpectingSuccessAsync(username, ApiFactory.Password);

        var responses = await Task.WhenAll(auth.RefreshAsync(cookie1), auth.RefreshAsync(cookie1));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var bodies = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<AuthResponse>()));
        Assert.All(bodies, body => Assert.False(string.IsNullOrEmpty(body!.AccessToken)));

        // exactly one response rotated (and set the cookie); the other was answered with the session that replaced it
        var rotated = responses.Where(response => response.Headers.TryGetValues("Set-Cookie", out var values) && values.Any(v => v.StartsWith("__Secure-rt=", StringComparison.Ordinal))).ToList();
        var cookie2 = AuthClient.CookieValue(Assert.Single(rotated).Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("__Secure-rt=", StringComparison.Ordinal)));
        foreach (var body in bodies)
        {
            Assert.Equal(HttpStatusCode.OK, (await auth.MeAsync(body!.AccessToken)).StatusCode);
        }

        // the family is intact: the cookie the browser now holds keeps rotating
        Assert.Equal(HttpStatusCode.OK, (await auth.RefreshAsync(cookie2)).StatusCode);
    }

    [DatabaseFact]
    public async Task A_rotation_that_loses_the_race_on_the_row_is_answered_with_the_winner()
    {
        // C-02, the truly simultaneous case: both requests read the token before either committed. The loser's own rotation
        // is refused by the row version (CONCURRENCY-409); it is then judged against the committed winner, inside the grace.
        var (username, _, _) = await factory.SeedUserAsync();
        var auth = new AuthClient(factory.CreateClient());
        var (_, cookie1, _) = await auth.SignInExpectingSuccessAsync(username, ApiFactory.Password);

        using var loser = factory.Services.CreateScope();
        Assert.True(loser.ServiceProvider.GetRequiredService<Platform.Application.Abstractions.Tokens.IRefreshTokenService>().TryParse(cookie1, out var sessionId, out _));
        var stale = await loser.ServiceProvider.GetRequiredService<Platform.Application.Identity.IRefreshSessionRepository>().GetForUpdateAsync(sessionId, CancellationToken.None);
        Assert.Null(stale!.RotatedAtUtc);   // tracked by the loser's unit of work as it was before the winner rotated it

        var winner = await auth.RefreshAsync(cookie1);
        Assert.Equal(HttpStatusCode.OK, winner.StatusCode);

        var result = await loser.ServiceProvider.GetRequiredService<MediatR.ISender>().Send(new Platform.Application.Identity.Commands.RefreshSessionCommand(cookie1, null, null));
        Assert.Null(result.RefreshToken);   // a grace answer: no second successor, the winner's cookie stays current
        Assert.Equal(HttpStatusCode.OK, (await auth.MeAsync(result.AccessToken.Token)).StatusCode);
        var cookie2 = AuthClient.CookieValue(winner.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("__Secure-rt=", StringComparison.Ordinal)));
        Assert.Equal(HttpStatusCode.OK, (await auth.RefreshAsync(cookie2)).StatusCode);
    }

    [DatabaseFact]
    public async Task A_loser_that_read_the_clock_before_the_winner_rotated_is_still_inside_the_grace_R_01()
    {
        // Re-test R-01 (2026-10-12): with a real clock the losing request reads "now" before the winner's rotation is
        // stamped, so now < rotatedAt. That is the concurrent case itself and must be answered with the winner.
        var (username, _, _) = await factory.SeedUserAsync();
        var auth = new AuthClient(factory.CreateClient());
        var (_, cookie1, _) = await auth.SignInExpectingSuccessAsync(username, ApiFactory.Password);

        factory.Clock.Advance(TimeSpan.FromMilliseconds(20));   // the winner rotates 20 ms later than the loser's clock reading
        var winner = await auth.RefreshAsync(cookie1);
        Assert.Equal(HttpStatusCode.OK, winner.StatusCode);
        factory.Clock.Advance(TimeSpan.FromMilliseconds(-20));

        var loser = await auth.RefreshAsync(cookie1);
        Assert.Equal(HttpStatusCode.OK, loser.StatusCode);
        Assert.False(loser.Headers.TryGetValues("Set-Cookie", out _), "a grace answer must leave the winner's cookie in place");
        var cookie2 = AuthClient.CookieValue(winner.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("__Secure-rt=", StringComparison.Ordinal)));
        Assert.Equal(HttpStatusCode.OK, (await auth.RefreshAsync(cookie2)).StatusCode);
    }

    [DatabaseFact]
    public async Task The_previous_token_inside_the_grace_gets_the_current_session_without_a_new_cookie()
    {
        var (username, _, _) = await factory.SeedUserAsync();
        var auth = new AuthClient(factory.CreateClient());
        var (_, cookie1, _) = await auth.SignInExpectingSuccessAsync(username, ApiFactory.Password);
        var first = await auth.RefreshAsync(cookie1);
        var cookie2 = AuthClient.CookieValue(first.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("__Secure-rt=", StringComparison.Ordinal)));
        var firstToken = (await first.Content.ReadFromJsonAsync<AuthResponse>())!.AccessToken;
        var successor = (await (await auth.MeAsync(firstToken)).Content.ReadFromJsonAsync<SessionResponse>())!.SessionId;

        factory.Clock.Advance(TimeSpan.FromSeconds(3));   // inside the 5 s grace
        var late = await auth.RefreshAsync(cookie1);
        factory.Clock.Advance(TimeSpan.FromSeconds(-3));
        Assert.Equal(HttpStatusCode.OK, late.StatusCode);
        Assert.False(late.Headers.TryGetValues("Set-Cookie", out _), "a grace answer must leave the current cookie in place");
        var lateToken = (await late.Content.ReadFromJsonAsync<AuthResponse>())!.AccessToken;
        var me = (await (await auth.MeAsync(lateToken)).Content.ReadFromJsonAsync<SessionResponse>())!;
        Assert.Equal(successor, me.SessionId);   // the access token names the live successor, not the rotated session

        Assert.Equal(HttpStatusCode.OK, (await auth.RefreshAsync(cookie2)).StatusCode);
    }

    [DatabaseFact]
    public async Task An_older_generation_inside_the_grace_is_still_reuse_and_revokes_the_family()
    {
        var (username, _, _) = await factory.SeedUserAsync();
        var auth = new AuthClient(factory.CreateClient());
        var (_, cookie1, _) = await auth.SignInExpectingSuccessAsync(username, ApiFactory.Password);
        var second = await auth.RefreshAsync(cookie1);
        var cookie2 = AuthClient.CookieValue(second.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("__Secure-rt=", StringComparison.Ordinal)));
        var third = await auth.RefreshAsync(cookie2);
        var cookie3 = AuthClient.CookieValue(third.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("__Secure-rt=", StringComparison.Ordinal)));

        var reuse = await auth.RefreshAsync(cookie1);   // its successor was rotated again: two generations old
        Assert.Equal((HttpStatusCode.Unauthorized, ProblemCodes.RefreshTokenRejected), (reuse.StatusCode, await AuthClient.CodeOf(reuse)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await auth.RefreshAsync(cookie3)).StatusCode);   // the whole family is revoked
    }

    [DatabaseFact]
    public async Task A_user_who_must_change_the_password_reaches_only_the_password_change()
    {
        // C-04 (ORG-001 A13, 02 §6.1): enforced by the server, not only by the SPA's guard
        var (username, _, userId) = await factory.SeedUserAsync(permissions: ["settings.view"]);
        await using (var connection = new Npgsql.NpgsqlConnection(TestDatabase.OwnerConnection))
        {
            await connection.OpenAsync();
            await using var command = new Npgsql.NpgsqlCommand("UPDATE identity.platform_users SET must_change_password = true WHERE id = @id", connection);
            command.Parameters.AddWithValue("id", userId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        var auth = new AuthClient(factory.CreateClient());
        var (body, cookie, _) = await auth.SignInExpectingSuccessAsync(username, ApiFactory.Password);
        Assert.True(body.Profile!.MustChangePassword);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", body.AccessToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "Platform");

        var settings = await client.GetAsync("/api/v1/settings");
        Assert.Equal((HttpStatusCode.Forbidden, AuthProblemCodes.PasswordChangeRequired), (settings.StatusCode, await AuthClient.CodeOf(settings)));
        var audit = await client.GetAsync("/api/v1/audit/entries");
        Assert.Equal((HttpStatusCode.Forbidden, AuthProblemCodes.PasswordChangeRequired), (audit.StatusCode, await AuthClient.CodeOf(audit)));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/account/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(AuthClient.MePath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(AuthClient.ChallengePath)).StatusCode);
        var refreshed = await auth.RefreshAsync(cookie);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.True((await refreshed.Content.ReadFromJsonAsync<AuthResponse>())!.Profile!.MustChangePassword);

        const string newPassword = "Changed-Password-2026!";
        const string passwordPath = "/api/v1/account/password";
        var challenge = await auth.ChallengeAsync();
        var changed = await client.PostAsJsonAsync(passwordPath, new
        {
            currentPassword = TestSealer.Seal(challenge, "currentPassword", ApiFactory.Password, "POST " + passwordPath),
            newPassword = TestSealer.Seal(challenge, "newPassword", newPassword, "POST " + passwordPath),
        });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        // released at once, with the same access token (the gate re-reads a cached "must change" before refusing)
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/settings")).StatusCode);
    }

    [DatabaseFact]
    public async Task Cookie_only_requests_cannot_reach_data_endpoints_and_the_cookie_endpoints_are_csrf_checked()
    {
        var (username, _, _) = await factory.SeedUserAsync();
        var auth = new AuthClient(factory.CreateClient());
        var (_, cookie, _) = await auth.SignInExpectingSuccessAsync(username, ApiFactory.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, (await auth.MeAsync(null, cookie)).StatusCode);

        var noHeader = await auth.RefreshAsync(cookie, withHeader: false);
        Assert.Equal(HttpStatusCode.Forbidden, noHeader.StatusCode);
        Assert.Equal(ProblemCodes.CsrfRejected, await AuthClient.CodeOf(noHeader));

        var crossSite = await auth.RefreshAsync(cookie, origin: "https://evil.example", fetchSite: "cross-site");
        Assert.Equal(HttpStatusCode.Forbidden, crossSite.StatusCode);

        var noCookieNoChecks = await auth.RefreshAsync(null, withHeader: false, origin: null, fetchSite: null);
        Assert.Equal(HttpStatusCode.Unauthorized, noCookieNoChecks.StatusCode);   // machine path: no cookie → no CSRF checks → token missing

        var stillValid = await auth.RefreshAsync(cookie);
        Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);
    }

    [DatabaseFact]
    public async Task Sign_out_revokes_the_family_and_clears_the_cookie()
    {
        var (username, _, _) = await factory.SeedUserAsync();
        var auth = new AuthClient(factory.CreateClient());
        var (_, cookie, _) = await auth.SignInExpectingSuccessAsync(username, ApiFactory.Password);

        var signedOut = await auth.SignOutAsync(cookie);
        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
        Assert.Contains(signedOut.Headers.GetValues("Set-Cookie"), v => v.StartsWith("__Secure-rt=;", StringComparison.Ordinal) || v.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(HttpStatusCode.Unauthorized, (await auth.RefreshAsync(cookie)).StatusCode);
    }

    [DatabaseFact]
    public async Task The_cookie_of_a_signed_out_or_expired_session_is_refused_without_the_reuse_alarm_R2_06()
    {
        // Round 2 R2-06: a sign-out response cut off by a page navigation leaves the cookie in the browser; its next refresh
        // was logged as REFRESH_REUSE_DETECTED (error). It is an ended session, not a theft.
        var (username, _, userId) = await factory.SeedUserAsync();
        var auth = new AuthClient(factory.CreateClient());
        var (_, cookie1, _) = await auth.SignInExpectingSuccessAsync(username, ApiFactory.Password);
        var rotated = await auth.RefreshAsync(cookie1);
        var cookie2 = AuthClient.CookieValue(rotated.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("__Secure-rt=", StringComparison.Ordinal)));
        Assert.Equal(HttpStatusCode.NoContent, (await auth.SignOutAsync(cookie2)).StatusCode);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var stale = await auth.RefreshAsync(cookie2);
            Assert.Equal((HttpStatusCode.Unauthorized, ProblemCodes.RefreshTokenRejected), (stale.StatusCode, await AuthClient.CodeOf(stale)));
        }

        var events = await SecurityEventsOfAsync(userId);
        Assert.DoesNotContain(events, row => row.Name == "REFRESH_REUSE_DETECTED");
        Assert.Equal(2, events.Count(row => row == ("SESSION_CONTEXT_CHANGED", "info", "refresh_after_logout")));
        Assert.All(await RevocationReasonsOfAsync(userId), reason => Assert.Equal("Logout", reason));   // the family is left as sign-out left it

        // The same for a session that expired: refused at expiry, then refused again quietly.
        var (otherName, _, otherId) = await factory.SeedUserAsync();
        var (_, otherCookie, _) = await auth.SignInExpectingSuccessAsync(otherName, ApiFactory.Password);
        factory.Clock.Advance(TimeSpan.FromHours(9));
        try
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await auth.RefreshAsync(otherCookie)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await auth.RefreshAsync(otherCookie)).StatusCode);
        }
        finally
        {
            factory.Clock.Advance(TimeSpan.FromHours(-9));
        }

        var otherEvents = await SecurityEventsOfAsync(otherId);
        Assert.DoesNotContain(otherEvents, row => row.Name == "REFRESH_REUSE_DETECTED");
        Assert.Contains(otherEvents, row => row == ("SESSION_CONTEXT_CHANGED", "info", "refresh_after_expired"));
        Assert.Equal("Expired", Assert.Single(await RevocationReasonsOfAsync(otherId)));
    }

    [DatabaseFact]
    public async Task A_rotated_token_presented_after_the_grace_is_still_reuse_with_the_alarm_and_the_family_revoked_R2_06()
    {
        var (username, _, userId) = await factory.SeedUserAsync();
        var auth = new AuthClient(factory.CreateClient());
        var (_, cookie1, _) = await auth.SignInExpectingSuccessAsync(username, ApiFactory.Password);
        var rotated = await auth.RefreshAsync(cookie1);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);

        factory.Clock.Advance(GraceElapsed);
        try
        {
            var reuse = await auth.RefreshAsync(cookie1);
            Assert.Equal((HttpStatusCode.Unauthorized, ProblemCodes.RefreshTokenRejected), (reuse.StatusCode, await AuthClient.CodeOf(reuse)));
        }
        finally
        {
            factory.Clock.Advance(-GraceElapsed);
        }

        var events = await SecurityEventsOfAsync(userId);
        Assert.Contains(events, row => row.Name == "REFRESH_REUSE_DETECTED" && row.Severity == "error");
        Assert.DoesNotContain(events, row => row.Detail is not null && row.Detail.StartsWith("refresh_after_", StringComparison.Ordinal));
        var reasons = await RevocationReasonsOfAsync(userId);
        Assert.Equal(2, reasons.Count);
        Assert.All(reasons, reason => Assert.Equal("ReuseDetected", reason));
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

    [DatabaseFact]
    public async Task Sign_out_honours_only_the_genuine_cookie_and_answers_the_same_for_a_forged_or_stale_one()
    {
        var (victimName, _, _) = await factory.SeedUserAsync();
        var (attackerName, _, _) = await factory.SeedUserAsync();
        var victim = new AuthClient(factory.CreateClient());
        var attacker = new AuthClient(factory.CreateClient());
        var (_, victimCookie, _) = await victim.SignInExpectingSuccessAsync(victimName, ApiFactory.Password);
        var (_, attackerCookie, _) = await attacker.SignInExpectingSuccessAsync(attackerName, ApiFactory.Password);

        // the victim's session id with the attacker's own secret: 204 and a cleared cookie, but nothing ends
        var dot = victimCookie.IndexOf('.', StringComparison.Ordinal);
        var forged = string.Concat(victimCookie.AsSpan(0, dot + 1), attackerCookie.AsSpan(attackerCookie.IndexOf('.', StringComparison.Ordinal) + 1));
        var forgedOut = await attacker.SignOutAsync(forged);
        Assert.Equal(HttpStatusCode.NoContent, forgedOut.StatusCode);
        Assert.Contains(forgedOut.Headers.GetValues("Set-Cookie"), v => v.StartsWith("__Secure-rt=;", StringComparison.Ordinal) || v.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));
        var refreshed = await victim.RefreshAsync(victimCookie);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var current = AuthClient.CookieValue(refreshed.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("__Secure-rt=", StringComparison.Ordinal)));

        // a stale cookie (already rotated) ends nothing either
        Assert.Equal(HttpStatusCode.NoContent, (await victim.SignOutAsync(victimCookie)).StatusCode);
        var stillLive = await victim.RefreshAsync(current);
        Assert.Equal(HttpStatusCode.OK, stillLive.StatusCode);
        current = AuthClient.CookieValue(stillLive.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("__Secure-rt=", StringComparison.Ordinal)));

        // the genuine cookie ends the session; the attacker's own session was never touched
        Assert.Equal(HttpStatusCode.NoContent, (await victim.SignOutAsync(current)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await victim.RefreshAsync(current)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await attacker.RefreshAsync(attackerCookie)).StatusCode);
    }

    [DatabaseFact]
    public async Task A_sign_in_ends_the_session_of_the_cookie_the_browser_presented_whoever_it_belonged_to()
    {
        // R2-04: tab A's session (user X) is still held by the browser's cookie when tab B signs in as user Y.
        var (first, _, _) = await factory.SeedUserAsync();
        var (second, _, _) = await factory.SeedUserAsync();
        var auth = new AuthClient(factory.CreateClient());
        var (previous, previousCookie, _) = await auth.SignInExpectingSuccessAsync(first, ApiFactory.Password);

        var (_, newCookie, _) = await auth.SignInExpectingSuccessAsync(second, ApiFactory.Password, cookie: previousCookie);

        Assert.NotEqual(previousCookie, newCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await auth.RefreshAsync(previousCookie)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await auth.RefreshAsync(newCookie)).StatusCode);
        factory.Clock.Advance(TimeSpan.FromSeconds(11));   // past the principal cache: the previous access token is dead too
        Assert.Equal(HttpStatusCode.Unauthorized, (await auth.MeAsync(previous.AccessToken)).StatusCode);
        factory.Clock.Advance(TimeSpan.FromSeconds(-11));
    }

    [DatabaseFact]
    public async Task A_sign_in_ends_nothing_for_a_forged_cookie_or_a_refused_password()
    {
        var (username, _, _) = await factory.SeedUserAsync();
        var auth = new AuthClient(factory.CreateClient());
        var (_, cookie, _) = await auth.SignInExpectingSuccessAsync(username, ApiFactory.Password);

        // the session id of someone else's cookie with a secret that is not its own revokes nothing
        var dot = cookie.IndexOf('.', StringComparison.Ordinal);
        var forged = string.Concat(cookie.AsSpan(0, dot + 1), new string('A', cookie.Length - dot - 1));
        await auth.SignInExpectingSuccessAsync(username, ApiFactory.Password, cookie: forged);
        var stillLive = await auth.RefreshAsync(cookie);
        Assert.Equal(HttpStatusCode.OK, stillLive.StatusCode);
        var rotated = AuthClient.CookieValue(stillLive.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("__Secure-rt=", StringComparison.Ordinal)));

        // a refused sign-in leaves the browser's session as it was
        Assert.Equal(HttpStatusCode.Unauthorized, (await auth.SignInAsync(username, "not-the-password", cookie: rotated)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await auth.RefreshAsync(rotated)).StatusCode);
    }

    [DatabaseFact]
    public async Task An_account_locked_by_an_administrator_is_refused_with_its_own_code()
    {
        // Validation 2026-10-11 B-11: AUTH-015 (contact your administrator), not the automatic lockout's AUTH-003 (try again later).
        var (username, _, userId) = await factory.SeedUserAsync();
        await using (var connection = new Npgsql.NpgsqlConnection(TestDatabase.OwnerConnection))
        {
            await connection.OpenAsync();
            await using var command = new Npgsql.NpgsqlCommand("UPDATE identity.platform_users SET is_locked_out = true, lockout_end_utc = NULL WHERE id = @id", connection);
            command.Parameters.AddWithValue("id", userId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        Assert.True((await factory.LoadUserAsync(userId)).IsLockedByAdministrator);
        var auth = new AuthClient(factory.CreateClient());
        var locked = await auth.SignInAsync(username, ApiFactory.Password);
        Assert.Equal((HttpStatusCode.Unauthorized, ProblemCodes.AccountLockedByAdministrator), (locked.StatusCode, await AuthClient.CodeOf(locked)));

        // the same refusal whatever the password, and still AUTH-002 for an unknown username: nothing more is revealed
        var wrong = await auth.SignInAsync(username, "not the password");
        Assert.Equal((HttpStatusCode.Unauthorized, ProblemCodes.AccountLockedByAdministrator), (wrong.StatusCode, await AuthClient.CodeOf(wrong)));
        var unknown = await auth.SignInAsync("nobody." + Guid.CreateVersion7().ToString("N")[..8], ApiFactory.Password);
        Assert.Equal((HttpStatusCode.Unauthorized, ProblemCodes.InvalidCredentials), (unknown.StatusCode, await AuthClient.CodeOf(unknown)));
    }

    [DatabaseFact]
    public async Task Five_wrong_passwords_lock_the_account_and_the_right_one_is_then_refused()
    {
        var (username, _, userId) = await factory.SeedUserAsync();
        var auth = new AuthClient(factory.CreateClient());

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var wrong = await auth.SignInAsync(username, "wrong-" + attempt);
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            Assert.Equal(ProblemCodes.InvalidCredentials, await AuthClient.CodeOf(wrong));
        }

        Assert.True((await factory.LoadUserAsync(userId)).IsLockedOut);
        var locked = await auth.SignInAsync(username, ApiFactory.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Equal(ProblemCodes.AccountLocked, await AuthClient.CodeOf(locked));

        factory.Clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(HttpStatusCode.OK, (await auth.SignInAsync(username, ApiFactory.Password)).StatusCode);
        factory.Clock.Advance(TimeSpan.FromMinutes(-31));
    }

    [DatabaseFact]
    public async Task An_expired_session_is_refused_with_its_code_once_the_cache_lapses()
    {
        var (username, _, _) = await factory.SeedUserAsync();
        var auth = new AuthClient(factory.CreateClient());
        var (body, _, _) = await auth.SignInExpectingSuccessAsync(username, ApiFactory.Password);

        factory.Clock.Advance(TimeSpan.FromHours(9));
        var me = await auth.MeAsync(body.AccessToken);
        factory.Clock.Advance(TimeSpan.FromHours(-9));

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(ProblemCodes.SessionRevokedOrExpired, await AuthClient.CodeOf(me));
    }

    [DatabaseFact]
    public async Task Recovery_by_mobile_finds_the_account_whichever_spelling_was_stored_or_typed()
    {
        // C-03: rows saved before the E.164 rule hold the national form; the lookup matches every spelling of the number
        await SeedEgyptLikeCountryAsync();
        var legacyNational = NewEgyptianNational();
        var currentNational = NewEgyptianNational();
        await factory.SeedUserAsync(mobile: "0" + legacyNational);
        await factory.SeedUserAsync(mobile: "+20" + currentNational);
        var auth = new AuthClient(factory.CreateClient());

        var byE164 = await auth.Http.PostAsJsonAsync("/api/v1/auth/recovery/start", new StartPasswordRecoveryRequest("+20" + legacyNational, "en"));
        Assert.Equal(HttpStatusCode.Accepted, byE164.StatusCode);
        Assert.NotNull(factory.Notifications.LastTo("0" + legacyNational));

        var byNational = await auth.Http.PostAsJsonAsync("/api/v1/auth/recovery/start", new StartPasswordRecoveryRequest("0" + currentNational, "en"));
        Assert.Equal(HttpStatusCode.Accepted, byNational.StatusCode);
        Assert.NotNull(factory.Notifications.LastTo("+20" + currentNational));
    }

    private static string NewEgyptianNational() =>
        "10" + Random.Shared.NextInt64(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>An active country with the dial code +20 and 10 national digits (ISO codes J? are this class's own).</summary>
    private async Task SeedEgyptLikeCountryAsync()
    {
        var n = Interlocked.Increment(ref _isoCounter);
        var iso = "J" + (char)('A' + (n % 26));
        using var scope = factory.Services.CreateScope();
        var country = Platform.Domain.PlatformAdmin.Country.Create(new Platform.Domain.PlatformAdmin.CountryData("Mobileland " + iso, "أرض " + iso, iso, "+20", "Pound", "EGP", "ar", "Africa/Cairo", 10, null, null));
        scope.ServiceProvider.GetRequiredService<Platform.Application.PlatformAdmin.ICountryRepository>().Add(country);
        await scope.ServiceProvider.GetRequiredService<Platform.Application.Abstractions.Persistence.IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    private static HttpResponseMessage Assert400(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return response;
    }
}
