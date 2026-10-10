using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Registration;

namespace Platform.Infrastructure.Persistence.Registration;

// Tables of the Registration module (intake LIS-INT-REG-001 §5). app.visits, app.visit_lines and app.batches carry the
// MODULES.md §5 contract columns other modules read by raw SQL; names are kept. Keys (tenant_id, id), the tenant query
// filter, the xmin token of the roots and the enum CHECK constraints come from the conventions; RLS from the wave
// migration; the no-delete triggers from Registration.migration.sql.

internal static class RegistrationMoney
{
    public const int Precision = 12;
    public const int Scale = 2;

    public static PropertyBuilder<decimal> Money(this PropertyBuilder<decimal> property) => property.HasPrecision(Precision, Scale).IsRequired();
}

/// <summary>
/// <c>app.visits</c>: accession unique per tenant (INV-REG-001); composite FKs to the batch and booking of this module. The
/// patient file, payer and tier are validated by the handlers through their read ports (no FK across modules in this wave).
/// </summary>
internal sealed class VisitConfiguration : IEntityTypeConfiguration<Visit>
{
    public void Configure(EntityTypeBuilder<Visit> builder)
    {
        builder.ToTable("visits", AppDbContext.AppSchema, table =>
        {
            table.HasCheckConstraint("ck_visits_totals", "total_list_price >= 0 AND discount_total >= 0 AND patient_share_total >= 0 AND payer_share_total >= 0");
            table.HasCheckConstraint("ck_visits_completion", "(status = 'Completed') = (completed_at_utc IS NOT NULL)");
        });

        builder.Property(visit => visit.Accession).HasMaxLength(Visit.MaxAccessionLength).IsRequired();
        builder.HasIndex(visit => new { visit.TenantId, visit.Accession }).IsUnique();

        builder.Property(visit => visit.PatientId).IsRequired();
        builder.Property(visit => visit.BranchId).IsRequired();   // the session branch; FK to app.branches is the Organization package's
        builder.Property(visit => visit.LocationType).IsRequired();
        builder.Property(visit => visit.TreatmentNo).HasMaxLength(Visit.MaxTreatmentNoLength);
        builder.Property(visit => visit.InsuranceId).HasMaxLength(Visit.MaxInsuranceIdLength);
        builder.Property(visit => visit.PayerId).IsRequired();
        builder.Property(visit => visit.ContractTierId).IsRequired();
        builder.Property(visit => visit.Status).IsRequired();
        builder.Property(visit => visit.RegisteredAtUtc).IsRequired();
        builder.Property(visit => visit.RegisteredByUserId).IsRequired();
        builder.Property(visit => visit.UtcOffsetMinutes).IsRequired();
        builder.Property(visit => visit.TotalListPrice).Money();
        builder.Property(visit => visit.DiscountTotal).Money();
        builder.Property(visit => visit.PatientShareTotal).Money();
        builder.Property(visit => visit.PayerShareTotal).Money();
        builder.Property(visit => visit.DiscountValue).Money();
        builder.Property(visit => visit.DiscountReason).HasMaxLength(Visit.MaxReasonLength);
        builder.Property(visit => visit.Revision).IsRequired();

        builder.HasIndex(visit => new { visit.TenantId, visit.RegisteredAtUtc });
        builder.HasIndex(visit => new { visit.TenantId, visit.PatientId, visit.RegisteredAtUtc });
        builder.HasIndex(visit => new { visit.TenantId, visit.BranchId, visit.RegisteredAtUtc });
        builder.HasIndex(visit => new { visit.TenantId, visit.PayerId });
        builder.HasIndex(visit => new { visit.TenantId, visit.BatchId }).HasFilter("batch_id IS NOT NULL");
        builder.HasIndex(visit => new { visit.TenantId, visit.BookingId }).HasFilter("booking_id IS NOT NULL");

        builder.HasOne<Batch>().WithMany().HasForeignKey(visit => new { visit.TenantId, visit.BatchId }).IsRequired(false).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_visits_batch");
        builder.HasOne<Booking>().WithMany().HasForeignKey(visit => new { visit.TenantId, visit.BookingId }).IsRequired(false).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_visits_booking");

        builder.HasMany(visit => visit.Lines).WithOne().HasForeignKey(line => new { line.TenantId, line.VisitId }).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_visit_lines_visit");
        builder.Navigation(visit => visit.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(visit => visit.Tubes).WithOne().HasForeignKey(tube => new { tube.TenantId, tube.VisitId }).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_visit_tubes_visit");
        builder.Navigation(visit => visit.Tubes).HasField("_tubes").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(visit => visit.PreparationAnswers).WithOne().HasForeignKey(answer => new { answer.TenantId, answer.VisitId }).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_visit_preparation_answers_visit");
        builder.Navigation(visit => visit.PreparationAnswers).HasField("_answers").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(visit => visit.Transfers).WithOne().HasForeignKey(transfer => new { transfer.TenantId, transfer.VisitId }).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_visit_transfers_visit");
        builder.Navigation(visit => visit.Transfers).HasField("_transfers").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(visit => visit.LiveServiceIds);
    }
}

/// <summary><c>app.visit_lines</c> (MODULES.md §5): a cancelled line keeps reason, actor and time (CHECK); a member points at its package; a line points at its tube.</summary>
internal sealed class VisitLineConfiguration : IEntityTypeConfiguration<VisitLine>
{
    public void Configure(EntityTypeBuilder<VisitLine> builder)
    {
        builder.ToTable("visit_lines", AppDbContext.AppSchema, table =>
        {
            table.HasCheckConstraint("ck_visit_lines_amounts", "list_price >= 0 AND patient_share >= 0 AND payer_share >= 0 AND discount >= 0");
            table.HasCheckConstraint("ck_visit_lines_cancellation", "(status = 'Cancelled') = (cancelled_at_utc IS NOT NULL AND cancellation_reason IS NOT NULL AND cancelled_by_user_id IS NOT NULL)");
            table.HasCheckConstraint("ck_visit_lines_member", "(kind = 'PackageMember') = (parent_line_id IS NOT NULL)");
        });

        builder.Property(line => line.VisitId).IsRequired();
        builder.Property(line => line.ServiceId).IsRequired();
        builder.Property(line => line.ServiceCode).HasMaxLength(VisitLine.MaxServiceCodeLength).IsRequired();
        builder.Property(line => line.ServiceNameEn).HasMaxLength(VisitLine.MaxServiceNameLength).IsRequired();
        builder.Property(line => line.ServiceNameAr).HasMaxLength(VisitLine.MaxServiceNameLength).IsRequired();
        builder.Property(line => line.ServiceType).HasMaxLength(VisitLine.MaxServiceTypeLength).IsRequired();
        builder.Property(line => line.Kind).IsRequired();
        builder.Property(line => line.Status).IsRequired();
        builder.Property(line => line.Origin).IsRequired();
        builder.Property(line => line.ListPrice).Money();
        builder.Property(line => line.PatientShare).Money();
        builder.Property(line => line.PayerShare).Money();
        builder.Property(line => line.Discount).Money();
        builder.Property(line => line.TestIds).IsRequired();
        builder.Property(line => line.Sequence).IsRequired();
        builder.Property(line => line.OrderedAtUtc).IsRequired();
        builder.Property(line => line.OrderedByUserId).IsRequired();
        builder.Property(line => line.CancellationReason).HasMaxLength(Visit.MaxReasonLength);

        builder.HasIndex(line => new { line.TenantId, line.VisitId }).HasDatabaseName("ix_visit_lines_visit");
        builder.HasIndex(line => new { line.TenantId, line.TubeId }).HasFilter("tube_id IS NOT NULL").HasDatabaseName("ix_visit_lines_tube");
        builder.HasIndex(line => new { line.TenantId, line.ServiceId }).HasDatabaseName("ix_visit_lines_service");
        builder.HasIndex(line => new { line.TenantId, line.CancelledAtUtc }).HasFilter("status = 'Cancelled'").HasDatabaseName("ix_visit_lines_cancelled");

        builder.HasOne<VisitLine>().WithMany().HasForeignKey(line => new { line.TenantId, line.ParentLineId }).IsRequired(false).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_visit_lines_parent");
        builder.HasOne<VisitTube>().WithMany().HasForeignKey(line => new { line.TenantId, line.TubeId }).IsRequired(false).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_visit_lines_tube");

        builder.Ignore(line => line.IsLive);
        builder.Ignore(line => line.GrossPatientShare);
        builder.Ignore(line => line.IsTopLevel);
    }
}

/// <summary><c>app.visit_tubes</c>: sample numbers unique per tenant (BR-01).</summary>
internal sealed class VisitTubeConfiguration : IEntityTypeConfiguration<VisitTube>
{
    public void Configure(EntityTypeBuilder<VisitTube> builder)
    {
        builder.ToTable("visit_tubes", AppDbContext.AppSchema);

        builder.Property(tube => tube.VisitId).IsRequired();
        builder.Property(tube => tube.SampleNumber).HasMaxLength(VisitTube.MaxSampleNumberLength).IsRequired();
        builder.HasIndex(tube => new { tube.TenantId, tube.SampleNumber }).IsUnique().HasDatabaseName("ux_visit_tubes_sample_number");
        builder.Property(tube => tube.ClinicalGroupId).IsRequired();
        builder.Property(tube => tube.SampleTypeId).IsRequired();
        builder.Property(tube => tube.UnitCode).HasMaxLength(VisitTube.MaxUnitCodeLength).IsRequired();
        builder.Property(tube => tube.ClinicalGroupNameEn).HasMaxLength(VisitTube.MaxNameLength).IsRequired();
        builder.Property(tube => tube.ClinicalGroupNameAr).HasMaxLength(VisitTube.MaxNameLength).IsRequired();
        builder.Property(tube => tube.SampleTypeNameEn).HasMaxLength(VisitTube.MaxNameLength).IsRequired();
        builder.Property(tube => tube.SampleTypeNameAr).HasMaxLength(VisitTube.MaxNameLength).IsRequired();
        builder.Property(tube => tube.SampleConditionNameEn).HasMaxLength(VisitTube.MaxNameLength);
        builder.Property(tube => tube.SampleConditionNameAr).HasMaxLength(VisitTube.MaxNameLength);
        builder.Property(tube => tube.CreatedAtUtc).IsRequired();
        builder.HasIndex(tube => new { tube.TenantId, tube.VisitId }).HasDatabaseName("ix_visit_tubes_visit");

        builder.Ignore(tube => tube.Key);
    }
}

/// <summary><c>app.visit_preparation_answers</c>: one answer per question and visit.</summary>
internal sealed class VisitPreparationAnswerConfiguration : IEntityTypeConfiguration<VisitPreparationAnswer>
{
    public void Configure(EntityTypeBuilder<VisitPreparationAnswer> builder)
    {
        builder.ToTable("visit_preparation_answers", AppDbContext.AppSchema);

        builder.Property(answer => answer.VisitId).IsRequired();
        builder.Property(answer => answer.PreparationId).IsRequired();
        builder.Property(answer => answer.QuestionEn).HasMaxLength(VisitPreparationAnswer.MaxQuestionLength).IsRequired();
        builder.Property(answer => answer.QuestionAr).HasMaxLength(VisitPreparationAnswer.MaxQuestionLength).IsRequired();
        builder.Property(answer => answer.Answer).HasMaxLength(VisitPreparationAnswer.MaxAnswerLength).IsRequired();
        builder.Property(answer => answer.AnsweredAtUtc).IsRequired();
        builder.Property(answer => answer.AnsweredByUserId).IsRequired();
        builder.HasIndex(answer => new { answer.TenantId, answer.VisitId, answer.PreparationId }).IsUnique().HasDatabaseName("ux_visit_preparation_answers_question");
    }
}

/// <summary><c>app.visit_transfers</c>: the money-transfer log (CP-25), never changed.</summary>
internal sealed class VisitTransferConfiguration : IEntityTypeConfiguration<VisitTransfer>
{
    public void Configure(EntityTypeBuilder<VisitTransfer> builder)
    {
        builder.ToTable("visit_transfers", AppDbContext.AppSchema);

        builder.Property(transfer => transfer.VisitId).IsRequired();
        builder.Property(transfer => transfer.Kind).IsRequired();
        builder.Property(transfer => transfer.PatientShareBefore).Money();
        builder.Property(transfer => transfer.PatientShareAfter).Money();
        builder.Property(transfer => transfer.PayerShareBefore).Money();
        builder.Property(transfer => transfer.PayerShareAfter).Money();
        builder.Property(transfer => transfer.Reason).HasMaxLength(VisitTransfer.MaxReasonLength).IsRequired();
        builder.Property(transfer => transfer.ActorUserId).IsRequired();
        builder.Property(transfer => transfer.AtUtc).IsRequired();
        builder.HasIndex(transfer => new { transfer.TenantId, transfer.VisitId }).HasDatabaseName("ix_visit_transfers_visit");
        builder.HasIndex(transfer => new { transfer.TenantId, transfer.AtUtc }).HasDatabaseName("ix_visit_transfers_at");
    }
}

/// <summary><c>app.bookings</c> (CP-07): number unique; a converted booking names its visit (INV-REG-010).</summary>
internal sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("bookings", AppDbContext.AppSchema, table =>
        {
            table.HasCheckConstraint("ck_bookings_conversion", "(status = 'Converted') = (visit_id IS NOT NULL AND accession IS NOT NULL AND converted_at_utc IS NOT NULL)");
            table.HasCheckConstraint("ck_bookings_cancellation", "(status = 'Cancelled') = (cancellation_reason IS NOT NULL AND cancelled_at_utc IS NOT NULL)");
        });

