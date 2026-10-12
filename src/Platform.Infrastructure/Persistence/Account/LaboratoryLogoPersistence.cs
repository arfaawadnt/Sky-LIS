using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Application.Account;
using Platform.Domain.Account;

namespace Platform.Infrastructure.Persistence.Account;

/// <summary>
/// <c>app.laboratory_logos</c>: key <c>(tenant_id, id)</c>, xmin and the tenant filter by convention; one row per
/// laboratory (unique on the tenant); a stored image has a content type and a size within the module's rules (CHECKs).
/// The bytes live in the file store, never in the database. RLS is declared in the creating migration.
/// </summary>
internal sealed class LaboratoryLogoConfiguration : IEntityTypeConfiguration<LaboratoryLogo>
{
    public void Configure(EntityTypeBuilder<LaboratoryLogo> builder)
    {
        builder.ToTable("laboratory_logos", AppDbContext.AppSchema, table =>
        {
            table.HasCheckConstraint("ck_laboratory_logos_content_type", "content_type IN ('image/png', 'image/jpeg', 'image/webp')");
            table.HasCheckConstraint("ck_laboratory_logos_size", $"size_bytes BETWEEN 1 AND {LaboratoryLogoFiles.MaxSizeBytes}");
            table.HasCheckConstraint("ck_laboratory_logos_version", "version >= 1");
        });

        builder.HasIndex(logo => logo.TenantId).IsUnique();
        builder.Property(logo => logo.ContentType).HasMaxLength(LaboratoryLogo.MaxContentTypeLength).IsRequired();
        builder.Property(logo => logo.SizeBytes).IsRequired();
        builder.Property(logo => logo.StorageKey).HasMaxLength(LaboratoryLogo.MaxStorageKeyLength).IsRequired();
        builder.Property(logo => logo.Sha256).HasMaxLength(LaboratoryLogo.Sha256HexLength).IsFixedLength().IsRequired();
        builder.Property(logo => logo.Version).IsRequired();
        builder.Property(logo => logo.IsActive).IsRequired();
        builder.Property(logo => logo.ChangedByUserId).IsRequired();
        builder.Property(logo => logo.ChangedAtUtc).IsRequired();
    }
}

/// <summary>The current laboratory's logo row: the tenant query filter (and RLS) leaves at most one.</summary>
internal sealed class LaboratoryLogoRepository(AppDbContext context) : Repository<LaboratoryLogo>(context), ILaboratoryLogoRepository
{
    public Task<LaboratoryLogo?> GetCurrentForUpdateAsync(CancellationToken cancellationToken) =>
        Set.FirstOrDefaultAsync(cancellationToken);

    public Task<LaboratoryLogoInfo?> FindActiveAsync(CancellationToken cancellationToken) =>
        Set.AsNoTracking()
            .Where(logo => logo.IsActive)
            .Select(logo => new LaboratoryLogoInfo(logo.StorageKey, logo.ContentType, logo.Version))
            .FirstOrDefaultAsync(cancellationToken);
}
