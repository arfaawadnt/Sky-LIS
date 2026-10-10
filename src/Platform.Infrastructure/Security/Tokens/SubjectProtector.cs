using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Platform.Application.Abstractions.Tokens;

namespace Platform.Infrastructure.Security.Tokens;

/// <summary>
/// The encrypted subject claim of 02 §4.6 (D-02): AES-256-GCM over the 16 bytes of the user id, a fresh 96-bit
/// nonce per token (so two tokens of one user never share a <c>sub</c>), AAD <c>platform:sub:v1 ‖ signing kid</c>
/// (binds the ciphertext to the token's key epoch), encoded <c>v1.&lt;kid&gt;.&lt;base64url(nonce ‖ ciphertext ‖ tag)&gt;</c>
/// where <c>kid</c> is the first 8 hex characters of SHA-256 over the key. Previous keys decrypt during an overlap;
/// new tokens always use the active key.
/// </summary>
public sealed class SubjectProtector : ISubjectProtector
{
    public const string Version = "v1";
    private const string AadPrefix = "platform:sub:v1";
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int PlaintextLength = 16;

    private readonly byte[] _activeKey;
    private readonly Dictionary<string, byte[]> _keysByKid;

    public SubjectProtector(SecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!ConfigGuard.IsKey(options.SubjectEncryptionKey))
        {
            throw new SecurityConfigurationException(["Jwt:SubjectEncryptionKey must be 32 random bytes, base64"]);
        }

        _activeKey = Convert.FromBase64String(options.SubjectEncryptionKey);
        ActiveKeyId = KeyIdOf(_activeKey);
        _keysByKid = new Dictionary<string, byte[]>(StringComparer.Ordinal) { [ActiveKeyId] = _activeKey };

        foreach (var previous in options.PreviousSubjectEncryptionKeys)
        {
            if (!ConfigGuard.IsKey(previous))
            {
                throw new SecurityConfigurationException(["Jwt:PreviousSubjectEncryptionKeys entries must be 32 bytes, base64"]);
            }

            var bytes = Convert.FromBase64String(previous);
            _keysByKid.TryAdd(KeyIdOf(bytes), bytes);
        }
    }

    public string ActiveKeyId { get; }

    public string Protect(Guid userId, string signingKeyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signingKeyId);

        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var plaintext = userId.ToByteArray();
        var buffer = new byte[NonceLength + PlaintextLength + TagLength];
        nonce.CopyTo(buffer, 0);

        using var aes = new AesGcm(_activeKey, TagLength);
        aes.Encrypt(nonce, plaintext, buffer.AsSpan(NonceLength, PlaintextLength), buffer.AsSpan(NonceLength + PlaintextLength, TagLength), Aad(signingKeyId));

        return $"{Version}.{ActiveKeyId}.{Base64UrlEncoder.Encode(buffer)}";
    }

    public Guid? Unprotect(string subject, string signingKeyId)
    {
        if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(signingKeyId))
        {
            return null;
        }

        var parts = subject.Split('.');
        if (parts.Length != 3 || parts[0] != Version || !_keysByKid.TryGetValue(parts[1], out var key))
        {
            return null;
        }

        byte[] buffer;
        try
        {
            buffer = Base64UrlEncoder.DecodeBytes(parts[2]);
        }
        catch (FormatException)
        {
            return null;
        }

        if (buffer.Length != NonceLength + PlaintextLength + TagLength)
        {
            return null;
        }

        var plaintext = new byte[PlaintextLength];
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(buffer.AsSpan(0, NonceLength), buffer.AsSpan(NonceLength, PlaintextLength), buffer.AsSpan(NonceLength + PlaintextLength, TagLength), plaintext, Aad(signingKeyId));
        }
        catch (CryptographicException)
        {
            return null;
        }

        return new Guid(plaintext);
    }

    private static byte[] Aad(string signingKeyId) => Encoding.UTF8.GetBytes(AadPrefix + signingKeyId);

    private static string KeyIdOf(byte[] key) => Convert.ToHexString(SHA256.HashData(key))[..8].ToLowerInvariant();
}
