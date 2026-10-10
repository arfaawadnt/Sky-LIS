using System.Security.Cryptography;
using System.Text;
using Platform.Application.Identity;
using Platform.Application.UnitTests.Support;
using Platform.Infrastructure.Security;
using Platform.Infrastructure.Security.CredentialTransport;
using Platform.Infrastructure.Security.Hashing;
using Platform.Infrastructure.Security.Protection;

namespace Platform.Application.UnitTests.Security;

public sealed class Argon2idHasherTests
{
    private readonly Argon2idHasher _hasher = new();

    [Fact]
    public void Hashes_are_PHC_strings_with_the_pinned_parameters_and_verify()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        Assert.StartsWith("$argon2id$v=19$m=65536,t=3,p=1$", hash, StringComparison.Ordinal);
        Assert.True(_hasher.Verify(hash, "correct horse battery staple"));
        Assert.False(_hasher.Verify(hash, "correct horse battery stapl"));
        Assert.False(_hasher.NeedsRehash(hash));
        Assert.NotEqual(hash, _hasher.Hash("correct horse battery staple"));
    }

    [Fact]
    public void Older_parameters_need_a_rehash_and_garbage_never_verifies()
    {
        var weaker = _hasher.Hash("x").Replace("m=65536", "m=4096", StringComparison.Ordinal);

        Assert.True(_hasher.NeedsRehash(weaker));
        Assert.True(_hasher.NeedsRehash("not-a-hash"));
        Assert.False(_hasher.Verify("not-a-hash", "x"));
        Assert.False(_hasher.Verify(string.Empty, "x"));
    }
}

public sealed class MasterKeyProtectorTests
{
    [Fact]
    public void Round_trips_per_purpose_and_refuses_another_purpose_or_a_tampered_payload()
    {
        var protector = new MasterKeyProtector(TestKeys.NewKeyBase64());
        var secret = RandomNumberGenerator.GetBytes(48);

        var protectedBytes = protector.Protect("purpose-a", secret);

        Assert.Equal(secret, protector.Unprotect("purpose-a", protectedBytes));
        Assert.NotEqual(protectedBytes, protector.Protect("purpose-a", secret));
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect("purpose-b", protectedBytes));

        protectedBytes[^1] ^= 0x01;
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect("purpose-a", protectedBytes));
        Assert.Throws<SecurityConfigurationException>(() => new MasterKeyProtector("short"));
    }
}

public sealed class ChallengeNonceTests
{
    [Fact]
    public void A_nonce_validates_until_its_expiry_and_fails_when_tampered()
    {
        var macKey = RandomNumberGenerator.GetBytes(32);
        var expiresAt = FixedClock.Instant.AddSeconds(120);
        var nonce = Build(macKey, expiresAt);

        Assert.True(CredentialChallengeIssuer.TryValidateNonce(nonce, macKey, FixedClock.Instant, out _, out var parsedExpiry));
        Assert.Equal(expiresAt, parsedExpiry);
        Assert.False(CredentialChallengeIssuer.TryValidateNonce(nonce, macKey, expiresAt, out _, out _));
        Assert.False(CredentialChallengeIssuer.TryValidateNonce(nonce, RandomNumberGenerator.GetBytes(32), FixedClock.Instant, out _, out _));
        Assert.False(CredentialChallengeIssuer.TryValidateNonce(nonce[..^2] + "AA", macKey, FixedClock.Instant, out _, out _));
        Assert.False(CredentialChallengeIssuer.TryValidateNonce("short", macKey, FixedClock.Instant, out _, out _));
    }

    private static string Build(byte[] macKey, DateTimeOffset expiresAt)
    {
        var nonce = new byte[CredentialChallengeIssuer.NonceLength];
        RandomNumberGenerator.Fill(nonce.AsSpan(0, CredentialChallengeIssuer.RandomLength));
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(nonce.AsSpan(CredentialChallengeIssuer.RandomLength, 8), expiresAt.ToUnixTimeSeconds());
        HMACSHA256.HashData(macKey, nonce.AsSpan(0, CredentialChallengeIssuer.RandomLength + 8), nonce.AsSpan(CredentialChallengeIssuer.RandomLength + 8));
        return Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(nonce);
    }
}

/// <summary>ConfigGuardTests (01 §8, C-07): the guard is pure, so it refuses identically under every environment name.</summary>
public sealed class ConfigGuardTests
{
    private static readonly string[] EnvironmentNames = ["Development", "Staging", "Production", "QA", "Test", "Local"];

    [Fact]
    public void Valid_options_pass_under_every_environment_name()
    {
        var options = TestKeys.ValidOptions();
        foreach (var name in EnvironmentNames)
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", name);
            Assert.Empty(ConfigGuard.Inspect(options, TestKeys.DefaultPolicy()));
        }

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
    }

    [Fact]
    public void Each_weakness_is_named_and_the_refusal_is_identical_in_every_environment()
    {
        var valid = TestKeys.ValidOptions();
        var weak = new SecurityOptions
        {
            Issuer = string.Empty,
            Audience = valid.Audience,
            SigningKeyPem = "-----BEGIN PRIVATE KEY-----\nnope\n-----END PRIVATE KEY-----",
            SubjectEncryptionKey = Convert.ToBase64String(Encoding.UTF8.GetBytes("too short")),
            PreviousSubjectEncryptionKeys = ["@@"],
            MasterKey = string.Empty,
            EnvelopeRsaKeySize = 2048,
        };
        var longLived = new SessionPolicy { AccessTokenLifetime = TimeSpan.FromMinutes(16) };

        IReadOnlyList<string>? previous = null;
        foreach (var name in EnvironmentNames)
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", name);
            var violations = ConfigGuard.Inspect(weak, longLived);
            Assert.Contains(violations, v => v.Contains("Jwt:Issuer", StringComparison.Ordinal));
            Assert.Contains(violations, v => v.Contains("Jwt:SigningKey", StringComparison.Ordinal));
            Assert.Contains(violations, v => v.Contains("Jwt:SubjectEncryptionKey", StringComparison.Ordinal));
            Assert.Contains(violations, v => v.Contains("PreviousSubjectEncryptionKeys[0]", StringComparison.Ordinal));
            Assert.Contains(violations, v => v.Contains("DataProtection:MasterKey", StringComparison.Ordinal));
            Assert.Contains(violations, v => v.Contains("AccessTokenLifetime", StringComparison.Ordinal));
            Assert.Contains(violations, v => v.Contains("RsaKeySize", StringComparison.Ordinal));
            if (previous is not null)
            {
                Assert.Equal(previous, violations);
            }

            previous = violations;
        }

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        Assert.Throws<SecurityConfigurationException>(() => ConfigGuard.Enforce(weak, longLived));
    }

    [Fact]
    public void A_sixteen_minute_access_token_is_refused_and_fifteen_is_the_ceiling()
    {
        var options = TestKeys.ValidOptions();

        Assert.Empty(ConfigGuard.Inspect(options, new SessionPolicy { AccessTokenLifetime = TimeSpan.FromMinutes(15) }));
        Assert.NotEmpty(ConfigGuard.Inspect(options, new SessionPolicy { AccessTokenLifetime = TimeSpan.FromMinutes(15).Add(TimeSpan.FromSeconds(1)) }));
        Assert.NotEmpty(ConfigGuard.Inspect(options, new SessionPolicy { AccessTokenLifetime = TimeSpan.Zero }));
    }
}
