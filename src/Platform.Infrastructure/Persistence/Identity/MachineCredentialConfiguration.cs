using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Identity;

namespace Platform.Infrastructure.Persistence.Identity;

/// <summary>
/// <c>identity.machine_credentials</c> (02 §6.3): no row-level security and a single-column key, like
/// <c>refresh_sessions</c> — the token exchange finds the row by client id before any tenant is known. The tenant is an
/// ordinary column every management read filters on. Scopes are a <c>text[]</c>; the service user is one per credential.
/// </summary>
internal sealed class MachineCredentialConfiguration : IEntityTypeConfiguration<MachineCredential>
{
    public void Configure(EntityTypeBuilder<MachineCredential> builder)
    {
        builder.ToTable("machine_credentials", AppDbContext.IdentitySchema);

        builder.HasKey(credential => credential.Id);
        builder.Property(credential => credential.Id).ValueGeneratedNever();

        builder.Property(credential => credential.TenantId).IsRequired();
        builder.Property(credential => credential.ClientId).HasMaxLength(MachineCredential.ClientIdLength).IsRequired();
        builder.HasIndex(credential => credential.ClientId).IsUnique().HasDatabaseName("ux_machine_credentials_client_id");

        builder.Property(credential => credential.Name).HasMaxLength(MachineCredential.MaxNameLength).IsRequired();
        builder.Property(credential => credential.SecretHash).HasMaxLength(MachineCredential.MaxSecretHashLength).IsRequired();
        builder.Property(credential => credential.Scopes).IsRequired();
        builder.Property(credential => credential.BranchId);
        builder.Property(credential => credential.ExpiresAtUtc).IsRequired();
        builder.Property(credential => credential.RevocationReason).HasMaxLength(MachineCredential.MaxReasonLength);

        builder.Property(credential => credential.ServiceUserId).IsRequired();
        builder.HasOne<PlatformUser>().WithMany().HasForeignKey(credential => credential.ServiceUserId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_machine_credentials_service_user");
        builder.HasIndex(credential => credential.ServiceUserId).IsUnique().HasDatabaseName("ux_machine_credentials_service_user");
        builder.HasIndex(credential => new { credential.TenantId, credential.RevokedAtUtc }).HasDatabaseName("ix_machine_credentials_tenant");

        builder.Ignore(credential => credential.IsRevoked);
    }
}
