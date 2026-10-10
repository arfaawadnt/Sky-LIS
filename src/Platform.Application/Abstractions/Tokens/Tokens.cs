using Platform.Domain.Identity;

namespace Platform.Application.Abstractions.Tokens;

/// <summary>What an access token asserts (02 §4.2): the user (encrypted into <c>sub</c>), the session, the tenant and the scope. Nothing else.</summary>
public sealed record AccessTokenSubject(Guid UserId, Guid SessionId, Guid? TenantId, SessionScope Scope);

public sealed record IssuedAccessToken(string Token, DateTimeOffset ExpiresAtUtc, string Jti);

/// <summary>Mints ES256 access tokens with a 15-minute lifetime (D-01) and an encrypted subject (D-02).</summary>
public interface IAccessTokenService
{
    IssuedAccessToken Issue(AccessTokenSubject subject);
}

/// <summary>AES-256-GCM over the user id (02 §4.6, D-02): a fresh nonce per token, bound to the signing key epoch through the AAD.</summary>
public interface ISubjectProtector
{
    string ActiveKeyId { get; }

    /// <summary><c>v1.&lt;kid&gt;.&lt;base64url(nonce ‖ ciphertext ‖ tag)&gt;</c>; different for every call.</summary>
    string Protect(Guid userId, string signingKeyId);

    /// <summary>The user id, or <see langword="null"/> for a plaintext, tampered or unknown-key subject.</summary>
    Guid? Unprotect(string subject, string signingKeyId);
}

/// <summary>The refresh secret the browser holds in the cookie or a machine client stores; only its hash is persisted (02 §4.3).</summary>
public sealed record RefreshSecret(string Secret, byte[] Hash);

public interface IRefreshTokenService
{
    RefreshSecret NewSecret();

    /// <summary><c>"&lt;sessionId&gt;.&lt;secret&gt;"</c>.</summary>
    string Format(Guid sessionId, string secret);

    /// <summary>Splits a presented token into the session id and the hash of its secret; false for anything malformed.</summary>
    bool TryParse(string? token, out Guid sessionId, out byte[] hash);
}
