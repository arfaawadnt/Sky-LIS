using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Infrastructure.Persistence;

namespace Platform.Infrastructure.Security.CredentialTransport;

/// <summary>
/// One RSA-3072 envelope key pair with its nonce MAC key (02 §3.7, 01 §4.8), shared by every replica through
/// <c>identity.envelope_keys</c> and protected at rest with the master key. Rotated every 24 hours; a retired key
/// is accepted for 10 more minutes so outstanding challenges survive the rotation.
/// </summary>
public sealed class EnvelopeKey
{
    public const int MaxKidLength = 64;
    public const string KeyProtectionPurpose = "envelope-private-key";
    public const string MacProtectionPurpose = "envelope-nonce-mac-key";

    private EnvelopeKey()
    {
        Kid = string.Empty;
        PublicKeyPem = string.Empty;
        PrivateKeyEnc = [];
        NonceMacKeyEnc = [];
    }

    public EnvelopeKey(string kid, string publicKeyPem, byte[] privateKeyEnc, byte[] nonceMacKeyEnc, DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kid);
        Kid = kid;
        PublicKeyPem = publicKeyPem;
        PrivateKeyEnc = privateKeyEnc;
        NonceMacKeyEnc = nonceMacKeyEnc;
        CreatedAtUtc = createdAtUtc;
    }

    public string Kid { get; private set; }

    public string PublicKeyPem { get; private set; }

    /// <summary>PKCS#8 private key, protected with the master key.</summary>
    public byte[] PrivateKeyEnc { get; private set; }

    /// <summary>HMAC-SHA256 key for the nonces issued under this kid, protected with the master key.</summary>
    public byte[] NonceMacKeyEnc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? RetiredAtUtc { get; private set; }

    public void Retire(DateTimeOffset nowUtc) => RetiredAtUtc ??= nowUtc;

    /// <summary>Active, or retired less than <paramref name="overlap"/> ago.</summary>
    public bool IsAcceptedAt(DateTimeOffset nowUtc, TimeSpan overlap) => RetiredAtUtc is null || RetiredAtUtc.Value + overlap > nowUtc;
}

/// <summary>A consumed nonce (02 §3.3): inserted on first use; the primary key refuses a second use. UNLOGGED: a 120-second fact.</summary>
public sealed class CredentialNonce
{
    public const int HashLength = 32;

    private CredentialNonce()
    {
        NonceHash = [];
    }

    public CredentialNonce(byte[] nonceHash, DateTimeOffset expiresAtUtc)
    {
        NonceHash = nonceHash;
        ExpiresAtUtc = expiresAtUtc;
    }

    public byte[] NonceHash { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }
}

internal sealed class EnvelopeKeyConfiguration : IEntityTypeConfiguration<EnvelopeKey>
{
    public void Configure(EntityTypeBuilder<EnvelopeKey> builder)
    {
        builder.ToTable("envelope_keys", AppDbContext.IdentitySchema);
        builder.HasKey(key => key.Kid);
        builder.Property(key => key.Kid).HasMaxLength(EnvelopeKey.MaxKidLength);
        builder.Property(key => key.PublicKeyPem).HasColumnType("text").IsRequired();
        builder.Property(key => key.PrivateKeyEnc).IsRequired();
        builder.Property(key => key.NonceMacKeyEnc).IsRequired();
        builder.Property(key => key.CreatedAtUtc).IsRequired();
        builder.HasIndex(key => key.CreatedAtUtc).HasDatabaseName("ix_envelope_keys_created");
    }
}

internal sealed class CredentialNonceConfiguration : IEntityTypeConfiguration<CredentialNonce>
{
    public void Configure(EntityTypeBuilder<CredentialNonce> builder)
    {
        // Pre-authentication table: no tenant column, no RLS (01 §4.1); listed in the migration gate's allow-list.
        builder.ToTable("credential_nonces", AppDbContext.AppSchema);
        builder.IsUnlogged();
        builder.HasKey(nonce => nonce.NonceHash);
        builder.Property(nonce => nonce.NonceHash).HasMaxLength(CredentialNonce.HashLength);
        builder.Property(nonce => nonce.ExpiresAtUtc).IsRequired();
        builder.HasIndex(nonce => nonce.ExpiresAtUtc).HasDatabaseName("ix_credential_nonces_expiry");
    }
}
