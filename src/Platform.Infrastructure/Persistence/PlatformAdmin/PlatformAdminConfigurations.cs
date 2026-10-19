using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Localization;

namespace Platform.Infrastructure.Persistence.PlatformAdmin;

// Platform-level tables of the Super Admin portal (schema saas, no tenant_id, no RLS, single-column keys).
// lis_app has no default privileges in saas: PlatformAdmin.migration.sql grants USAGE and the table rights, and adds
// the indexes EF cannot express on owned columns (governorate name per country) and the cross-tenant count function.

internal static class SaasColumns
{
    public static void Bilingual<TOwner>(EntityTypeBuilder<TOwner> builder, System.Linq.Expressions.Expression<Func<TOwner, LocalizedText?>> navigation, string prefix, int maxLength)
        where TOwner : class
    {
        builder.OwnsOne(navigation, text =>
        {
            text.Property(value => value.En).HasColumnName(prefix + "_en").HasMaxLength(maxLength).IsRequired();
            text.Property(value => value.Ar).HasColumnName(prefix + "_ar").HasMaxLength(maxLength).IsRequired();
        });
        builder.Navigation(navigation!).IsRequired();
    }
}

/// <summary><c>saas.tenants</c>: the clients (SA-04); the id is the tenant id; the portal slug is unique (INV-PLT-001).</summary>
internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants", AppDbContext.SaasSchema);
        SaasColumns.Bilingual(builder, tenant => tenant.Name, "name", Tenant.MaxNameLength);

        builder.Property(tenant => tenant.Address).HasMaxLength(Tenant.MaxAddressLength).IsRequired();
        builder.Property(tenant => tenant.Mobile).HasMaxLength(Tenant.MaxMobileLength).IsRequired();
        builder.Property(tenant => tenant.LicenceNo).HasMaxLength(Tenant.MaxLicenceLength);
        builder.Property(tenant => tenant.ManagerName).HasMaxLength(Tenant.MaxManagerNameLength).IsRequired();
        builder.Property(tenant => tenant.ManagerMobile).HasMaxLength(Tenant.MaxMobileLength).IsRequired();
        builder.Property(tenant => tenant.Email).HasMaxLength(Tenant.MaxEmailLength);
        builder.Property(tenant => tenant.PortalSlug).HasMaxLength(Tenant.MaxSlugLength).IsRequired();
        builder.Property(tenant => tenant.DeactivationReason).HasMaxLength(Tenant.MaxReasonLength);
        builder.Property(tenant => tenant.AdministratorUsername).HasMaxLength(64);
        builder.Property(tenant => tenant.PreferredLanguage).HasMaxLength(5).IsRequired();
        builder.Property(tenant => tenant.Source).IsRequired();
        builder.Property(tenant => tenant.Status).IsRequired();

        builder.HasIndex(tenant => tenant.PortalSlug).IsUnique();
        builder.HasIndex(tenant => tenant.Status);
        builder.HasIndex(tenant => tenant.PlanId);
        builder.HasIndex(tenant => tenant.CountryId);
        builder.HasIndex(tenant => tenant.GovernorateId);

        builder.HasOne<Country>().WithMany().HasForeignKey(tenant => tenant.CountryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Governorate>().WithMany().HasForeignKey(tenant => tenant.GovernorateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Plan>().WithMany().HasForeignKey(tenant => tenant.PlanId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>saas.plans</c>; the code is unique (INV-PLT-020).</summary>
internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("plans", AppDbContext.SaasSchema);
        builder.Property(plan => plan.Code).HasMaxLength(Plan.MaxCodeLength).IsRequired();
        builder.HasIndex(plan => plan.Code).IsUnique();
        SaasColumns.Bilingual(builder, plan => plan.Name, "name", Plan.MaxNameLength);
        builder.Property(plan => plan.MonthlyFee).HasPrecision(12, 2);
        builder.Property(plan => plan.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
    }
}

/// <summary><c>saas.wallets</c> (one per client) with its movements and top-up requests; the history is never loaded with the root.</summary>
internal sealed class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        builder.ToTable("wallets", AppDbContext.SaasSchema);
        builder.Property(wallet => wallet.Balance).HasPrecision(14, 2);
        builder.Property(wallet => wallet.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.HasIndex(wallet => wallet.TenantId).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(wallet => wallet.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(wallet => wallet.IsAtOrBelowZero);

        builder.HasMany(wallet => wallet.Transactions).WithOne().HasForeignKey(transaction => transaction.WalletId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(wallet => wallet.Transactions).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_transactions");

        builder.HasMany(wallet => wallet.TopUpRequests).WithOne().HasForeignKey(request => request.WalletId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(wallet => wallet.TopUpRequests).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_topUpRequests");
    }
}

internal sealed class WalletTransactionConfiguration : IEntityTypeConfiguration<WalletTransaction>
{
    public void Configure(EntityTypeBuilder<WalletTransaction> builder)
    {
        builder.ToTable("wallet_transactions", AppDbContext.SaasSchema);
        builder.Property(transaction => transaction.Id).ValueGeneratedNever();   // minted in the domain: a movement reached through Wallet.Transactions is inserted, never updated
        builder.Property(transaction => transaction.Kind).IsRequired();
        builder.Property(transaction => transaction.Amount).HasPrecision(14, 2);
        builder.Property(transaction => transaction.BalanceAfter).HasPrecision(14, 2);
        builder.Property(transaction => transaction.Reference).HasMaxLength(Wallet.MaxReferenceLength).IsRequired();
        builder.Property(transaction => transaction.Note).HasMaxLength(Wallet.MaxNoteLength);
        builder.HasIndex(transaction => new { transaction.WalletId, transaction.AtUtc });
    }
}

internal sealed class WalletTopUpRequestConfiguration : IEntityTypeConfiguration<WalletTopUpRequest>
{
    public void Configure(EntityTypeBuilder<WalletTopUpRequest> builder)
    {
        builder.ToTable("wallet_top_up_requests", AppDbContext.SaasSchema);
        builder.Property(request => request.Id).ValueGeneratedNever();   // minted in the domain: a new request reached through Wallet.TopUpRequests is inserted
        builder.Property(request => request.Amount).HasPrecision(14, 2);
        builder.Property(request => request.Note).HasMaxLength(Wallet.MaxNoteLength);
        builder.Property(request => request.Status).IsRequired();
        builder.HasIndex(request => new { request.WalletId, request.Status });
    }
}

/// <summary><c>saas.countries</c>; the ISO code is unique (INV-PLT-011).</summary>
internal sealed class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        builder.ToTable("countries", AppDbContext.SaasSchema);
        SaasColumns.Bilingual(builder, country => country.Name, "name", Country.MaxNameLength);
        builder.Property(country => country.IsoCode).HasMaxLength(2).IsFixedLength().IsRequired();
        builder.HasIndex(country => country.IsoCode).IsUnique();
        builder.Property(country => country.DialCode).HasMaxLength(5).IsRequired();
        builder.Property(country => country.Currency).HasMaxLength(Country.MaxCurrencyNameLength).IsRequired();
        builder.Property(country => country.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(country => country.Language).HasMaxLength(2).IsRequired();
        builder.Property(country => country.TimeZone).HasMaxLength(Country.MaxTimeZoneLength).IsRequired();
        builder.Property(country => country.Flag).HasMaxLength(Country.MaxFlagLength);
        builder.Property(country => country.Remarks).HasMaxLength(Country.MaxRemarksLength);
    }
}

/// <summary><c>saas.governorates</c>; the English name is unique per country (index <c>ux_governorates_country_name</c> in the module SQL).</summary>
internal sealed class GovernorateConfiguration : IEntityTypeConfiguration<Governorate>
{
    public void Configure(EntityTypeBuilder<Governorate> builder)
    {
        builder.ToTable("governorates", AppDbContext.SaasSchema);
        SaasColumns.Bilingual(builder, governorate => governorate.Name, "name", Governorate.MaxNameLength);
        builder.Property(governorate => governorate.District).HasMaxLength(Governorate.MaxDistrictLength);
        builder.Property(governorate => governorate.Remarks).HasMaxLength(Governorate.MaxRemarksLength);
        builder.HasOne<Country>().WithMany().HasForeignKey(governorate => governorate.CountryId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>saas.notification_templates</c>; the code is unique (INV-PLT-040).</summary>
internal sealed class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("notification_templates", AppDbContext.SaasSchema);
        builder.Property(template => template.Code).HasMaxLength(NotificationTemplate.MaxCodeLength).IsRequired();
        builder.HasIndex(template => template.Code).IsUnique();
        builder.Property(template => template.Channel).IsRequired();
        builder.Property(template => template.SubjectEn).HasMaxLength(NotificationTemplate.MaxSubjectLength);
        builder.Property(template => template.SubjectAr).HasMaxLength(NotificationTemplate.MaxSubjectLength);
        builder.Property(template => template.TextEn).HasMaxLength(NotificationTemplate.MaxTextLength).IsRequired();
        builder.Property(template => template.TextAr).HasMaxLength(NotificationTemplate.MaxTextLength).IsRequired();
    }
}

internal sealed class AnalyserModelConfiguration : IEntityTypeConfiguration<AnalyserModel>
{
    public void Configure(EntityTypeBuilder<AnalyserModel> builder)
    {
        builder.ToTable("analyser_models", AppDbContext.SaasSchema);
        builder.Property(model => model.Vendor).HasMaxLength(AnalyserModel.MaxTextLength).IsRequired();
        builder.Property(model => model.Model).HasMaxLength(AnalyserModel.MaxTextLength).IsRequired();
        builder.Property(model => model.Discipline).HasMaxLength(AnalyserModel.MaxTextLength).IsRequired();
        builder.Property(model => model.Protocol).IsRequired();
        builder.Property(model => model.PhysicalLink).IsRequired();
        builder.Property(model => model.Modes).IsRequired();   // text[]
        builder.Property(model => model.DriverVersion).HasMaxLength(40);
        builder.HasIndex(model => new { model.Vendor, model.Model }).IsUnique();
    }
}

internal sealed class PcrKitTemplateConfiguration : IEntityTypeConfiguration<PcrKitTemplate>
{
    public void Configure(EntityTypeBuilder<PcrKitTemplate> builder)
    {
        builder.ToTable("pcr_kit_templates", AppDbContext.SaasSchema);
        builder.Property(kit => kit.Name).HasMaxLength(PcrKitTemplate.MaxNameLength).IsRequired();
        builder.HasIndex(kit => kit.Name).IsUnique();
        builder.Property(kit => kit.LayoutJson).HasColumnType("jsonb").IsRequired();
        builder.Property(kit => kit.ThresholdsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(kit => kit.VerdictRulesJson).HasColumnType("jsonb").IsRequired();
        builder.HasOne<AnalyserModel>().WithMany().HasForeignKey(kit => kit.AnalyserModelId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CodeSystemConfiguration : IEntityTypeConfiguration<CodeSystem>
{
    public void Configure(EntityTypeBuilder<CodeSystem> builder)
    {
        builder.ToTable("code_systems", AppDbContext.SaasSchema);
        builder.Property(codeSystem => codeSystem.Name).IsRequired();
        builder.Property(codeSystem => codeSystem.Version).HasMaxLength(CodeSystem.MaxVersionLength).IsRequired();
        builder.Property(codeSystem => codeSystem.Use).HasMaxLength(CodeSystem.MaxUseLength);
        builder.HasIndex(codeSystem => new { codeSystem.Name, codeSystem.Version }).IsUnique();
    }
}

/// <summary><c>saas.code_system_items</c>: key <c>(code_system_id, code)</c>; bulk rows, upserted by import.</summary>
internal sealed class CodeSystemItemConfiguration : IEntityTypeConfiguration<CodeSystemItem>
{
    public void Configure(EntityTypeBuilder<CodeSystemItem> builder)
    {
        builder.ToTable("code_system_items", AppDbContext.SaasSchema);
        builder.HasKey(item => new { item.CodeSystemId, item.Code });
        builder.Property(item => item.Code).HasMaxLength(CodeSystemItem.MaxCodeLength).IsRequired();
        builder.Property(item => item.Display).HasMaxLength(CodeSystemItem.MaxDisplayLength).IsRequired();
        builder.HasOne<CodeSystem>().WithMany().HasForeignKey(item => item.CodeSystemId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RuleTemplateConfiguration : IEntityTypeConfiguration<RuleTemplate>
{
    public void Configure(EntityTypeBuilder<RuleTemplate> builder)
    {
        builder.ToTable("rule_templates", AppDbContext.SaasSchema);
        builder.Property(rule => rule.Name).HasMaxLength(RuleTemplate.MaxNameLength).IsRequired();
        builder.HasIndex(rule => rule.Name).IsUnique();
        builder.Property(rule => rule.ClinicalGroupName).HasMaxLength(RuleTemplate.MaxGroupLength);
        builder.Property(rule => rule.Formula).HasMaxLength(RuleTemplate.MaxFormulaLength).IsRequired();
        builder.Property(rule => rule.Description).HasMaxLength(RuleTemplate.MaxDescriptionLength);
    }
}

/// <summary><c>saas.subscription_requests</c>: transient wizard state (purged after expiry by the retention job, intake §12); no FKs.</summary>
internal sealed class SubscriptionRequestConfiguration : IEntityTypeConfiguration<SubscriptionRequest>
{
    public void Configure(EntityTypeBuilder<SubscriptionRequest> builder)
    {
        builder.ToTable("subscription_requests", AppDbContext.SaasSchema);
        builder.Property(request => request.NameEn).HasMaxLength(Tenant.MaxNameLength).IsRequired();
        builder.Property(request => request.NameAr).HasMaxLength(Tenant.MaxNameLength).IsRequired();
        builder.Property(request => request.Address).HasMaxLength(Tenant.MaxAddressLength).IsRequired();
        builder.Property(request => request.Mobile).HasMaxLength(Tenant.MaxMobileLength).IsRequired();
        builder.Property(request => request.LicenceNo).HasMaxLength(Tenant.MaxLicenceLength);
        builder.Property(request => request.ManagerName).HasMaxLength(Tenant.MaxManagerNameLength).IsRequired();
        builder.Property(request => request.ManagerMobile).HasMaxLength(Tenant.MaxMobileLength).IsRequired();
        builder.Property(request => request.Email).HasMaxLength(Tenant.MaxEmailLength);
        builder.Property(request => request.Language).HasMaxLength(5).IsRequired();
        builder.Property(request => request.Status).IsRequired();
        builder.HasIndex(request => request.ExpiresAtUtc);
    }
}

// Location hierarchy under a governorate (change request 2026-10-19, A2): saas.cities, saas.districts, saas.areas.
// The English name is unique per parent through the case-insensitive indexes of PlatformAdmin.migration.sql
// (ux_cities_governorate_name, ux_districts_city_name, ux_areas_district_name); the parent FK is Restrict.

internal sealed class CityConfiguration : IEntityTypeConfiguration<City>
{
    public void Configure(EntityTypeBuilder<City> builder)
    {
        builder.ToTable("cities", AppDbContext.SaasSchema);
        SaasColumns.Bilingual(builder, city => city.Name, "name", City.MaxNameLength);
        builder.HasIndex(city => city.GovernorateId);
        builder.HasOne<Governorate>().WithMany().HasForeignKey(city => city.GovernorateId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DistrictConfiguration : IEntityTypeConfiguration<District>
{
    public void Configure(EntityTypeBuilder<District> builder)
    {
        builder.ToTable("districts", AppDbContext.SaasSchema);
        SaasColumns.Bilingual(builder, district => district.Name, "name", District.MaxNameLength);
        builder.HasIndex(district => district.CityId);
        builder.HasOne<City>().WithMany().HasForeignKey(district => district.CityId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AreaConfiguration : IEntityTypeConfiguration<Area>
{
    public void Configure(EntityTypeBuilder<Area> builder)
    {
        builder.ToTable("areas", AppDbContext.SaasSchema);
        SaasColumns.Bilingual(builder, area => area.Name, "name", Area.MaxNameLength);
        builder.HasIndex(area => area.DistrictId);
        builder.HasOne<District>().WithMany().HasForeignKey(area => area.DistrictId).OnDelete(DeleteBehavior.Restrict);
    }
}
