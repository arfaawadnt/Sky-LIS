using System.Security.Cryptography;
using System.Text;

namespace Platform.Infrastructure.Security.Protection;

/// <summary>
/// Protects secrets at rest (envelope private keys, nonce MAC keys, later MFA secrets) with the master key from the
/// secret store (02 §3.7, §6.2): a purpose-bound subkey derived with HKDF-SHA256, then AES-256-GCM with a fresh
/// 96-bit nonce. Output: <c>version ‖ nonce ‖ tag ‖ ciphertext</c>. Purpose strings keep one secret from being
/// replayed as another.
/// </summary>
public sealed class MasterKeyProtector
{
    private const byte Version = 1;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private readonly byte[] _masterKey;

    public MasterKeyProtector(string masterKeyBase64)
    {
        if (!ConfigGuard.IsKey(masterKeyBase64))
        {
            throw new SecurityConfigurationException(["DataProtection:MasterKey must be 32 random bytes, base64"]);
        }

        _masterKey = Convert.FromBase64String(masterKeyBase64);
    }

    public byte[] Protect(string purpose, ReadOnlySpan<byte> plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var output = new byte[1 + NonceLength + TagLength + plaintext.Length];
        output[0] = Version;
        nonce.CopyTo(output, 1);

        using var aes = new AesGcm(DeriveKey(purpose), TagLength);
        aes.Encrypt(nonce, plaintext, output.AsSpan(1 + NonceLength + TagLength), output.AsSpan(1 + NonceLength, TagLength), Encoding.UTF8.GetBytes(purpose));
        return output;
    }

    public byte[] Unprotect(string purpose, ReadOnlySpan<byte> protectedBytes)
    {
        if (protectedBytes.Length < 1 + NonceLength + TagLength || protectedBytes[0] != Version)
        {
            throw new CryptographicException("The protected payload is malformed.");
        }

        var plaintext = new byte[protectedBytes.Length - 1 - NonceLength - TagLength];
        using var aes = new AesGcm(DeriveKey(purpose), TagLength);
        aes.Decrypt(
            protectedBytes.Slice(1, NonceLength),
            protectedBytes[(1 + NonceLength + TagLength)..],
            protectedBytes.Slice(1 + NonceLength, TagLength),
            plaintext,
            Encoding.UTF8.GetBytes(purpose));
        return plaintext;
    }

    private byte[] DeriveKey(string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, _masterKey, SecurityOptions.KeyLength, salt: null, info: Encoding.UTF8.GetBytes("platform:protect:v1:" + purpose));
    }
}
