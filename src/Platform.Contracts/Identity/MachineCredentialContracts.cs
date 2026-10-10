using Platform.Contracts.Common;

namespace Platform.Contracts.Identity;

// Machine credentials (02 §4.2, §6.3; intake LIS-INT-INT-001). Every secret-bearing property carries [CredentialField]:
// in a request the edge filter opens its credential envelope (D-03); in a response it marks material shown once.

/// <summary><c>POST /api/v1/auth/token</c>: the client id and the client secret, sealed against a credential challenge like any credential.</summary>
public sealed record MachineTokenRequest
{
    public string ClientId { get; init; } = string.Empty;

    [CredentialField]
    public string ClientSecret { get; init; } = string.Empty;
}

/// <summary><c>POST /api/v1/auth/token/refresh</c>: the refresh token of a machine session, in the body (never a cookie).</summary>
public sealed record MachineTokenRefreshRequest(string? RefreshToken);

/// <summary>An access token (15 min, bearer) and the rotated refresh token the client stores; <paramref name="TokenType"/> is always <c>Bearer</c>.</summary>
public sealed record MachineTokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc, string RefreshToken, string TokenType, IReadOnlyList<string> Scopes);

/// <summary>Issue a credential with the given permission keys (each held by the caller); expiry defaults to one year, at most two.</summary>
public sealed record CreateMachineCredentialRequest(string Name, IReadOnlyList<string> Scopes, DateTimeOffset? ExpiresAtUtc);

public sealed record RevokeMachineCredentialRequest(string? Reason);

/// <summary>The only response that carries the secret: after creation or rotation. The client must store it now.</summary>
public sealed record MachineCredentialSecretResponse
{
    public Guid Id { get; init; }

    public string ClientId { get; init; } = string.Empty;

    [CredentialField]
    public string ClientSecret { get; init; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; init; }
}

/// <summary>Status names: Live, Expired, Revoked.</summary>
public sealed record MachineCredentialListItem(
    Guid Id,
    string Name,
    string ClientId,
    IReadOnlyList<string> Scopes,
    Guid? BranchId,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? LastUsedAtUtc,
    DateTimeOffset? SecretRotatedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    string? RevocationReason);
