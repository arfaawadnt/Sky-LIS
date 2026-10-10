using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Identity;

namespace Platform.Infrastructure.Persistence.Identity;

/// <summary><c>identity.platform_users</c> (01 §4.7, D-06): global credentials, no tenant column, no RLS.</summary>
internal sealed class PlatformUserConfiguration : IEntityTypeConfiguration<PlatformUser>
{
    public void Configure(EntityTypeBuilder<PlatformUser> builder)
    {
        builder.ToTable("platform_users", AppDbContext.IdentitySchema);

        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedNever();

        builder.Property(user => user.Username).HasMaxLength(PlatformUser.MaxUsernameLength).IsRequired();
        builder.HasIndex(user => user.Username).IsUnique();

        builder.Property(user => user.Email).HasMaxLength(PlatformUser.MaxEmailLength).IsRequired();
        builder.HasIndex(user => user.Email).IsUnique();

        builder.Property(user => user.DisplayName).HasMaxLength(PlatformUser.MaxDisplayNameLength).IsRequired();
        builder.Property(user => user.Mobile).HasMaxLength(PlatformUser.MaxMobileLength);
        builder.HasIndex(user => user.Mobile).IsUnique().HasFilter("mobile IS NOT NULL").HasDatabaseName("ux_platform_users_mobile");
        builder.Property(user => user.PasswordHash).HasMaxLength(PlatformUser.MaxPasswordHashLength).IsRequired();
        builder.Property(user => user.SecurityStamp).IsRequired();
        builder.Property(user => user.PasswordChangedAtUtc).IsRequired();
        builder.Property(user => user.IsActive).IsRequired();
        builder.Property(user => user.FailedPasswordAttempts).IsRequired();
        builder.Property(user => user.IsLockedOut).IsRequired();
        builder.Property(user => user.LockoutEndUtc);
    }
}

/// <summary><c>identity.refresh_sessions</c> (01 §4.7, 02 §4.3): one row per session; the rotation family lives here.</summary>
internal sealed class RefreshSessionConfiguration : IEntityTypeConfiguration<RefreshSession>
{
    public void Configure(EntityTypeBuilder<RefreshSession> builder)
    {
        builder.ToTable("refresh_sessions", AppDbContext.IdentitySchema);

        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).ValueGeneratedNever();

        builder.Property(session => session.FamilyId).IsRequired();
        builder.HasIndex(session => session.FamilyId);

        builder.Property(session => session.PlatformUserId).IsRequired();
        builder.HasOne<PlatformUser>().WithMany().HasForeignKey(session => session.PlatformUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(session => session.PlatformUserId).HasFilter("is_revoked = false").HasDatabaseName("ix_refresh_sessions_user_live");

        builder.Property(session => session.RefreshTokenHash).HasMaxLength(RefreshSession.TokenHashLength).IsRequired();
        builder.HasIndex(session => session.RefreshTokenHash).IsUnique();

        builder.Property(session => session.Kind).IsRequired();
        builder.Property(session => session.Scope).IsRequired();
        builder.Property(session => session.IssuedAtUtc).IsRequired();
        builder.Property(session => session.ExpiresAtUtc).IsRequired();
        builder.Property(session => session.AbsoluteExpiresAtUtc).IsRequired();
        builder.Property(session => session.LastSeenAtUtc).IsRequired();
        builder.Property(session => session.IsRevoked).IsRequired();
        builder.Property(session => session.UserAgent).HasMaxLength(RefreshSession.MaxUserAgentLength);
        builder.Property(session => session.IpAddress); // System.Net.IPAddress maps to inet
        builder.Property(session => session.BranchId); // CP-04: the branch of the session, inherited through rotation
    }
}
