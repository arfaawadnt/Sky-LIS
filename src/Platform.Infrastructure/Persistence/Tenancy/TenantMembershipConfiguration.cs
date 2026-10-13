using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Identity;
using Platform.Domain.Organization;
using Platform.Domain.Tenancy;
using Platform.SharedKernel.Localization;

namespace Platform.Infrastructure.Persistence.Tenancy;

/// <summary>
/// <c>app.tenant_memberships</c> (01 §4.7, D-06). The key <c>(tenant_id, id)</c>, the tenant query filter and the
/// xmin token come from the conventions; FORCE ROW LEVEL SECURITY and the <c>tenant_isolation</c> policy are
/// declared in the migration that creates the table (pitfall P-04).
/// </summary>
internal sealed class TenantMembershipConfiguration : IEntityTypeConfiguration<TenantMembership>
{
    public void Configure(EntityTypeBuilder<TenantMembership> builder)
    {
        builder.ToTable("tenant_memberships", AppDbContext.AppSchema);

        builder.Property(membership => membership.TenantId).IsRequired();
        builder.Property(membership => membership.PlatformUserId).IsRequired();
        builder.HasOne<PlatformUser>().WithMany().HasForeignKey(membership => membership.PlatformUserId).OnDelete(DeleteBehavior.Restrict);

        // Business uniqueness always includes the tenant (01 §4.2 item 4).
        builder.HasIndex(membership => new { membership.TenantId, membership.PlatformUserId }).IsUnique();
        builder.HasIndex(membership => new { membership.TenantId, membership.EmployeeCode }).IsUnique();

        builder.Property(membership => membership.EmployeeCode).HasMaxLength(TenantMembership.MaxEmployeeCodeLength).IsRequired();

        builder.OwnsOne(membership => membership.FullName, name =>
        {
            name.Property(text => text.En).HasColumnName("full_name_en").HasMaxLength(LocalizedText.MaxLength).IsRequired();
            name.Property(text => text.Ar).HasColumnName("full_name_ar").HasMaxLength(LocalizedText.MaxLength).IsRequired();
        });
        builder.Navigation(membership => membership.FullName).IsRequired();

        builder.Property(membership => membership.IsActive).IsRequired();
        builder.HasOne<Role>().WithMany().HasForeignKey(membership => new { membership.TenantId, membership.RoleId }).OnDelete(DeleteBehavior.Restrict);
        builder.Property(membership => membership.BranchIds).IsRequired();      // uuid[]; empty = unrestricted
        builder.Property(membership => membership.DepartmentIds).IsRequired();
        builder.Property(membership => membership.PreferredLanguage).HasMaxLength(5).IsRequired();
        // results grid density (results page, 2026-10-14); the domain CHECK ck_tenant_memberships_preferred_results_density_domain is declared in the migration
        builder.Property(membership => membership.PreferredResultsDensity).HasMaxLength(16);

        // Staff profile and BR-17 overrides (CP-38, intake LIS-INT-ORG-001 §5).
        builder.Property(membership => membership.Designation).HasMaxLength(TenantMembership.MaxDesignationLength);
        builder.Property(membership => membership.Shifts).IsRequired();                  // text[]
        builder.Property(membership => membership.SignatureFileId);
        builder.Property(membership => membership.IsPrimaryAdministrator).IsRequired();
        builder.HasIndex(membership => membership.TenantId)
            .IsUnique()
            .HasFilter("is_primary_administrator")
            .HasDatabaseName("ux_tenant_memberships_primary_administrator");                  // one main administrator per tenant
        builder.HasIndex(membership => new { membership.TenantId, membership.RoleId });
        builder.Property(membership => membership.AddedPermissionKeys).IsRequired();     // text[]
        builder.Property(membership => membership.RemovedPermissionKeys).IsRequired();   // text[]
        builder.OwnsOne(membership => membership.SpecialRightsOverride, rights =>
        {
            rights.Property(value => value.DiscountCapPercent).HasColumnName("override_discount_cap_percent");
            rights.Property(value => value.CanPay).HasColumnName("override_can_pay");
            rights.Property(value => value.CanRefund).HasColumnName("override_can_refund");
            rights.Property(value => value.CanTransfer).HasColumnName("override_can_transfer");
            rights.Property(value => value.CanCancel).HasColumnName("override_can_cancel");
            rights.Property(value => value.CanModifyVerifiedResult).HasColumnName("override_can_modify_verified_result");
            rights.Property(value => value.CanPreviewWithoutVerify).HasColumnName("override_can_preview_without_verify");
            rights.Property(value => value.CanPrintInvoice).HasColumnName("override_can_print_invoice");
            rights.Property(value => value.CanSeeVip).HasColumnName("override_can_see_vip");
            rights.Property(value => value.DemographicsEditUpTo).HasColumnName("override_demographics_edit_up_to").HasConversion<string>().HasMaxLength(16);
            rights.Property(value => value.CanViewBranchCollections).HasColumnName("override_can_view_branch_collections");
            rights.Property(value => value.ReportDaysLimit).HasColumnName("override_report_days_limit");
        });
        builder.Ignore(membership => membership.IsModified);
    }
}
