using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Identity.MachineCredentials;
using Platform.Domain.Identity;
using Platform.Infrastructure.Security.Hashing;

namespace Platform.Infrastructure.Persistence.Identity;

/// <summary>
/// <see cref="IMachineSecrets"/> (02 §6.3): client ids and secrets from the CSPRNG; the stored form of a secret is
/// <c>v1.</c> + base64url HMAC-SHA256 under the master-key subkey <c>machine-credential</c>. A 256-bit random secret
/// needs no slow hash; the keyed digest keeps a database copy useless without the master key. Rotating the master key
/// therefore requires rotating every machine secret (runbook note, intake A12). The keyed hash belongs to the security
/// registration, so it is resolved when a secret is hashed, not when the adapter is built: a host without the security
/// edge (tools, the persistence test fixture) can still resolve the persistence adapters.
/// </summary>
internal sealed class MachineCredentialSecrets(IServiceProvider services) : IMachineSecrets
{
    public const string Purpose = "machine-credential";
    public const string Version = "v1.";
    public const int SecretBytes = 32;

    public string NewClientId() => MachineCredential.ClientIdPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    public MachineSecret NewSecret()
    {
        var secret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretBytes));
        return new MachineSecret(secret, Hash(secret));
    }

    public bool Verify(string storedHash, string presentedSecret)
    {
        if (string.IsNullOrEmpty(storedHash) || !storedHash.StartsWith(Version, StringComparison.Ordinal) || string.IsNullOrEmpty(presentedSecret))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(storedHash), Encoding.ASCII.GetBytes(Hash(presentedSecret)));
    }

    public string UnusablePasswordHash() => "!machine-credential!" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    private string Hash(string secret) => Version + Base64Url.EncodeToString(services.GetRequiredService<MasterKeyedHash>().Compute(Purpose, secret));
}
