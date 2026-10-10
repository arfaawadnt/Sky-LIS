using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Platform.Application.Abstractions.Security;

namespace Platform.Infrastructure.Security.Hashing;

/// <summary>
/// Argon2id with the parameters of 02 §6.1 (memory 64 MiB, iterations 3, parallelism 1, 16-byte salt, 32-byte tag),
/// stored as a PHC string. Parameters are pinned in code and asserted by a test; a hash made with older parameters
/// reports <see cref="NeedsRehash"/> so sign-in upgrades it.
/// </summary>
public sealed class Argon2idHasher : IPasswordHasher
{
    public const int MemoryKiB = 65536;
    public const int Iterations = 3;
    public const int Parallelism = 1;
    public const int SaltLength = 16;
    public const int TagLength = 32;
    private const string Prefix = "$argon2id$v=19$";

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var tag = Compute(password, salt, MemoryKiB, Iterations, Parallelism, TagLength);
        return string.Create(CultureInfo.InvariantCulture, $"{Prefix}m={MemoryKiB},t={Iterations},p={Parallelism}${Base64(salt)}${Base64(tag)}");
    }

    public bool Verify(string passwordHash, string password)
    {
        if (string.IsNullOrEmpty(password) || !TryParse(passwordHash, out var parameters))
        {
            return false;
        }

        var computed = Compute(password, parameters.Salt, parameters.MemoryKiB, parameters.Iterations, parameters.Parallelism, parameters.Tag.Length);
        return CryptographicOperations.FixedTimeEquals(computed, parameters.Tag);
    }

    public bool NeedsRehash(string passwordHash) =>
        !TryParse(passwordHash, out var parameters)
        || parameters.MemoryKiB != MemoryKiB
        || parameters.Iterations != Iterations
        || parameters.Parallelism != Parallelism
        || parameters.Tag.Length != TagLength;

    private static byte[] Compute(string password, byte[] salt, int memoryKiB, int iterations, int parallelism, int tagLength)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKiB,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon2.GetBytes(tagLength);
    }

    private static bool TryParse(string? hash, out (byte[] Salt, byte[] Tag, int MemoryKiB, int Iterations, int Parallelism) parameters)
    {
        parameters = default;
        if (hash is null || !hash.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = hash[Prefix.Length..].Split('$');
        if (parts.Length != 3)
        {
            return false;
        }

        int? memory = null, iterations = null, parallelism = null;
        foreach (var pair in parts[0].Split(','))
        {
            var kv = pair.Split('=');
            if (kv.Length != 2 || !int.TryParse(kv[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            switch (kv[0])
            {
                case "m": memory = value; break;
                case "t": iterations = value; break;
                case "p": parallelism = value; break;
                default: return false;
            }
        }

        if (memory is null || iterations is null || parallelism is null)
        {
            return false;
        }

        try
        {
            parameters = (FromBase64(parts[1]), FromBase64(parts[2]), memory.Value, iterations.Value, parallelism.Value);
            return parameters.Salt.Length > 0 && parameters.Tag.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Base64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

    private static byte[] FromBase64(string value) => Convert.FromBase64String(value.PadRight(value.Length + ((4 - (value.Length % 4)) % 4), '='));
}
