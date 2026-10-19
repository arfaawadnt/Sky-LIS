using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Patients;

namespace Platform.Infrastructure.Persistence.Patients;

/// <summary>
/// <c>app.patients</c>: key <c>(tenant_id, id)</c>, xmin and the tenant filter by convention; number unique per tenant
/// (INV-PAT-001); the merge pointer is a composite self-FK (01 §4.2 item 3); CHECK constraints back INV-PAT-002/004/005.
/// The patient condition (<c>app.patient_conditions</c>, catalogue) and the governorate (<c>saas.governorates</c>) are
/// referenced by id without a FK (same-wave modules; MODULES.md §1). RLS is declared in the creating migration.
/// </summary>
internal sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    /// <summary>Normalised keys can be longer than the name (compatibility decomposition of ligatures).</summary>
    public const int MaxNameKeyLength = 400;

    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("patients", AppDbContext.AppSchema, table =>
        {
            table.HasCheckConstraint(
                "ck_patients_merge_consistency",
                "(status = 'Merged') = (merged_into_patient_id IS NOT NULL AND merged_at_utc IS NOT NULL AND merged_by_user_id IS NOT NULL AND merge_reason IS NOT NULL)");
            table.HasCheckConstraint("ck_patients_not_merged_into_self", "merged_into_patient_id IS NULL OR merged_into_patient_id <> id");
            table.HasCheckConstraint("ck_patients_birth_or_age", "date_of_birth IS NOT NULL OR (age_years IS NOT NULL AND age_recorded_on IS NOT NULL)");
        });

        builder.Property(patient => patient.Number).HasMaxLength(Patient.MaxNumberLength).IsRequired();
        builder.HasIndex(patient => new { patient.TenantId, patient.Number }).IsUnique();

        builder.Property(patient => patient.Title).IsRequired();
        builder.Property(patient => patient.FullName).HasMaxLength(Patient.MaxNameLength).IsRequired();
        builder.Property(patient => patient.NameAr).HasMaxLength(Patient.MaxNameLength);
        builder.Property(patient => patient.NameKey).HasMaxLength(MaxNameKeyLength).IsRequired();
        builder.Property(patient => patient.NameArKey).HasMaxLength(MaxNameKeyLength).IsRequired();
        builder.Property(patient => patient.Gender).IsRequired();
        builder.Property(patient => patient.RegisteredOn).IsRequired();
        builder.Property(patient => patient.Mobile).HasMaxLength(Patient.MaxMobileLength);
        builder.Property(patient => patient.SecondPhone).HasMaxLength(Patient.MaxMobileLength);   // C3 (change request 2026-10-19); city_id / district_id / area_id reference saas rows without a FK (A2)
        builder.Property(patient => patient.NationalId).HasMaxLength(Patient.MaxNationalIdLength);
        builder.Property(patient => patient.Nationality).HasMaxLength(Patient.MaxNationalityLength);
        builder.Property(patient => patient.Email).HasMaxLength(Patient.MaxEmailLength);
        builder.Property(patient => patient.City).HasMaxLength(Patient.MaxCityLength);
        builder.Property(patient => patient.Address).HasMaxLength(Patient.MaxAddressLength);
        builder.Property(patient => patient.Comments).HasColumnType("text");   // bound by the validator and the aggregate (2000)
        builder.Property(patient => patient.IsVip).IsRequired();
        builder.Property(patient => patient.Status).IsRequired();
        builder.Property(patient => patient.MergeReason).HasMaxLength(Patient.MaxReasonLength);

        builder.HasOne<Patient>().WithMany().HasForeignKey(patient => new { patient.TenantId, patient.MergedIntoPatientId }).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(patient => new { patient.TenantId, patient.Mobile });
        builder.HasIndex(patient => new { patient.TenantId, patient.SecondPhone });
        builder.HasIndex(patient => new { patient.TenantId, patient.NationalId });
        builder.HasIndex(patient => new { patient.TenantId, patient.NameKey });
        builder.HasIndex(patient => new { patient.TenantId, patient.Status });

        builder.Ignore(patient => patient.StatedAge);
        builder.Ignore(patient => patient.EstimatedDateOfBirth);
        builder.Ignore(patient => patient.DateOfBirthIsEstimated);
    }
}

/// <summary>
/// <c>app.patient_documents</c>: composite FK to the file (carries the tenant), storage key unique per tenant, the
/// deactivation record backed by a CHECK (INV-PAT-009). Content lives in the file store, never in the database.
/// </summary>
internal sealed class PatientDocumentConfiguration : IEntityTypeConfiguration<PatientDocument>
{
    public void Configure(EntityTypeBuilder<PatientDocument> builder)
    {
        builder.ToTable("patient_documents", AppDbContext.AppSchema, table =>
            table.HasCheckConstraint(
                "ck_patient_documents_deactivation",
                "is_active OR (deactivated_at_utc IS NOT NULL AND deactivated_by_user_id IS NOT NULL AND deactivation_reason IS NOT NULL)"));

        builder.HasOne<Patient>().WithMany().HasForeignKey(document => new { document.TenantId, document.PatientId }).OnDelete(DeleteBehavior.Restrict);

        builder.Property(document => document.FileName).HasMaxLength(PatientDocumentFiles.MaxFileNameLength).IsRequired();
        builder.Property(document => document.ContentType).HasMaxLength(PatientDocument.MaxContentTypeLength).IsRequired();
        builder.Property(document => document.SizeBytes).IsRequired();
        builder.Property(document => document.StorageKey).HasMaxLength(PatientDocument.MaxStorageKeyLength).IsRequired();
        builder.HasIndex(document => new { document.TenantId, document.StorageKey }).IsUnique();
        builder.Property(document => document.Sha256).HasMaxLength(PatientDocument.Sha256HexLength).IsFixedLength().IsRequired();
        builder.Property(document => document.Note).HasMaxLength(PatientDocument.MaxNoteLength);
        builder.Property(document => document.UploadedByUserId).IsRequired();
        builder.Property(document => document.UploadedAtUtc).IsRequired();
        builder.Property(document => document.IsActive).IsRequired();
        builder.Property(document => document.DeactivationReason).HasMaxLength(PatientDocument.MaxReasonLength);
    }
}
