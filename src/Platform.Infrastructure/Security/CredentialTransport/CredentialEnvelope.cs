using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;
using Platform.Application.Abstractions.CredentialTransport;
using Platform.Contracts.Common;
using Platform.SharedKernel.Abstractions;

namespace Platform.Infrastructure.Security.CredentialTransport;

/// <summary>
/// Issues challenges (02 §3.3): the active key as JWK and PEM, plus a nonce that is stateless to issue and stateful
/// to consume: <c>base64url(32 random bytes ‖ expiry unix seconds (8 bytes, big-endian) ‖ HMAC-SHA256(key, random ‖ expiry))</c>.
/// </summary>
public sealed class CredentialChallengeIssuer(EnvelopeKeyStore keys, SecurityOptions options, IClock clock) : ICredentialChallengeIssuer
{
    public const int RandomLength = 32;
    public const int ExpiryLength = 8;
    public const int MacLength = 32;
    public const int NonceLength = RandomLength + ExpiryLength + MacLength;

    public async Task<CredentialChallenge> IssueAsync(CancellationToken cancellationToken)
    {
        var key = await keys.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        var expiresAt = clock.UtcNow + options.ChallengeLifetime;

        var nonce = new byte[NonceLength];
        RandomNumberGenerator.Fill(nonce.AsSpan(0, RandomLength));
        BinaryPrimitives.WriteInt64BigEndian(nonce.AsSpan(RandomLength, ExpiryLength), expiresAt.ToUnixTimeSeconds());
        HMACSHA256.HashData(key.NonceMacKey, nonce.AsSpan(0, RandomLength + ExpiryLength), nonce.AsSpan(RandomLength + ExpiryLength, MacLength));

        using var rsa = key.OpenRsa();
        var publicParameters = rsa.ExportParameters(includePrivateParameters: false);

        return new CredentialChallenge(
            key.Kid,
            Base64UrlEncoder.Encode(publicParameters.Modulus),
            Base64UrlEncoder.Encode(publicParameters.Exponent),
            key.PublicKeyPem,
            Base64UrlEncoder.Encode(nonce),
            expiresAt);
    }

    /// <summary>Verifies a nonce's MAC and expiry against the key that issued it. Pure; the replay check is the store's.</summary>
    public static bool TryValidateNonce(string? nonceText, byte[] macKey, DateTimeOffset nowUtc, out byte[] nonceBytes, out DateTimeOffset expiresAtUtc)
    {
        nonceBytes = [];
        expiresAtUtc = default;
        if (string.IsNullOrEmpty(nonceText))
        {
            return false;
        }

        try
        {
            nonceBytes = Base64UrlEncoder.DecodeBytes(nonceText);
        }
        catch (FormatException)
        {
            return false;
        }

        if (nonceBytes.Length != NonceLength)
        {
            return false;
        }

        Span<byte> expectedMac = stackalloc byte[MacLength];
        HMACSHA256.HashData(macKey, nonceBytes.AsSpan(0, RandomLength + ExpiryLength), expectedMac);
        if (!CryptographicOperations.FixedTimeEquals(expectedMac, nonceBytes.AsSpan(RandomLength + ExpiryLength, MacLength)))
        {
            return false;
        }

        expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64BigEndian(nonceBytes.AsSpan(RandomLength, ExpiryLength)));
        return expiresAtUtc > nowUtc;
    }
}

/// <summary>
/// Opens one compact JWE (02 §3.4, §3.6) in the order the specification fixes, each step with its refusal code:
/// shape and header (CRED-001), key known (CRED-002), unwrap and AEAD (CRED-001), nonce MAC and expiry (CRED-002),
/// nonce consumed once (CRED-003), audience and field binding (CRED-004); a plaintext value is CRED-005. Nothing
/// here ever counts toward a lockout, and nothing is logged beyond the code.
/// </summary>
public sealed class CredentialEnvelopeOpener(EnvelopeKeyStore keys, NonceStore nonces, IClock clock) : ICredentialEnvelopeOpener
{
    public const string ExpectedAlgorithm = "RSA-OAEP-256";
    public const string ExpectedEncryption = "A256GCM";
    private const int IvLength = 12;
    private const int TagLength = 16;
    private const int CekLength = 32;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<EnvelopeOpenResult> OpenAsync(string envelope, string expectedField, string expectedAudience, CancellationToken cancellationToken) =>
        OpenAsync(envelope, expectedField, expectedAudience, new HashSet<string>(StringComparer.Ordinal), cancellationToken);

    public async Task<EnvelopeOpenResult> OpenAsync(string envelope, string expectedField, string expectedAudience, ISet<string> requestNonces, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestNonces);
        if (string.IsNullOrEmpty(envelope))
        {
            return Refuse(ProblemCodes.CredentialPlaintext, "A credential envelope is required.");
        }

