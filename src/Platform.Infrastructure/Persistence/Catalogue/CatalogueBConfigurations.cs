using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Catalogue;
using Platform.SharedKernel.Localization;

namespace Platform.Infrastructure.Persistence.Catalogue;

// EF configuration of the Catalogue B tables (intake LIS-INT-CTB-001 §5): schema app, key (tenant_id, id), tenant
// query filter, xmin concurrency and enum CHECK constraints by convention; RLS declared in the creating migration.
// Id sets are uuid[] columns (precedent: tenant_memberships.branch_ids), options a text[]. Case-insensitive name
// uniqueness is enforced by the expression indexes in CatalogueB.migration.sql.

internal static class CatalogueBMapping
{
    /// <summary>A bilingual text owned on the owner's table as <c>&lt;prefix&gt;_en</c> / <c>&lt;prefix&gt;_ar</c>.</summary>
    public static void Bilingual<TEntity>(EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, LocalizedText?>> navigation, string prefix, int maxLength, bool required = true)
        where TEntity : class
    {
        builder.OwnsOne(navigation, text =>
        {
            text.Property(value => value.En).HasColumnName(prefix + "_en").HasMaxLength(maxLength).IsRequired(required);
            text.Property(value => value.Ar).HasColumnName(prefix + "_ar").HasMaxLength(maxLength).IsRequired(required);
        });
        builder.Navigation(navigation).IsRequired(required);
    }

    public static void Record<TEntity>(EntityTypeBuilder<TEntity> builder, string table)
        where TEntity : CatalogueBRecord
    {
        builder.ToTable(table, AppDbContext.AppSchema);
        builder.Property(record => record.IsActive).IsRequired();
        builder.HasIndex(record => new { record.TenantId, record.IsActive });
    }
}

internal sealed class PreparationConfiguration : IEntityTypeConfiguration<Preparation>
{
    public void Configure(EntityTypeBuilder<Preparation> builder)
    {
        CatalogueBMapping.Record(builder, "preparations");
        builder.Property(preparation => preparation.Code).HasMaxLength(CatalogueBGuard.MaxCodeLength).IsRequired();
        builder.HasIndex(preparation => new { preparation.TenantId, preparation.Code }).IsUnique();
        CatalogueBMapping.Bilingual(builder, preparation => preparation.Question, "question", CatalogueBGuard.MaxLongTextLength);
        builder.Property(preparation => preparation.AnswerType).IsRequired();
        builder.Property(preparation => preparation.Gender).IsRequired();
        builder.Property(preparation => preparation.Options).IsRequired();
        builder.Property(preparation => preparation.ServiceIds).IsRequired();
    }
}

internal sealed class CommentTemplateConfiguration : IEntityTypeConfiguration<CommentTemplate>
{
    public void Configure(EntityTypeBuilder<CommentTemplate> builder)
    {
        CatalogueBMapping.Record(builder, "comment_templates");
        builder.Property(template => template.ShortName).HasMaxLength(CommentTemplate.MaxShortNameLength).IsRequired();
        builder.HasIndex(template => new { template.TenantId, template.ShortName }).IsUnique();
        CatalogueBMapping.Bilingual(builder, template => template.Text, "text", CatalogueBGuard.MaxLongTextLength);
        builder.HasIndex(template => new { template.TenantId, template.ClinicalGroupId });   // FK to app.clinical_groups arrives with Catalogue A's wave integration
    }
}

internal sealed class DoctorConfiguration : IEntityTypeConfiguration<Doctor>
{
    public void Configure(EntityTypeBuilder<Doctor> builder)
    {
        CatalogueBMapping.Record(builder, "doctors");
        builder.ToTable(table => table.HasCheckConstraint("ck_doctors_share_percent_range", "share_percent >= 0 AND share_percent <= 100"));
        builder.Property(doctor => doctor.Code).HasMaxLength(CatalogueBGuard.MaxCodeLength).IsRequired();
        builder.HasIndex(doctor => new { doctor.TenantId, doctor.Code }).IsUnique();
        CatalogueBMapping.Bilingual(builder, doctor => doctor.Name, "name", CatalogueBGuard.MaxNameLength);
        builder.Property(doctor => doctor.Specialty).HasMaxLength(CatalogueBGuard.MaxFreeTextLength);
        builder.Property(doctor => doctor.Occupation).HasMaxLength(CatalogueBGuard.MaxFreeTextLength);
        builder.Property(doctor => doctor.BranchIds).IsRequired();   // uuid[]; empty = every branch
        builder.Property(doctor => doctor.SharePercent).HasPrecision(5, 2).IsRequired();
        builder.Property(doctor => doctor.Phone).HasMaxLength(CatalogueBGuard.MaxPhoneLength);
    }
}