        builder.Property(booking => booking.Number).HasMaxLength(Booking.MaxNumberLength).IsRequired();
        builder.HasIndex(booking => new { booking.TenantId, booking.Number }).IsUnique();
        builder.Property(booking => booking.BranchId).IsRequired();
        builder.Property(booking => booking.PatientName).HasMaxLength(Booking.MaxNameLength);
        builder.Property(booking => booking.Gender).HasMaxLength(10);
        builder.Property(booking => booking.Mobile).HasMaxLength(Booking.MaxMobileLength);
        builder.Property(booking => booking.Address).HasMaxLength(Booking.MaxAddressLength).IsRequired();
        builder.Property(booking => booking.VisitAtUtc).IsRequired();
        builder.Property(booking => booking.PayerId).IsRequired();
        builder.Property(booking => booking.ContractTierId).IsRequired();
        builder.Property(booking => booking.ServiceIds).IsRequired();
        builder.Property(booking => booking.Notes).HasMaxLength(Booking.MaxNotesLength);
        builder.Property(booking => booking.Status).IsRequired();
        builder.Property(booking => booking.Accession).HasMaxLength(Visit.MaxAccessionLength);
        builder.Property(booking => booking.CancellationReason).HasMaxLength(Booking.MaxReasonLength);

        builder.HasIndex(booking => new { booking.TenantId, booking.Status, booking.VisitAtUtc }).HasDatabaseName("ix_bookings_status_visit_at");
        builder.HasIndex(booking => new { booking.TenantId, booking.PatientId }).HasFilter("patient_id IS NOT NULL").HasDatabaseName("ix_bookings_patient");
        builder.HasIndex(booking => new { booking.TenantId, booking.PhlebotomistUserId }).HasFilter("phlebotomist_user_id IS NOT NULL").HasDatabaseName("ix_bookings_phlebotomist");

    }
}

