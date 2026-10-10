using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
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

        var reuse = await auth.RefreshAsync(cookie1);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        Assert.Equal(ProblemCodes.RefreshTokenRejected, await AuthClient.CodeOf(reuse));

        var collateral = await auth.RefreshAsync(cookie2);
        Assert.Equal(HttpStatusCode.Unauthorized, collateral.StatusCode);

        factory.Clock.Advance(TimeSpan.FromSeconds(11));   // past the principal cache
        Assert.Equal(HttpStatusCode.Unauthorized, (await auth.MeAsync(second.AccessToken)).StatusCode);
        factory.Clock.Advance(TimeSpan.FromSeconds(-11));
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

    private static HttpResponseMessage Assert400(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return response;
    }
}
