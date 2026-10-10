using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Platform.Infrastructure.Security.Tokens;

/// <summary>
/// The ES256 signing key pair (C-04): the private key signs access tokens, the public key validates them and is
/// published at <c>/.well-known/jwks.json</c>. The key id is the configured one or the first 8 hex characters of
/// SHA-256 over the public key, so a rotated key always gets a new id.
/// </summary>
public sealed class SigningKeys : IDisposable
{
    public const string Algorithm = SecurityAlgorithms.EcdsaSha256;
    private readonly ECDsa _privateKey;
    private readonly ECDsa _publicKey;

    public SigningKeys(SecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!ConfigGuard.IsP256PrivateKey(options.SigningKeyPem))
        {
            throw new SecurityConfigurationException(["Jwt:SigningKey must be a P-256 (ES256) private key in PEM"]);
        }

        _privateKey = ECDsa.Create();
        _privateKey.ImportFromPem(options.SigningKeyPem);

        var publicParameters = _privateKey.ExportParameters(includePrivateParameters: false);
        _publicKey = ECDsa.Create(publicParameters);

        KeyId = string.IsNullOrWhiteSpace(options.SigningKeyId)
            ? Convert.ToHexString(SHA256.HashData(_publicKey.ExportSubjectPublicKeyInfo()))[..8].ToLowerInvariant()
            : options.SigningKeyId.Trim();

        // Each key set gets its own provider cache. The library's default factory caches signers by key identity
        // (derived from the public key), so a second instance of the same key — a rotation overlap, or several
        // hosts in one process — would otherwise sign with an earlier instance's ECDsa after it was disposed.
        var providers = new CryptoProviderFactory();
        SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(_privateKey) { KeyId = KeyId, CryptoProviderFactory = providers }, Algorithm);
        ValidationKey = new ECDsaSecurityKey(_publicKey) { KeyId = KeyId, CryptoProviderFactory = providers };

        var x = Base64UrlEncoder.Encode(publicParameters.Q.X);
        var y = Base64UrlEncoder.Encode(publicParameters.Q.Y);
        PublicJwk = new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = x,
            ["y"] = y,
            ["kid"] = KeyId,
            ["use"] = "sig",
            ["alg"] = "ES256",
        };
    }

    public string KeyId { get; }

    public SigningCredentials SigningCredentials { get; }

    public ECDsaSecurityKey ValidationKey { get; }

    /// <summary>The public key as a JWK document entry (RFC 7517).</summary>
    public IReadOnlyDictionary<string, string> PublicJwk { get; }

    public void Dispose()
    {
        _privateKey.Dispose();
        _publicKey.Dispose();
    }
}
