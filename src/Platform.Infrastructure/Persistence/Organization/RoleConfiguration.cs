using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Organization;
using Platform.SharedKernel.Localization;

namespace Platform.Infrastructure.Persistence.Organization;

/// <summary>
/// <c>app.roles</c> (01 §4.7, CP-37): permission keys as a <c>text[]</c>, the special rights as owned columns
/// (<c>special_*</c>) and the result rights as an owned collection in <c>app.role_result_rights</c> keyed
/// <c>(tenant_id, id)</c> with the composite FK <c>(tenant_id, role_id)</c> (pitfall P-31). The membership's composite
/// FK points here. The case-insensitive unique name (INV-ORG-006) is an expression index in the module SQL.
/// </summary>
internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles", AppDbContext.AppSchema);

        builder.OwnsOne(role => role.Name, name =>
        {
            name.Property(text => text.En).HasColumnName("name_en").HasMaxLength(LocalizedText.MaxLength).IsRequired();
            name.Property(text => text.Ar).HasColumnName("name_ar").HasMaxLength(LocalizedText.MaxLength).IsRequired();
        });
        builder.Navigation(role => role.Name).IsRequired();

        builder.Property(role => role.Description).HasMaxLength(Role.MaxDescriptionLength);
        builder.Property(role => role.PermissionKeys).IsRequired();
        builder.Property(role => role.IsActive).IsRequired();
        builder.HasIndex(role => new { role.TenantId, role.IsActive });

        builder.OwnsOne(role => role.SpecialRights, rights =>
        {
            rights.Property(value => value.DiscountCapPercent).HasColumnName("special_discount_cap_percent");
            rights.Property(value => value.CanPay).HasColumnName("special_can_pay").IsRequired();
            rights.Property(value => value.CanRefund).HasColumnName("special_can_refund").IsRequired();
            rights.Property(value => value.CanTransfer).HasColumnName("special_can_transfer").IsRequired();
            rights.Property(value => value.CanCancel).HasColumnName("special_can_cancel").IsRequired();
            rights.Property(value => value.CanModifyVerifiedResult).HasColumnName("special_can_modify_verified_result").IsRequired();
            rights.Property(value => value.CanPreviewWithoutVerify).HasColumnName("special_can_preview_without_verify").IsRequired();
            rights.Property(value => value.CanPrintInvoice).HasColumnName("special_can_print_invoice").IsRequired();
            rights.Property(value => value.CanSeeVip).HasColumnName("special_can_see_vip").IsRequired();
            // Three later rights (change request 2026-10-13). The convention adds no CHECK to owned enums: the migration declares
            // ck_roles_special_demographics_edit_up_to_domain. No EF default value here: with one, the CLR default of the enum (Ordered)
            // would be EF's "unset" sentinel and the column default (Complete) would silently replace a deliberate Ordered. The
            // migration alone carries the defaults that fill the rows existing at upgrade time.
            rights.Property(value => value.DemographicsEditUpTo).HasColumnName("special_demographics_edit_up_to").HasConversion<string>().HasMaxLength(16).IsRequired();
            rights.Property(value => value.CanViewBranchCollections).HasColumnName("special_can_view_branch_collections").IsRequired();
            rights.Property(value => value.ReportDaysLimit).HasColumnName("special_report_days_limit");
        });
        builder.Navigation(role => role.SpecialRights).IsRequired();

        builder.OwnsMany(role => role.ResultRights, rights =>
        {
            rights.ToTable("role_result_rights", AppDbContext.AppSchema);
            rights.WithOwner().HasForeignKey("TenantId", "RoleId");
            rights.Property(right => right.Id).ValueGeneratedNever();
            rights.HasKey("TenantId", nameof(RoleResultRight.Id));
            rights.Property(right => right.ClinicalGroupId).IsRequired();   // app.clinical_groups (catalogue module): referenced by id, no FK across modules
            rights.Property(right => right.BranchId);
            rights.Property(right => right.CanSee).IsRequired();
            rights.Property(right => right.CanReview).IsRequired();
            rights.Property(right => right.CanVerify).IsRequired();
            rights.Property(right => right.CanEdit).IsRequired();
            rights.Property(right => right.CanPrint).IsRequired();

            // INV-ORG-008: one row per (group, branch); "all branches" (NULL) counts as one value.
            rights.HasIndex("TenantId", "RoleId", nameof(RoleResultRight.ClinicalGroupId), nameof(RoleResultRight.BranchId))
                .IsUnique()
                .AreNullsDistinct(false)
                .HasDatabaseName("ux_role_result_rights_scope");
            rights.HasIndex("TenantId", nameof(RoleResultRight.ClinicalGroupId)).HasDatabaseName("ix_role_result_rights_group");
        });
        builder.Navigation(role => role.ResultRights).HasField("_resultRights").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary><c>app.branches</c> (CP-36): code unique per tenant (INV-ORG-001), RLS declared in the creating migration.</summary>
internal sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("branches", AppDbContext.AppSchema);

        builder.Property(branch => branch.Code).HasMaxLength(Branch.MaxCodeLength).IsRequired();
        builder.HasIndex(branch => new { branch.TenantId, branch.Code }).IsUnique();

        builder.OwnsOne(branch => branch.Name, name =>
        {
            name.Property(text => text.En).HasColumnName("name_en").HasMaxLength(LocalizedText.MaxLength).IsRequired();
            name.Property(text => text.Ar).HasColumnName("name_ar").HasMaxLength(LocalizedText.MaxLength).IsRequired();
        });
        builder.Navigation(branch => branch.Name).IsRequired();

        builder.Property(branch => branch.CountryId);       // saas.countries (platform module): referenced by id
        builder.Property(branch => branch.GovernorateId);   // saas.governorates (platform module): referenced by id
        builder.Property(branch => branch.Phone).HasMaxLength(Branch.MaxPhoneLength);
        builder.Property(branch => branch.Email).HasMaxLength(Branch.MaxEmailLength);
        builder.Property(branch => branch.Currency).HasMaxLength(Branch.CurrencyLength).IsRequired();
        builder.Property(branch => branch.IsActive).IsRequired();
        builder.HasIndex(branch => new { branch.TenantId, branch.IsActive });
    }
}