        var parts = envelope.Split('.');
        if (parts.Length != 5)
        {
            return Refuse(ProblemCodes.CredentialPlaintext, "The credential must be sealed; plaintext credentials are refused.");
        }

        EnvelopeHeader? header;
        try
        {
            header = JsonSerializer.Deserialize<EnvelopeHeader>(Base64UrlEncoder.DecodeBytes(parts[0]), Json);
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            return Refuse(ProblemCodes.CredentialEnvelopeMalformed, "The credential envelope is malformed.");
        }

        if (header is null || header.Alg != ExpectedAlgorithm || header.Enc != ExpectedEncryption || string.IsNullOrEmpty(header.Kid))
        {
            return Refuse(ProblemCodes.CredentialEnvelopeMalformed, "The credential envelope is malformed.");
        }

        var key = await keys.FindAsync(header.Kid, cancellationToken).ConfigureAwait(false);
        if (key is null)
        {
            return Refuse(ProblemCodes.CredentialChallengeExpired, "The challenge key is unknown or has been rotated out; request a new challenge.");
        }

        byte[] plaintext;
        try
        {
            var wrappedKey = Base64UrlEncoder.DecodeBytes(parts[1]);
            var iv = Base64UrlEncoder.DecodeBytes(parts[2]);
            var ciphertext = Base64UrlEncoder.DecodeBytes(parts[3]);
            var tag = Base64UrlEncoder.DecodeBytes(parts[4]);
            if (iv.Length != IvLength || tag.Length != TagLength)
            {
                return Refuse(ProblemCodes.CredentialEnvelopeMalformed, "The credential envelope is malformed.");
            }

            byte[] cek;
            using (var rsa = key.OpenRsa())
            {
                cek = rsa.Decrypt(wrappedKey, RSAEncryptionPadding.OaepSHA256);
            }

            if (cek.Length != CekLength)
            {
                return Refuse(ProblemCodes.CredentialEnvelopeMalformed, "The credential envelope is malformed.");
            }

            plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(cek, TagLength);
            aes.Decrypt(iv, ciphertext, tag, plaintext, Encoding.ASCII.GetBytes(parts[0]));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return Refuse(ProblemCodes.CredentialEnvelopeMalformed, "The credential envelope could not be opened.");
        }

        EnvelopeBody? body;
        try
        {
            body = JsonSerializer.Deserialize<EnvelopeBody>(plaintext, Json);
        }
        catch (JsonException)
        {
            return Refuse(ProblemCodes.CredentialEnvelopeMalformed, "The credential envelope is malformed.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        if (body is null)
        {
            return Refuse(ProblemCodes.CredentialEnvelopeMalformed, "The credential envelope is malformed.");
        }

        if (!CredentialChallengeIssuer.TryValidateNonce(body.N, key.NonceMacKey, clock.UtcNow, out var nonceBytes, out var expiresAt))
        {
            return Refuse(ProblemCodes.CredentialChallengeExpired, "The challenge has expired; request a new one.");
        }

        var nonceHash = SHA256.HashData(nonceBytes);
        var nonceKey = Convert.ToHexString(nonceHash);
        if (!requestNonces.Contains(nonceKey))
        {
            if (!await nonces.TryConsumeAsync(nonceHash, expiresAt, cancellationToken).ConfigureAwait(false))
            {
                return Refuse(ProblemCodes.CredentialReplayed, "The challenge was already used; request a new one.");
            }

            requestNonces.Add(nonceKey);   // later fields of this same request share the challenge
        }

        if (!string.Equals(body.Aud, expectedAudience, StringComparison.Ordinal) || !string.Equals(body.F, expectedField, StringComparison.Ordinal))
        {
            return Refuse(ProblemCodes.CredentialRetargeted, "The credential envelope was sealed for another endpoint or field.");
        }

        return body.V is null
            ? Refuse(ProblemCodes.CredentialEnvelopeMalformed, "The credential envelope carries no value.")
            : EnvelopeOpenResult.Success(body.V);
    }

    private static EnvelopeOpenResult Refuse(string code, string title) => EnvelopeOpenResult.Refused(code, title);

    private sealed record EnvelopeHeader([property: JsonPropertyName("alg")] string? Alg, [property: JsonPropertyName("enc")] string? Enc, [property: JsonPropertyName("kid")] string? Kid);

    private sealed record EnvelopeBody([property: JsonPropertyName("v")] string? V, [property: JsonPropertyName("f")] string? F, [property: JsonPropertyName("aud")] string? Aud, [property: JsonPropertyName("n")] string? N);
}
