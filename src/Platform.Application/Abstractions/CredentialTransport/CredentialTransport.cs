namespace Platform.Application.Abstractions.CredentialTransport;

/// <summary>A challenge (02 §3.3): the active envelope key and a MAC'd, 120-second nonce. Stateless to issue, consumed once on use.</summary>
public sealed record CredentialChallenge(
    string Kid,
    string PublicKeyModulus,
    string PublicKeyExponent,
    string PublicKeyPem,
    string Nonce,
    DateTimeOffset ExpiresAtUtc)
{
    public const string Algorithm = "RSA-OAEP-256";
    public const string Encryption = "A256GCM";
}

public interface ICredentialChallengeIssuer
{
    Task<CredentialChallenge> IssueAsync(CancellationToken cancellationToken);
}

/// <summary>The outcome of opening one envelope: the plaintext, or the <c>CRED-nnn</c> refusal (02 §3.6).</summary>
public sealed record EnvelopeOpenResult(bool Ok, string? Plaintext, string Code, string Title)
{
    public static EnvelopeOpenResult Success(string plaintext) => new(true, plaintext, string.Empty, string.Empty);

    public static EnvelopeOpenResult Refused(string code, string title) => new(false, null, code, title);
}

/// <summary>Opens a compact JWE sealed by the browser (RSA-OAEP-256 + A256GCM) and enforces nonce, audience and field binding.</summary>
public interface ICredentialEnvelopeOpener
{
    Task<EnvelopeOpenResult> OpenAsync(string envelope, string expectedField, string expectedAudience, CancellationToken cancellationToken);

    /// <summary>
    /// Opens one field of a request whose other fields share the same challenge (one challenge per request, the same
    /// nonce on every field, 02 §3.4). <paramref name="requestNonces"/> holds the nonces this request already consumed:
    /// a nonce is consumed once per request, so a second field of the same request opens, while any other request
    /// presenting it is still refused as a replay.
    /// </summary>
    Task<EnvelopeOpenResult> OpenAsync(string envelope, string expectedField, string expectedAudience, ISet<string> requestNonces, CancellationToken cancellationToken);
}
