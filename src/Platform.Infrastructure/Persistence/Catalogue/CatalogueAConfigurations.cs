using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Catalogue;
using Platform.SharedKernel.Localization;

namespace Platform.Infrastructure.Persistence.Catalogue;

// Tables of catalogue half A (intake LIS-INT-CAT-001 §5). Keys (tenant_id, id), the tenant query filter, xmin
// concurrency and enum CHECK constraints come from the conventions; RLS is declared by the wave migration. Children
// are tenant tables with a composite FK (tenant_id, <root>_id) to their root (cascade: they live and die with it);
// references to other aggregates are composite FKs with Restrict.

/// <summary><c>app.clinical_groups</c>: code unique per tenant (INV-CAT-001).</summary>
internal sealed class ClinicalGroupConfiguration : IEntityTypeConfiguration<ClinicalGroup>
{
    public void Configure(EntityTypeBuilder<ClinicalGroup> builder)
    {
        builder.ToTable("clinical_groups", AppDbContext.AppSchema);

        builder.Property(group => group.Code).HasMaxLength(ClinicalGroup.MaxCodeLength).IsRequired();
        builder.HasIndex(group => new { group.TenantId, group.Code }).IsUnique();
        builder.Property(group => group.UnitCode).HasMaxLength(ClinicalGroup.MaxUnitCodeLength).IsRequired();
        builder.OwnsOne(group => group.Name, CatalogueAMapping.Bilingual<ClinicalGroup>("name"));
        builder.Navigation(group => group.Name).IsRequired();
        builder.Property(group => group.ReportName).HasMaxLength(ClinicalGroup.MaxReportNameLength);
        builder.Property(group => group.ReportTemplateId);   // catalogue B (same wave): no FK yet
        builder.Property(group => group.DisplayOrder).IsRequired();
        builder.Property(group => group.IsActive).IsRequired();
        builder.HasIndex(group => new { group.TenantId, group.IsActive, group.DisplayOrder });
    }
}

