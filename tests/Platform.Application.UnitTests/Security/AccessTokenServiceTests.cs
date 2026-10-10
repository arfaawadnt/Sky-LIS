using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Platform.Application.Abstractions.Tokens;
using Platform.Application.UnitTests.Support;
using Platform.Domain.Identity;
using Platform.Infrastructure.Security.Tokens;

namespace Platform.Application.UnitTests.Security;

/// <summary>AccessTokenClaimsTests (01 §8, D-01, D-02): three segments, ES256, exact claim set, encrypted and unlinkable sub, 15-minute lifetime.</summary>
public sealed class AccessTokenServiceTests
{
    private static readonly string[] ExpectedClaims = ["aud", "exp", "iat", "iss", "jti", "nbf", "scope", "sid", "sub", "tid"];
    private static readonly string[] ForbiddenClaims = ["email", "name", "role", "roles", "permissions", "given_name", "family_name", "upn", "preferred_username"];

    [Fact]
    public async Task The_token_carries_only_the_specified_claims_and_validates_with_the_public_key()
    {
        var options = TestKeys.ValidOptions();
        using var keys = new SigningKeys(options);
        var protector = new SubjectProtector(options);
        var clock = new FixedClock(FixedClock.Instant);
        var service = new AccessTokenService(keys, protector, options, TestKeys.DefaultPolicy(), clock);
        var subject = new AccessTokenSubject(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), SessionScope.Full);

        var issued = service.Issue(subject);
        var again = service.Issue(subject);

        var parts = issued.Token.Split('.');
        Assert.Equal(3, parts.Length);

        var header = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[0])).RootElement;
        Assert.Equal("ES256", header.GetProperty("alg").GetString());
        Assert.Equal("at+jwt", header.GetProperty("typ").GetString());
        Assert.Equal(keys.KeyId, header.GetProperty("kid").GetString());

        var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[1])).RootElement;
        var names = payload.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.Equal(ExpectedClaims, names);
        Assert.DoesNotContain(names, name => ForbiddenClaims.Contains(name));

        var sub = payload.GetProperty("sub").GetString()!;
        Assert.StartsWith("v1.", sub, StringComparison.Ordinal);
        Assert.Equal(subject.UserId, protector.Unprotect(sub, keys.KeyId));
        Assert.NotEqual(sub, JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(again.Token.Split('.')[1])).RootElement.GetProperty("sub").GetString());

        Assert.Equal(subject.SessionId.ToString(), payload.GetProperty("sid").GetString());
        Assert.Equal(subject.TenantId.ToString(), payload.GetProperty("tid").GetString());
        Assert.Equal("full", payload.GetProperty("scope").GetString());
        Assert.Equal(900, payload.GetProperty("exp").GetInt64() - payload.GetProperty("iat").GetInt64());
        Assert.Equal(FixedClock.Instant.AddMinutes(15), issued.ExpiresAtUtc);

        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(issued.Token, new TokenValidationParameters
        {
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKey = keys.ValidationKey,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            ValidTypes = ["at+jwt"],
            ValidateLifetime = false,
        });
        Assert.True(validation.IsValid, validation.Exception?.Message);
    }

    [Fact]
    public void A_platform_session_without_a_tenant_has_no_tid_claim()
    {
        var options = TestKeys.ValidOptions();
        using var keys = new SigningKeys(options);
        var service = new AccessTokenService(keys, new SubjectProtector(options), options, TestKeys.DefaultPolicy(), new FixedClock(FixedClock.Instant));

        var issued = service.Issue(new AccessTokenSubject(Guid.CreateVersion7(), Guid.CreateVersion7(), null, SessionScope.MfaEnrollment));

        var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(issued.Token.Split('.')[1])).RootElement;
        Assert.False(payload.TryGetProperty("tid", out _));
        Assert.Equal("mfa_enrollment", payload.GetProperty("scope").GetString());
    }

    [Fact]
    public void Refresh_tokens_round_trip_and_reject_malformed_input()
    {
        var service = new RefreshTokenService();
        var sessionId = Guid.CreateVersion7();
        var secret = service.NewSecret();

        var token = service.Format(sessionId, secret.Secret);

        Assert.True(service.TryParse(token, out var parsedId, out var hash));
        Assert.Equal(sessionId, parsedId);
        Assert.Equal(secret.Hash, hash);
        Assert.Equal(32, hash.Length);
        Assert.False(service.TryParse(null, out _, out _));
        Assert.False(service.TryParse("not-a-token", out _, out _));
        Assert.False(service.TryParse($"{sessionId:N}.", out _, out _));
        Assert.False(service.TryParse($"{sessionId:N}.bad secret!", out _, out _));
        Assert.NotEqual(secret.Secret, service.NewSecret().Secret);
    }
}
