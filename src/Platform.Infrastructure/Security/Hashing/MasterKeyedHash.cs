using System.Security.Cryptography;
using System.Text;

namespace Platform.Infrastructure.Security.Hashing;

/// <summary>
/// Deterministic, keyed digests for lookups that must not reveal their input (one-time code targets and codes):
/// HMAC-SHA256 under a purpose-bound subkey derived from the master key with HKDF-SHA256. Purpose strings keep a
/// digest made for one use from matching another; the master key itself never touches the data.
/// </summary>
public sealed class MasterKeyedHash
{
    private readonly byte[] _masterKey;

    public MasterKeyedHash(SecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!ConfigGuard.IsKey(options.MasterKey))
        {
            throw new SecurityConfigurationException(["DataProtection:MasterKey must be 32 random bytes, base64"]);
        }

        _masterKey = Convert.FromBase64String(options.MasterKey);
    }

    public byte[] Compute(string purpose, string data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentNullException.ThrowIfNull(data);
        var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, _masterKey, SecurityOptions.KeyLength, salt: null, info: Encoding.UTF8.GetBytes("platform:mac:v1:" + purpose));
        return HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));
    }
}