internal sealed class DiagnosisConfiguration : IEntityTypeConfiguration<Diagnosis>
{
    public void Configure(EntityTypeBuilder<Diagnosis> builder)
    {
        CatalogueBMapping.Record(builder, "diagnoses");
        builder.Property(diagnosis => diagnosis.IcdCode).HasMaxLength(CatalogueBGuard.MaxCodeLength).IsRequired();
        builder.HasIndex(diagnosis => new { diagnosis.TenantId, diagnosis.IcdCode }).IsUnique();
        CatalogueBMapping.Bilingual(builder, diagnosis => diagnosis.Name, "name", CatalogueBGuard.MaxNameLength);
        builder.Property(diagnosis => diagnosis.ServiceIds).IsRequired();
    }
}

internal sealed class SendingLabConfiguration : IEntityTypeConfiguration<SendingLab>
{
    public void Configure(EntityTypeBuilder<SendingLab> builder)
    {
        CatalogueBMapping.Record(builder, "sending_labs");
        CatalogueBMapping.Bilingual(builder, lab => lab.Name, "name", CatalogueBGuard.MaxNameLength);
        CatalogueBMapping.Bilingual(builder, lab => lab.Courier, "courier", CatalogueBGuard.MaxNameLength);
        builder.Property(lab => lab.PayerId).IsRequired();          // Pricing (same wave): validated by the handler, FK added by the integrator
        builder.Property(lab => lab.ContractTierId).IsRequired();
        builder.Property(lab => lab.Phone).HasMaxLength(CatalogueBGuard.MaxPhoneLength);
        builder.HasIndex(lab => new { lab.TenantId, lab.PayerId });
        builder.HasOne<Doctor>().WithMany().HasForeignKey(lab => new { lab.TenantId, lab.DoctorId }).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ShortcutSetConfiguration : IEntityTypeConfiguration<ShortcutSet>
{
    public void Configure(EntityTypeBuilder<ShortcutSet> builder)
    {
        CatalogueBMapping.Record(builder, "shortcut_sets");
        builder.ToTable(table => table.HasCheckConstraint("ck_shortcut_sets_services_present", "cardinality(service_ids) >= 1"));
        builder.Property(set => set.Code).HasMaxLength(CatalogueBGuard.MaxCodeLength).IsRequired();
        builder.HasIndex(set => new { set.TenantId, set.Code }).IsUnique();
        CatalogueBMapping.Bilingual(builder, set => set.Name, "name", CatalogueBGuard.MaxNameLength);
        builder.Property(set => set.ServiceIds).IsRequired();
    }
}

internal sealed class TestRuleConfiguration : IEntityTypeConfiguration<TestRule>
{
    public void Configure(EntityTypeBuilder<TestRule> builder)
    {
        CatalogueBMapping.Record(builder, "test_rules");
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_test_rules_sides",
            "cardinality(trigger_service_ids) >= 1 AND cardinality(added_service_ids) >= 1 AND NOT (trigger_service_ids && added_service_ids)"));
        CatalogueBMapping.Bilingual(builder, rule => rule.Name, "name", CatalogueBGuard.MaxNameLength);
        builder.Property(rule => rule.TriggerServiceIds).IsRequired();
        builder.Property(rule => rule.AddedServiceIds).IsRequired();
        builder.HasIndex(rule => new { rule.TenantId, rule.ClinicalGroupId });
    }
}

internal sealed class ReasonConfiguration : IEntityTypeConfiguration<Reason>
{
    public void Configure(EntityTypeBuilder<Reason> builder)
    {
        CatalogueBMapping.Record(builder, "reasons");
        builder.Property(reason => reason.Kind).IsRequired();
        CatalogueBMapping.Bilingual(builder, reason => reason.Name, "name", CatalogueBGuard.MaxNameLength);
        builder.HasIndex(reason => new { reason.TenantId, reason.Kind });
    }
}