/// <summary><c>app.batches</c> (MODULES.md §5): number unique; the members are the visits carrying <c>batch_id</c>.</summary>
internal sealed class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> builder)
    {
        builder.ToTable("batches", AppDbContext.AppSchema, table =>
            table.HasCheckConstraint("ck_batches_kind", "(kind = 'LabToLab') = (sending_lab_id IS NOT NULL)"));

        builder.Property(batch => batch.Number).HasMaxLength(Batch.MaxNumberLength).IsRequired();
        builder.HasIndex(batch => new { batch.TenantId, batch.Number }).IsUnique();
        builder.Property(batch => batch.Kind).IsRequired();
        builder.Property(batch => batch.BranchId).IsRequired();
        builder.Property(batch => batch.PayerId).IsRequired();
        builder.Property(batch => batch.ContractTierId).IsRequired();
        builder.Property(batch => batch.GroupName).HasMaxLength(Batch.MaxGroupNameLength);
        builder.Property(batch => batch.CourierEn).HasMaxLength(Batch.MaxCourierLength);
        builder.Property(batch => batch.CourierAr).HasMaxLength(Batch.MaxCourierLength);
        builder.Property(batch => batch.Notes).HasMaxLength(Batch.MaxNotesLength);
        builder.Property(batch => batch.RegisteredAtUtc).IsRequired();
        builder.Property(batch => batch.RegisteredByUserId).IsRequired();
        builder.Property(batch => batch.MemberCount).IsRequired();

        builder.HasIndex(batch => new { batch.TenantId, batch.RegisteredAtUtc }).HasDatabaseName("ix_batches_registered_at");
        builder.HasIndex(batch => new { batch.TenantId, batch.SendingLabId }).HasFilter("sending_lab_id IS NOT NULL").HasDatabaseName("ix_batches_sending_lab");

    }
}
