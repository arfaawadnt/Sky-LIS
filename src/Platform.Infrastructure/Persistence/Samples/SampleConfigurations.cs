using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Samples;

namespace Platform.Infrastructure.Persistence.Samples;

/// <summary>
/// <c>app.samples</c> (MODULES.md §5 wave-2 table contract: <c>id, tenant_id, tube_id, sample_number, visit_id,
/// clinical_group_id, sample_type_id, status, collected_at_utc, received_at_utc</c> are read by other modules): key
/// <c>(tenant_id, id)</c> by convention, the sample number unique per tenant (INV-SMP-002), one sample per tube and
/// generation (INV-SMP-001), the last rejection as owned columns, RLS declared in the creating migration.
/// </summary>
internal sealed class SampleConfiguration : IEntityTypeConfiguration<Sample>
{
    public void Configure(EntityTypeBuilder<Sample> builder)
    {
        builder.ToTable("samples", AppDbContext.AppSchema);

        builder.Property(sample => sample.TubeId).IsRequired();
        builder.Property(sample => sample.Generation).IsRequired();
        builder.Property(sample => sample.SampleNumber).HasMaxLength(Sample.MaxSampleNumberLength).IsRequired();
        builder.Property(sample => sample.VisitId).IsRequired();
        builder.Property(sample => sample.Accession).HasMaxLength(Sample.MaxAccessionLength).IsRequired();
        builder.Property(sample => sample.PatientId).IsRequired();
        builder.Property(sample => sample.BranchId).IsRequired();
        builder.Property(sample => sample.ClinicalGroupId).IsRequired();
        builder.Property(sample => sample.SampleTypeId).IsRequired();
        builder.Property(sample => sample.ServiceIds).IsRequired();   // uuid[]
        builder.Property(sample => sample.Status).IsRequired();
        builder.Property(sample => sample.OrderedAtUtc).IsRequired();
        builder.Property(sample => sample.StatusChangedAtUtc).IsRequired();
        builder.Property(sample => sample.CollectedOutside).IsRequired();   // LIS-CR-008 C-02, default false; backfill in Registration.migration.sql
        builder.Property(sample => sample.CancellationReason).HasMaxLength(Sample.MaxReasonLength);

        builder.OwnsOne(sample => sample.Rejection, rejection =>
        {
            rejection.Property(value => value.ReasonId).HasColumnName("rejection_reason_id");
            rejection.Property(value => value.ReasonNameEn).HasColumnName("rejection_reason_name_en").HasMaxLength(SampleRejection.MaxReasonNameLength);
            rejection.Property(value => value.ReasonNameAr).HasColumnName("rejection_reason_name_ar").HasMaxLength(SampleRejection.MaxReasonNameLength);
            rejection.Property(value => value.Note).HasColumnName("rejection_note").HasMaxLength(Sample.MaxNoteLength);
            rejection.Property(value => value.AtUtc).HasColumnName("rejected_at_utc");
            rejection.Property(value => value.ByUserId).HasColumnName("rejected_by_user_id");
            rejection.Property(value => value.BranchId).HasColumnName("rejected_branch_id");
        });

        builder.HasMany(sample => sample.Events)
            .WithOne()
            .HasForeignKey(step => new { step.TenantId, step.SampleId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(sample => sample.Events).HasField("_events").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(sample => new { sample.TenantId, sample.SampleNumber }).IsUnique();
        builder.HasIndex(sample => new { sample.TenantId, sample.TubeId, sample.Generation }).IsUnique();
        builder.HasIndex(sample => new { sample.TenantId, sample.VisitId });
        builder.HasIndex(sample => new { sample.TenantId, sample.Accession });
        builder.HasIndex(sample => new { sample.TenantId, sample.OrderedAtUtc });
        builder.HasIndex(sample => new { sample.TenantId, sample.Status, sample.StatusChangedAtUtc });

        builder.Ignore(sample => sample.IsLive);
        builder.Ignore(sample => sample.IsResultEntryAllowed);
    }
}

/// <summary><c>app.sample_events</c>: the timeline (CP-17), keyed <c>(tenant_id, id)</c>, composite FK to the sample, ordered by <c>sequence</c>; append-only by the aggregate.</summary>
internal sealed class SampleEventConfiguration : IEntityTypeConfiguration<SampleEvent>
{
    public void Configure(EntityTypeBuilder<SampleEvent> builder)
    {
        builder.ToTable("sample_events", AppDbContext.AppSchema);

        builder.Property(step => step.SampleId).IsRequired();
        builder.Property(step => step.Sequence).IsRequired();
        builder.Property(step => step.Action).IsRequired();
        builder.Property(step => step.Status).IsRequired();
        builder.Property(step => step.AtUtc).IsRequired();
        builder.Property(step => step.Note).HasMaxLength(Sample.MaxReasonLength);

        // the timeline order; unique, so a transition on a sample loaded without its steps is refused instead of misordered
        builder.HasIndex(step => new { step.TenantId, step.SampleId, step.Sequence }).IsUnique();
    }
}