internal sealed class ReportTemplateConfiguration : IEntityTypeConfiguration<ReportTemplate>
{
    public void Configure(EntityTypeBuilder<ReportTemplate> builder)
    {
        CatalogueBMapping.Record(builder, "report_templates");
        CatalogueBMapping.Bilingual(builder, template => template.Name, "name", CatalogueBGuard.MaxNameLength);
        builder.Property(template => template.Type).IsRequired();
        builder.Property(template => template.Language).IsRequired();
        builder.Property(template => template.ShowQr).IsRequired();
        CatalogueBMapping.Bilingual(builder, template => template.HeaderText, "header_text", CatalogueBGuard.MaxLongTextLength, required: false);
        CatalogueBMapping.Bilingual(builder, template => template.FooterText, "footer_text", CatalogueBGuard.MaxLongTextLength, required: false);
        builder.Property(template => template.Design);   // nullable string, CHECK by the enum convention (LIS-CR-010)
        builder.HasIndex(template => new { template.TenantId, template.Type });
    }
}

internal sealed class DrugTypeConfiguration : IEntityTypeConfiguration<DrugType>
{
    public void Configure(EntityTypeBuilder<DrugType> builder)
    {
        CatalogueBMapping.Record(builder, "drug_types");
        CatalogueBMapping.Bilingual(builder, drugType => drugType.Name, "name", CatalogueBGuard.MaxNameLength);
    }
}

internal sealed class CommercialNameConfiguration : IEntityTypeConfiguration<CommercialName>
{
    public void Configure(EntityTypeBuilder<CommercialName> builder)
    {
        CatalogueBMapping.Record(builder, "commercial_names");
        CatalogueBMapping.Bilingual(builder, commercialName => commercialName.Name, "name", CatalogueBGuard.MaxNameLength);
        builder.HasOne<DrugType>().WithMany().HasForeignKey(commercialName => new { commercialName.TenantId, commercialName.DrugTypeId }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AntibioticConfiguration : IEntityTypeConfiguration<Antibiotic>
{
    public void Configure(EntityTypeBuilder<Antibiotic> builder)
    {
        CatalogueBMapping.Record(builder, "antibiotics");
        builder.Property(antibiotic => antibiotic.Code).HasMaxLength(CatalogueBGuard.MaxCodeLength).IsRequired();
        builder.HasIndex(antibiotic => new { antibiotic.TenantId, antibiotic.Code }).IsUnique();
        CatalogueBMapping.Bilingual(builder, antibiotic => antibiotic.Name, "name", CatalogueBGuard.MaxNameLength);
        builder.Property(antibiotic => antibiotic.Abbreviation).HasMaxLength(Antibiotic.MaxAbbreviationLength).IsRequired();
        builder.HasOne<CommercialName>().WithMany().HasForeignKey(antibiotic => new { antibiotic.TenantId, antibiotic.CommercialNameId }).IsRequired(false).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_antibiotics_commercial_names");
    }
}

internal sealed class OrganismConfiguration : IEntityTypeConfiguration<Organism>
{
    public void Configure(EntityTypeBuilder<Organism> builder)
    {
        CatalogueBMapping.Record(builder, "organisms");
        builder.Property(organism => organism.Code).HasMaxLength(CatalogueBGuard.MaxCodeLength).IsRequired();
        builder.HasIndex(organism => new { organism.TenantId, organism.Code }).IsUnique();
        CatalogueBMapping.Bilingual(builder, organism => organism.Name, "name", CatalogueBGuard.MaxNameLength);
    }
}

internal sealed class SensitivityValueConfiguration : IEntityTypeConfiguration<SensitivityValue>
{
    public void Configure(EntityTypeBuilder<SensitivityValue> builder)
    {
        CatalogueBMapping.Record(builder, "sensitivity_values");
        builder.ToTable(table => table.HasCheckConstraint("ck_sensitivity_values_display_order_range", "display_order >= 0 AND display_order <= 999"));
        builder.Property(value => value.Code).HasMaxLength(SensitivityValue.MaxCodeLength).IsRequired();
        builder.HasIndex(value => new { value.TenantId, value.Code }).IsUnique();
        CatalogueBMapping.Bilingual(builder, value => value.Name, "name", CatalogueBGuard.MaxNameLength);
        builder.Property(value => value.DisplayOrder).IsRequired();
    }
}
