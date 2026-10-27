using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Catalogue;

namespace Platform.Infrastructure.Persistence.Catalogue;

// Compatibility groups of samples (change request LIS-CR-015, note M4). Keys (tenant_id, id), the tenant query filter and
// xmin concurrency come from the conventions; RLS is declared by the wave migration.

/// <summary><c>app.sample_compatibility_groups</c>: code unique per tenant.</summary>
internal sealed class SampleCompatibilityGroupConfiguration : IEntityTypeConfiguration<SampleCompatibilityGroup>
{
    public void Configure(EntityTypeBuilder<SampleCompatibilityGroup> builder)
    {
        builder.ToTable("sample_compatibility_groups", AppDbContext.AppSchema);

        builder.Property(group => group.Code).HasMaxLength(SampleCompatibilityGroup.MaxCodeLength).IsRequired();
        builder.HasIndex(group => new { group.TenantId, group.Code }).IsUnique();
        builder.OwnsOne(group => group.Name, CatalogueAMapping.Bilingual<SampleCompatibilityGroup>("name"));
        builder.Navigation(group => group.Name).IsRequired();
        builder.Property(group => group.IsActive).IsRequired();
        builder.Property(group => group.Revision).IsRequired();
        builder.Ignore(group => group.Pairs);

        builder.HasMany(group => group.Members).WithOne()
            .HasForeignKey(member => new { member.TenantId, member.GroupId })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_sample_compatibility_members_group");   // the default name reached 62 characters (DB review)
        builder.Navigation(group => group.Members).HasField("_members").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

/// <summary>
/// <c>app.sample_compatibility_members</c>: a (sample type, condition) pair once per group, "no condition" counting as one
/// value (NULLS NOT DISTINCT); composite FKs to the sample type and the condition (Restrict: conditions are never deleted).
/// </summary>
internal sealed class SampleCompatibilityMemberConfiguration : IEntityTypeConfiguration<SampleCompatibilityMember>
{
    public void Configure(EntityTypeBuilder<SampleCompatibilityMember> builder)
    {
        builder.ToTable("sample_compatibility_members", AppDbContext.AppSchema);

        builder.Property(member => member.SampleTypeId).IsRequired();
        builder.Property(member => member.SampleConditionId);
        builder.Property(member => member.Priority).IsRequired();
        builder.Ignore(member => member.Pair);

        builder.HasIndex(member => new { member.TenantId, member.GroupId, member.SampleTypeId, member.SampleConditionId })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("ux_sample_compatibility_members_pair");
        builder.HasIndex(member => new { member.TenantId, member.SampleTypeId, member.SampleConditionId })
            .HasDatabaseName("ix_sample_compatibility_members_sample");

        builder.HasOne<SampleType>().WithMany().HasForeignKey(member => new { member.TenantId, member.SampleTypeId }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_sample_compatibility_members_sample_type");
        builder.HasOne<SampleCondition>().WithMany().HasForeignKey(member => new { member.TenantId, member.SampleConditionId }).IsRequired(false).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_sample_compatibility_members_sample_condition");
    }
}
