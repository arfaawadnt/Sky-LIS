using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Platform.Infrastructure.Persistence.Identity;

/// <summary>
/// <c>identity.otp_challenges</c> (SRS CP-02/CP-03, 02 §4.5 as a capability): one-time codes at rest. Neither the
/// target nor the code is stored; both are keyed HMAC-SHA256 digests under the master key, so the table reveals
/// nothing if read. Five-minute lifetime, five attempts, consumed on the first match; an earlier live challenge for
/// the same purpose and target is closed when a new one is issued. Global table: no tenant, no RLS.
/// </summary>
public sealed class OtpChallenge
{
    public const int MaxPurposeLength = 40;
    public const int HashLength = 32;

    private OtpChallenge()
    {
        Purpose = string.Empty;
        TargetHash = [];
        CodeHash = [];
    }

    public OtpChallenge(string purpose, byte[] targetHash, byte[] codeHash, DateTimeOffset issuedAtUtc, TimeSpan lifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        Id = Guid.CreateVersion7();
        Purpose = purpose.Length <= MaxPurposeLength ? purpose : purpose[..MaxPurposeLength];
        TargetHash = targetHash;
        CodeHash = codeHash;
        IssuedAtUtc = issuedAtUtc;
        ExpiresAtUtc = issuedAtUtc + lifetime;
    }

    public Guid Id { get; private set; }

    public string Purpose { get; private set; }

    public byte[] TargetHash { get; private set; }

    public byte[] CodeHash { get; private set; }

    public DateTimeOffset IssuedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    /// <summary>Wrong codes presented; a 429 never counts, a checked wrong code always does.</summary>
    public int Attempts { get; private set; }

    public DateTimeOffset? ConsumedAtUtc { get; private set; }

    public bool IsLiveAt(DateTimeOffset nowUtc, int maxAttempts) => ConsumedAtUtc is null && ExpiresAtUtc > nowUtc && Attempts < maxAttempts;

    public void RecordAttempt() => Attempts++;

    public void Consume(DateTimeOffset nowUtc) => ConsumedAtUtc ??= nowUtc;
}

internal sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> builder)
    {
        builder.ToTable("otp_challenges", AppDbContext.IdentitySchema);
        builder.HasKey(challenge => challenge.Id);
        builder.Property(challenge => challenge.Id).ValueGeneratedNever();
        builder.Property(challenge => challenge.Purpose).HasMaxLength(OtpChallenge.MaxPurposeLength).IsRequired();
        builder.Property(challenge => challenge.TargetHash).HasMaxLength(OtpChallenge.HashLength).IsRequired();
        builder.Property(challenge => challenge.CodeHash).HasMaxLength(OtpChallenge.HashLength).IsRequired();
        builder.Property(challenge => challenge.IssuedAtUtc).IsRequired();
        builder.Property(challenge => challenge.ExpiresAtUtc).IsRequired();
        builder.Property(challenge => challenge.Attempts).IsRequired();
        builder.HasIndex(challenge => new { challenge.Purpose, challenge.TargetHash }).HasFilter("consumed_at_utc IS NULL").HasDatabaseName("ix_otp_challenges_live");
    }
}