/// <summary><c>app.sample_types</c> with <c>app.sample_conditions</c> (condition code unique within its type).</summary>
internal sealed class SampleTypeConfiguration : IEntityTypeConfiguration<SampleType>
{
    public void Configure(EntityTypeBuilder<SampleType> builder)
    {
        builder.ToTable("sample_types", AppDbContext.AppSchema);

        builder.Property(sampleType => sampleType.Code).HasMaxLength(SampleType.MaxCodeLength).IsRequired();
        builder.HasIndex(sampleType => new { sampleType.TenantId, sampleType.Code }).IsUnique();
        builder.Property(sampleType => sampleType.ShortName).HasMaxLength(SampleType.MaxShortNameLength).IsRequired();
        builder.OwnsOne(sampleType => sampleType.Name, CatalogueAMapping.Bilingual<SampleType>("name"));
        builder.Navigation(sampleType => sampleType.Name).IsRequired();
        builder.Property(sampleType => sampleType.IsActive).IsRequired();
        builder.Property(sampleType => sampleType.Revision).IsRequired();

        builder.HasMany(sampleType => sampleType.Conditions).WithOne()
            .HasForeignKey(condition => new { condition.TenantId, condition.SampleTypeId })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(sampleType => sampleType.Conditions).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

internal sealed class SampleConditionConfiguration : IEntityTypeConfiguration<SampleCondition>
{
    public void Configure(EntityTypeBuilder<SampleCondition> builder)
    {
        builder.ToTable("sample_conditions", AppDbContext.AppSchema);

        builder.Property(condition => condition.Code).HasMaxLength(SampleCondition.MaxCodeLength).IsRequired();
        builder.HasIndex(condition => new { condition.TenantId, condition.SampleTypeId, condition.Code }).IsUnique();
        builder.OwnsOne(condition => condition.Name, CatalogueAMapping.Bilingual<SampleCondition>("name"));
        builder.Navigation(condition => condition.Name).IsRequired();
        builder.Property(condition => condition.IsActive).IsRequired();
    }
}

/// <summary><c>app.measurement_units</c>: the upper-cased name is unique per tenant (INV-CAT-002).</summary>
internal sealed class MeasurementUnitConfiguration : IEntityTypeConfiguration<MeasurementUnit>
{
    public void Configure(EntityTypeBuilder<MeasurementUnit> builder)
    {
        builder.ToTable("measurement_units", AppDbContext.AppSchema);

        builder.Property(unit => unit.Name).HasMaxLength(MeasurementUnit.MaxNameLength).IsRequired();
        builder.Property(unit => unit.NameKey).HasMaxLength(MeasurementUnit.MaxNameLength).IsRequired();
        builder.HasIndex(unit => new { unit.TenantId, unit.NameKey }).IsUnique();
        builder.Property(unit => unit.IsActive).IsRequired();
    }
}

/// <summary><c>app.patient_conditions</c>: the upper-cased English name is unique per tenant (INV-CAT-002).</summary>
internal sealed class PatientConditionConfiguration : IEntityTypeConfiguration<PatientCondition>
{
    public void Configure(EntityTypeBuilder<PatientCondition> builder)
    {
        builder.ToTable("patient_conditions", AppDbContext.AppSchema);

        builder.OwnsOne(condition => condition.Name, CatalogueAMapping.Bilingual<PatientCondition>("name"));
        builder.Navigation(condition => condition.Name).IsRequired();
        builder.Property(condition => condition.NameKey).HasMaxLength(LocalizedText.MaxLength).IsRequired();
        builder.HasIndex(condition => new { condition.TenantId, condition.NameKey }).IsUnique();
        builder.Property(condition => condition.IsActive).IsRequired();
    }
}

/// <summary>
/// <c>app.services</c> with <c>app.service_members</c>. The sequence's uniqueness (INV-CAT-004) is the deferrable
/// constraint <c>ux_services_sequence</c> of <c>CatalogueA.migration.sql</c>, so a swap in one save passes; the
/// shortcut number is unique when set (INV-CAT-005).
/// </summary>
internal sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.ToTable("services", AppDbContext.AppSchema);

        builder.Property(service => service.Code).HasMaxLength(Service.MaxCodeLength).IsRequired();
        builder.HasIndex(service => new { service.TenantId, service.Code }).IsUnique();
        builder.Property(service => service.Type).IsRequired();
        builder.Property(service => service.ShortName).HasMaxLength(Service.MaxShortNameLength).IsRequired();
        builder.HasIndex(service => new { service.TenantId, service.ShortcutNo }).IsUnique().HasFilter("shortcut_no IS NOT NULL").HasDatabaseName("ux_services_shortcut_no");
        builder.OwnsOne(service => service.Name, CatalogueAMapping.Bilingual<Service>("name"));
        builder.Navigation(service => service.Name).IsRequired();
        builder.Property(service => service.ReportName).HasMaxLength(Service.MaxReportNameLength);
        builder.Property(service => service.Loinc).HasMaxLength(Service.MaxReferenceCodeLength);
        builder.Property(service => service.Cpt).HasMaxLength(Service.MaxReferenceCodeLength);
        builder.Property(service => service.Header).HasMaxLength(Service.MaxHeaderLength);
        builder.Property(service => service.Aliases).IsRequired();
        builder.Property(service => service.Precaution).HasMaxLength(Service.MaxPrecautionLength);
        builder.Property(service => service.Gender).IsRequired();
        builder.Property(service => service.ProfileSum).HasPrecision(12, 4);
        builder.Property(service => service.Sequence).IsRequired();
        builder.Property(service => service.IsActive).IsRequired();
        builder.Property(service => service.Revision).IsRequired();
        builder.HasIndex(service => new { service.TenantId, service.Type, service.IsActive });
        builder.Ignore(service => service.CarriesSample);

        builder.HasOne<ClinicalGroup>().WithMany().HasForeignKey(service => new { service.TenantId, service.ClinicalGroupId }).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SampleType>().WithMany().HasForeignKey(service => new { service.TenantId, service.SampleTypeId }).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SampleCondition>().WithMany().HasForeignKey(service => new { service.TenantId, service.SampleConditionId }).IsRequired(false).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_services_sample_condition");

        builder.HasMany(service => service.Members).WithOne()
            .HasForeignKey(member => new { member.TenantId, member.ServiceId })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(service => service.Members).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

/// <summary><c>app.service_members</c>: the member points at a test or a service by kind and id (no FK; the handler checks it).</summary>
internal sealed class ServiceMemberConfiguration : IEntityTypeConfiguration<ServiceMember>
{
    public void Configure(EntityTypeBuilder<ServiceMember> builder)
    {
        builder.ToTable("service_members", AppDbContext.AppSchema);

        builder.Property(member => member.Kind).IsRequired();
        builder.Property(member => member.MemberId).IsRequired();
        builder.Property(member => member.Order).IsRequired();
        builder.HasIndex(member => new { member.TenantId, member.MemberId });
        builder.HasIndex(member => new { member.TenantId, member.ServiceId, member.Kind, member.MemberId }).IsUnique().HasDatabaseName("ux_service_members_member");
        builder.HasIndex(member => new { member.TenantId, member.ServiceId, member.Order }).IsUnique().HasDatabaseName("ux_service_members_order");
    }
}

/// <summary><c>app.tests</c> with <c>app.test_range_bands</c>, <c>app.test_list_values</c> and <c>app.test_paragraphs</c>.</summary>
internal sealed class LabTestConfiguration : IEntityTypeConfiguration<LabTest>
{
    public void Configure(EntityTypeBuilder<LabTest> builder)
    {
        builder.ToTable("tests", AppDbContext.AppSchema);

        builder.Property(test => test.Code).HasMaxLength(LabTest.MaxCodeLength).IsRequired();
        builder.HasIndex(test => new { test.TenantId, test.Code }).IsUnique();
        builder.OwnsOne(test => test.Name, CatalogueAMapping.Bilingual<LabTest>("name"));
        builder.Navigation(test => test.Name).IsRequired();
        builder.Property(test => test.ReportName).HasMaxLength(LabTest.MaxReportNameLength);
        builder.Property(test => test.Loinc).HasMaxLength(LabTest.MaxReferenceCodeLength);
        builder.Property(test => test.Cpt).HasMaxLength(LabTest.MaxReferenceCodeLength);
        builder.Property(test => test.Type).IsRequired();
        builder.Property(test => test.Gender).IsRequired();
        builder.Property(test => test.Methodology).HasMaxLength(LabTest.MaxMethodologyLength);
        builder.Property(test => test.Prefix).HasMaxLength(LabTest.MaxPrefixLength);
        builder.Property(test => test.FrequencyAction).IsRequired();
        builder.Property(test => test.DeltaPercent).HasPrecision(9, 3);
        builder.Property(test => test.FormulaExpression).HasMaxLength(LabTest.MaxFormulaLength);
        builder.Property(test => test.FormulaReferences).IsRequired();
        builder.Property(test => test.IsActive).IsRequired();
        builder.Property(test => test.Revision).IsRequired();
        builder.HasIndex(test => new { test.TenantId, test.ClinicalGroupId, test.IsActive });
        builder.Ignore(test => test.Timing);

        builder.HasOne<ClinicalGroup>().WithMany().HasForeignKey(test => new { test.TenantId, test.ClinicalGroupId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SampleType>().WithMany().HasForeignKey(test => new { test.TenantId, test.SampleTypeId }).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SampleCondition>().WithMany().HasForeignKey(test => new { test.TenantId, test.SampleConditionId }).IsRequired(false).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_tests_sample_condition");
        builder.HasOne<MeasurementUnit>().WithMany().HasForeignKey(test => new { test.TenantId, test.UnitId }).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(test => test.RangeBands).WithOne().HasForeignKey(band => new { band.TenantId, band.LabTestId }).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(test => test.ListValues).WithOne().HasForeignKey(value => new { value.TenantId, value.LabTestId }).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(test => test.Paragraphs).WithOne().HasForeignKey(paragraph => new { paragraph.TenantId, paragraph.LabTestId }).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(test => test.RangeBands).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        builder.Navigation(test => test.ListValues).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        builder.Navigation(test => test.Paragraphs).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

internal sealed class RangeBandConfiguration : IEntityTypeConfiguration<RangeBand>
{
    public void Configure(EntityTypeBuilder<RangeBand> builder)
    {
        builder.ToTable("test_range_bands", AppDbContext.AppSchema);

        builder.Property(band => band.Sex).IsRequired();
        builder.Property(band => band.AgeUnit).IsRequired();
        builder.Property(band => band.Low).HasPrecision(18, 6);
        builder.Property(band => band.High).HasPrecision(18, 6);
        builder.Property(band => band.PanicLow).HasPrecision(18, 6);
        builder.Property(band => band.PanicHigh).HasPrecision(18, 6);
        builder.Property(band => band.ReportableLow).HasPrecision(18, 6);
        builder.Property(band => band.ReportableHigh).HasPrecision(18, 6);
        builder.Property(band => band.TextRange).HasMaxLength(RangeBand.MaxTextRangeLength);
        builder.Property(band => band.DisplayOrder).IsRequired();
        builder.Ignore(band => band.HasLimits);

        builder.HasOne<PatientCondition>().WithMany().HasForeignKey(band => new { band.TenantId, band.PatientConditionId }).IsRequired(false).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_test_range_bands_patient_condition");   // the conventional name would exceed 63 characters
        builder.HasIndex(band => new { band.TenantId, band.PatientConditionId }).HasDatabaseName("ix_test_range_bands_patient_condition");
    }
}

internal sealed class ListValueConfiguration : IEntityTypeConfiguration<ListValue>
{
    public void Configure(EntityTypeBuilder<ListValue> builder)
    {
        builder.ToTable("test_list_values", AppDbContext.AppSchema);

        builder.Property(value => value.Value).HasMaxLength(ListValue.MaxValueLength).IsRequired();
        builder.Property(value => value.Colour).HasMaxLength(7);
        builder.Property(value => value.DisplayOrder).IsRequired();
    }
}

internal sealed class TestParagraphConfiguration : IEntityTypeConfiguration<TestParagraph>
{
    public void Configure(EntityTypeBuilder<TestParagraph> builder)
    {
        builder.ToTable("test_paragraphs", AppDbContext.AppSchema);

        builder.Property(paragraph => paragraph.Title).HasMaxLength(TestParagraph.MaxTitleLength).IsRequired();
        builder.Property(paragraph => paragraph.Text).HasMaxLength(TestParagraph.MaxTextLength);
        builder.Property(paragraph => paragraph.DisplayOrder).IsRequired();
    }
}

internal static class CatalogueAMapping
{
    /// <summary>A <see cref="LocalizedText"/> split into <c>&lt;prefix&gt;_en</c> / <c>&lt;prefix&gt;_ar</c> on the owner's table.</summary>
    public static Action<OwnedNavigationBuilder<TOwner, LocalizedText>> Bilingual<TOwner>(string prefix)
        where TOwner : class =>
        name =>
        {
            name.Property(text => text.En).HasColumnName($"{prefix}_en").HasMaxLength(LocalizedText.MaxLength).IsRequired();
            name.Property(text => text.Ar).HasColumnName($"{prefix}_ar").HasMaxLength(LocalizedText.MaxLength).IsRequired();
        };
}
