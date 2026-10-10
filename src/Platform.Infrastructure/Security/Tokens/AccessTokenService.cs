using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Platform.Application.Abstractions.Tokens;
using Platform.Application.Identity;
using Platform.Application.Identity.Commands;
using Platform.SharedKernel.Abstractions;

namespace Platform.Infrastructure.Security.Tokens;

/// <summary>
/// Mints the access token of 02 §4.2: header <c>{alg: ES256, typ: at+jwt, kid}</c>; claims <c>iss aud sub sid tid
/// scope jti iat nbf exp</c> with <c>exp = iat + 15 min</c>. <c>email</c>, <c>name</c>, <c>role</c> and permissions
/// are absent by design (D-02, pitfall P-21): the server resolves them per request from the session.
/// </summary>
public sealed class AccessTokenService(SigningKeys keys, ISubjectProtector subjects, SecurityOptions options, SessionPolicy policy, IClock clock) : IAccessTokenService
{
    public const string TokenType = "at+jwt";
    private static readonly JsonWebTokenHandler Handler = new() { SetDefaultTimesOnTokenCreation = false };

    public IssuedAccessToken Issue(AccessTokenSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var now = clock.UtcNow;
        var expires = now + policy.AccessTokenLifetime;
        var jti = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(16));

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = subjects.Protect(subject.UserId, keys.KeyId),
            ["sid"] = subject.SessionId.ToString(),
            ["scope"] = ScopeNames.Of(subject.Scope),
            [JwtRegisteredClaimNames.Jti] = jti,
        };

        if (subject.TenantId is { } tenantId)
        {
            claims["tid"] = tenantId.ToString();
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            TokenType = TokenType,
            SigningCredentials = keys.SigningCredentials,
            Claims = claims,
        };

        return new IssuedAccessToken(Handler.CreateToken(descriptor), expires, jti);
    }
}

/// <summary>Refresh token format of 02 §4.3: <c>"&lt;sessionId&gt;.&lt;base64url 32 random bytes&gt;"</c>; the store keeps SHA-256 of the secret only.</summary>
public sealed class RefreshTokenService : IRefreshTokenService
{
    public const int SecretLength = 32;
    private const int MaxSecretTextLength = 64;

    public RefreshSecret NewSecret()
    {
        var secret = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(SecretLength));
        return new RefreshSecret(secret, HashOf(secret));
    }

    public string Format(Guid sessionId, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        return $"{sessionId:N}.{secret}";
    }

    public bool TryParse(string? token, out Guid sessionId, out byte[] hash)
    {
        sessionId = Guid.Empty;
        hash = [];
        if (string.IsNullOrEmpty(token) || token.Length > 32 + 1 + MaxSecretTextLength)
        {
            return false;
        }

        var dot = token.IndexOf('.', StringComparison.Ordinal);
        if (dot != 32 || dot == token.Length - 1 || !Guid.TryParseExact(token.AsSpan(0, 32), "N", out sessionId))
        {
            return false;
        }

        var secret = token[(dot + 1)..];
        if (secret.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
        {
            return false;
        }

        hash = HashOf(secret);
        return true;
    }

    private static byte[] HashOf(string secret) => SHA256.HashData(Encoding.ASCII.GetBytes(secret));
}
