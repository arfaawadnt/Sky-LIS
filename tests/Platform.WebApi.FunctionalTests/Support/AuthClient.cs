using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Platform.Contracts.Identity;

namespace Platform.WebApi.FunctionalTests.Support;

/// <summary>Seals a credential exactly as the browser does (02 §3.4, §3.5): compact JWE, RSA-OAEP-256 wrapped CEK, A256GCM with the header as AAD.</summary>
internal static class TestSealer
{
    public static string Seal(CredentialChallengeResponse challenge, string field, string value, string audience, string? nonceOverride = null)
    {
        var header = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { alg = "RSA-OAEP-256", enc = "A256GCM", kid = challenge.Kid })));

        using var rsa = RSA.Create(new RSAParameters
        {
            Modulus = Base64UrlEncoder.DecodeBytes(challenge.PublicKey.N),
            Exponent = Base64UrlEncoder.DecodeBytes(challenge.PublicKey.E),
        });
        var cek = RandomNumberGenerator.GetBytes(32);
        var wrapped = rsa.Encrypt(cek, RSAEncryptionPadding.OaepSHA256);
        var iv = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { v = value, f = field, aud = audience, n = nonceOverride ?? challenge.Nonce }));
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(cek, 16))
        {
            aes.Encrypt(iv, plaintext, ciphertext, tag, Encoding.ASCII.GetBytes(header));
        }

        return string.Join('.', header, Base64UrlEncoder.Encode(wrapped), Base64UrlEncoder.Encode(iv), Base64UrlEncoder.Encode(ciphertext), Base64UrlEncoder.Encode(tag));
    }
}

/// <summary>Drives the auth endpoints the way the SPA does. Cookies are handled by hand so the Secure flag can be inspected over the test server.</summary>
internal sealed class AuthClient(HttpClient http)
{
    public const string SignInPath = "/api/v1/auth/sign-in";
    public const string RefreshPath = "/api/v1/auth/refresh";
    public const string SignOutPath = "/api/v1/auth/sign-out";
    public const string MePath = "/api/v1/auth/me";
    public const string ChallengePath = "/api/v1/auth/credential-challenge";

    public HttpClient Http { get; } = http;

    public async Task<CredentialChallengeResponse> ChallengeAsync()
    {
        var response = await Http.GetAsync(ChallengePath);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CredentialChallengeResponse>())!;
    }

    /// <param name="cookie">The refresh cookie the browser still holds, sent with the sign-in like a real browser does (R2-04).</param>
    public async Task<HttpResponseMessage> SignInRawAsync(string username, string sealedPassword, Guid? tenant = null, string? cookie = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, SignInPath)
        {
            Content = JsonContent.Create(new { tenant, username, password = sealedPassword }),
        };
        request.Headers.Add("X-Requested-With", "Platform");
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"__Secure-rt={cookie}");
        }

        return await Http.SendAsync(request);
    }

    public async Task<HttpResponseMessage> SignInAsync(string username, string password, Guid? tenant = null, string? cookie = null)
    {
        var challenge = await ChallengeAsync();
        return await SignInRawAsync(username, TestSealer.Seal(challenge, "password", password, "POST " + SignInPath), tenant, cookie);
    }

    public async Task<(AuthResponse Body, string Cookie, string SetCookie)> SignInExpectingSuccessAsync(string username, string password, Guid? tenant = null, string? cookie = null)
    {
        var response = await SignInAsync(username, password, tenant, cookie);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        var setCookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("__Secure-rt=", StringComparison.Ordinal));
        return (body, CookieValue(setCookie), setCookie);
    }

    public Task<HttpResponseMessage> RefreshAsync(string? cookie, bool withHeader = true, string? origin = "http://localhost", string? fetchSite = "same-origin") =>
        CookiePostAsync(RefreshPath, cookie, withHeader, origin, fetchSite);

    public Task<HttpResponseMessage> SignOutAsync(string? cookie) => CookiePostAsync(SignOutPath, cookie, true, "http://localhost", "same-origin");

    public async Task<HttpResponseMessage> MeAsync(string? accessToken, string? cookie = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, MePath);
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"__Secure-rt={cookie}");
        }

        return await Http.SendAsync(request);
    }

    public static string CookieValue(string setCookie)
    {
        var start = setCookie.IndexOf('=', StringComparison.Ordinal) + 1;
        var end = setCookie.IndexOf(';', StringComparison.Ordinal);
        return setCookie[start..(end < 0 ? setCookie.Length : end)];
    }

    public static async Task<string> CodeOf(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString()!;
    }

    private async Task<HttpResponseMessage> CookiePostAsync(string path, string? cookie, bool withHeader, string? origin, string? fetchSite)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"__Secure-rt={cookie}");
        }

        if (withHeader)
        {
            request.Headers.Add("X-Requested-With", "Platform");
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        if (fetchSite is not null)
        {
            request.Headers.Add("Sec-Fetch-Site", fetchSite);
        }

        return await Http.SendAsync(request);
    }
}
