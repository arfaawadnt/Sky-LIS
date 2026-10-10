using System.Security.Cryptography;
using Platform.Application.Identity;

namespace Platform.Infrastructure.Security;

/// <summary>
/// The secrets and parameters of the security edge (02 §6.4, 01 §5.3). Bound from configuration by the host; never
/// from <c>appsettings*.json</c> for the secret values. <see cref="ConfigGuard"/> refuses to start on anything
/// missing or weak, under every environment name (C-07).
/// </summary>
public sealed class SecurityOptions
{
    public const int KeyLength = 32;
    public const int MinimumRsaKeySize = 3072;

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = "platform-api";

    /// <summary><c>Jwt:SigningKey</c>: a P-256 private key in PEM (ES256).</summary>
    public string SigningKeyPem { get; init; } = string.Empty;

    /// <summary><c>Jwt:SigningKeyId</c>: optional; derived from the public key when absent.</summary>
    public string? SigningKeyId { get; init; }

    /// <summary><c>Jwt:SubjectEncryptionKey</c>: 32 random bytes, base64 (D-02).</summary>
    public string SubjectEncryptionKey { get; init; } = string.Empty;

    /// <summary><c>Jwt:PreviousSubjectEncryptionKeys</c>: accepted for decryption during a rotation overlap.</summary>
    public string[] PreviousSubjectEncryptionKeys { get; init; } = [];

    /// <summary><c>DataProtection:MasterKey</c>: 32 random bytes, base64; protects envelope keys and future MFA secrets at rest.</summary>
    public string MasterKey { get; init; } = string.Empty;

    public TimeSpan ChallengeLifetime { get; init; } = TimeSpan.FromSeconds(120);

    public TimeSpan EnvelopeKeyRotation { get; init; } = TimeSpan.FromHours(24);

    public TimeSpan EnvelopeKeyOverlap { get; init; } = TimeSpan.FromMinutes(10);

    public int EnvelopeRsaKeySize { get; init; } = MinimumRsaKeySize;

    /// <summary>
    /// Mirrors <c>Lis:Security:AllowInsecureLocalDevelopment</c>: the one-time code is also written to the structured
    /// log at Debug so a developer without an SMS gateway can complete the flows. Never set outside loopback development.
    /// </summary>
    public bool RevealOtpInDevelopmentLog { get; init; }
}

/// <summary>Raised at start-up when the configuration would weaken a control. Lists every violation at once.</summary>
public sealed class SecurityConfigurationException(IReadOnlyList<string> violations)
    : Exception("The security configuration is incomplete or weak: " + string.Join("; ", violations))
{
    public IReadOnlyList<string> Violations { get; } = violations;
}

/// <summary>The configuration guard of 01 §5.3. Pure: the same input gives the same refusal under any environment name.</summary>
public static class ConfigGuard
{
    public static IReadOnlyList<string> Inspect(SecurityOptions options, SessionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(policy);

        var violations = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            violations.Add("Jwt:Issuer is required");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            violations.Add("Jwt:Audience is required");
        }

        if (!IsP256PrivateKey(options.SigningKeyPem))
        {
            violations.Add("Jwt:SigningKey must be a P-256 (ES256) private key in PEM");
        }

        if (!IsKey(options.SubjectEncryptionKey))
        {
            violations.Add($"Jwt:SubjectEncryptionKey must be {SecurityOptions.KeyLength} random bytes, base64");
        }

        for (var i = 0; i < options.PreviousSubjectEncryptionKeys.Length; i++)
        {
            if (!IsKey(options.PreviousSubjectEncryptionKeys[i]))
            {
                violations.Add($"Jwt:PreviousSubjectEncryptionKeys[{i}] must be {SecurityOptions.KeyLength} bytes, base64");
            }
        }

        if (!IsKey(options.MasterKey))
        {
            violations.Add($"DataProtection:MasterKey must be {SecurityOptions.KeyLength} random bytes, base64");
        }

        if (policy.AccessTokenLifetime <= TimeSpan.Zero || policy.AccessTokenLifetime > SessionPolicy.MaxAccessTokenLifetime)
        {
            violations.Add($"Security:Session:AccessTokenLifetime must be between 1 second and {SessionPolicy.MaxAccessTokenLifetime.TotalMinutes} minutes (D-01)");
        }

        if (policy.BrowserAbsoluteLifetime < policy.BrowserSlidingLifetime || policy.MachineAbsoluteLifetime < policy.MachineSlidingLifetime)
        {
            violations.Add("absolute session lifetimes must be at least the sliding lifetimes");
        }

        if (policy.LockoutThreshold < 1)
        {
            violations.Add("Security:Session:LockoutThreshold must be at least 1");
        }

        if (options.EnvelopeRsaKeySize < SecurityOptions.MinimumRsaKeySize)
        {
            violations.Add($"Security:Envelope:RsaKeySize must be at least {SecurityOptions.MinimumRsaKeySize} (D-03)");
        }

        if (options.ChallengeLifetime <= TimeSpan.Zero || options.ChallengeLifetime > TimeSpan.FromMinutes(5))
        {
            violations.Add("Security:Envelope:ChallengeLifetime must be between 1 second and 5 minutes");
        }

        if (policy.OtpLength is < 4 or > 10)
        {
            violations.Add("Security:Session:OtpLength must be between 4 and 10 digits");
        }

        if (policy.OtpLifetime <= TimeSpan.Zero || policy.OtpLifetime > TimeSpan.FromMinutes(15))
        {
            violations.Add("Security:Session:OtpLifetime must be between 1 second and 15 minutes");
        }

        if (policy.OtpMaxAttempts is < 1 or > 10)
        {
            violations.Add("Security:Session:OtpMaxAttempts must be between 1 and 10");
        }

        if (policy.PasswordMinLength is < 8 or > SessionPolicy.PasswordMaxLength)
        {
            violations.Add($"Security:Session:PasswordMinLength must be between 8 and {SessionPolicy.PasswordMaxLength}");
        }

        return violations;
    }

    public static void Enforce(SecurityOptions options, SessionPolicy policy)
    {
        var violations = Inspect(options, policy);
        if (violations.Count > 0)
        {
            throw new SecurityConfigurationException(violations);
        }
    }

    public static bool IsKey(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return false;
        }

        try
        {
            return Convert.FromBase64String(base64).Length == SecurityOptions.KeyLength;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool IsP256PrivateKey(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
        {
            return false;
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(pem);
            var parameters = ecdsa.ExportParameters(includePrivateParameters: true);
            return parameters.D is { Length: > 0 } && parameters.Curve.Oid?.Value == "1.2.840.10045.3.1.7";
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
