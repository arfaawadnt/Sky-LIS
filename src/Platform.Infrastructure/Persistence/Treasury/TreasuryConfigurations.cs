using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Identity;
using Platform.Domain.Treasury;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Infrastructure.Persistence.Treasury;

/// <summary><c>app.treasuries</c>: key <c>(tenant_id, id)</c> by convention, code unique per tenant (INV-TRS-001), RLS declared in the creating migration.</summary>
internal sealed class TreasuryConfiguration : IEntityTypeConfiguration<Domain.Treasury.Treasury>
{
    public void Configure(EntityTypeBuilder<Domain.Treasury.Treasury> builder)
    {
        builder.ToTable("treasuries", AppDbContext.AppSchema);

        builder.Property(treasury => treasury.Code).HasMaxLength(Domain.Treasury.Treasury.MaxCodeLength).IsRequired();
        builder.HasIndex(treasury => new { treasury.TenantId, treasury.Code }).IsUnique();

        builder.OwnsOne(treasury => treasury.Name, name =>
        {
            name.Property(text => text.En).HasColumnName("name_en").HasMaxLength(LocalizedText.MaxLength).IsRequired();
            name.Property(text => text.Ar).HasColumnName("name_ar").HasMaxLength(LocalizedText.MaxLength).IsRequired();
        });
        builder.Navigation(treasury => treasury.Name).IsRequired();

        builder.Property(treasury => treasury.BranchId).IsRequired();   // FK to app.branches arrives with the Organization package
        builder.Property(treasury => treasury.IsActive).IsRequired();
        builder.HasIndex(treasury => new { treasury.TenantId, treasury.BranchId });
    }
}

/// <summary>
/// <c>app.treasury_access_requests</c>: composite FK to the treasury (carries the tenant, 01 §4.2 item 3), the
/// requester as a platform user, the approval as owned columns, and the partial unique index of INV-TRS-002
/// (one live request per user, treasury and device).
/// </summary>
internal sealed class TreasuryAccessRequestConfiguration : IEntityTypeConfiguration<TreasuryAccessRequest>
{
    public void Configure(EntityTypeBuilder<TreasuryAccessRequest> builder)
    {
        builder.ToTable("treasury_access_requests", AppDbContext.AppSchema);

        builder.Property(request => request.Number).HasMaxLength(TreasuryAccessRequest.MaxNumberLength).IsRequired();
        builder.HasIndex(request => new { request.TenantId, request.Number }).IsUnique();

        builder.HasOne<Domain.Treasury.Treasury>().WithMany().HasForeignKey(request => new { request.TenantId, request.TreasuryId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlatformUser>().WithMany().HasForeignKey(request => request.RequestedByUserId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_treasury_access_requests_requested_by_user");   // the conventional name would exceed 63 characters

        builder.Property(request => request.BranchId).IsRequired();
        builder.Property(request => request.DeviceId).HasMaxLength(TreasuryAccessRequest.MaxDeviceIdLength).IsRequired();
        builder.Property(request => request.DeviceLabel).HasMaxLength(TreasuryAccessRequest.MaxDeviceLabelLength);
        builder.Property(request => request.Status).IsRequired();
        builder.Property(request => request.RequestedAtUtc).IsRequired();
        builder.Property(request => request.DecisionReason).HasMaxLength(TreasuryAccessRequest.MaxReasonLength);

        builder.OwnsOne(request => request.Approval, approval =>
        {
            approval.Property(value => value.ActorUserId).HasColumnName("approved_by_user_id");
            approval.Property(value => value.AtUtc).HasColumnName("approved_at_utc");
            approval.Property(value => value.Comment).HasColumnName("approval_comment").HasMaxLength(Approval.MaxCommentLength);
        });

        builder.HasIndex(request => new { request.TenantId, request.Status });
        builder.HasIndex(request => new { request.TenantId, request.RequestedByUserId });
        builder.HasIndex(request => new { request.TenantId, request.RequestedByUserId, request.TreasuryId, request.DeviceId })
            .IsUnique()
            .HasFilter("status IN ('Pending', 'Approved')")
            .HasDatabaseName("ux_treasury_access_requests_live");

        builder.Ignore(request => request.IsLive);
    }
}
