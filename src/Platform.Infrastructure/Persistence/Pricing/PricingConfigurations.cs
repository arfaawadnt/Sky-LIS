using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Pricing;
using Platform.SharedKernel.Localization;

namespace Platform.Infrastructure.Persistence.Pricing;

// Tables of the Pricing module (intake LIS-INT-PRC-001 §5). The conventions give every tenant table its (tenant_id, id)
// key, the tenant query filter and xmin concurrency on the roots; RLS is declared in the creating migration. Children
// carry a composite FK (tenant_id, <root>_id) with cascade (they live and die with their root, which is never deleted);
// references between aggregates are Restrict. Constraint names are explicit where the conventional one would be long.

/// <summary><c>app.payers</c>: search key unique per tenant (INV-PRC-001), web username unique when set (INV-PRC-002), one default payer per tenant (INV-PRC-003).</summary>
internal sealed class PayerConfiguration : IEntityTypeConfiguration<Payer>
{
    public void Configure(EntityTypeBuilder<Payer> builder)
    {
        builder.ToTable("payers", AppDbContext.AppSchema);

        builder.OwnsOne(payer => payer.Name, name =>
        {
            name.Property(text => text.En).HasColumnName("name_en").HasMaxLength(LocalizedText.MaxLength).IsRequired();
            name.Property(text => text.Ar).HasColumnName("name_ar").HasMaxLength(LocalizedText.MaxLength).IsRequired();
        });
        builder.Navigation(payer => payer.Name).IsRequired();

        builder.Property(payer => payer.SearchKey).HasMaxLength(Payer.MaxSearchKeyLength).IsRequired();
        builder.Property(payer => payer.Type).IsRequired();
        builder.Property(payer => payer.WebPortalEnabled).IsRequired();
        builder.Property(payer => payer.WebUsername).HasMaxLength(Payer.MaxWebUsernameLength);
        // office contacts (CR 2026-10-19 C19): nullable columns added by the PrototypeGaps migration; existing rows keep NULL
        builder.Property(payer => payer.Phone1).HasMaxLength(Payer.MaxContactNumberLength);
        builder.Property(payer => payer.Phone2).HasMaxLength(Payer.MaxContactNumberLength);
        builder.Property(payer => payer.WhatsApp).HasColumnName("whatsapp").HasMaxLength(Payer.MaxContactNumberLength);
        builder.Property(payer => payer.Email).HasMaxLength(Payer.MaxEmailLength);
        builder.Ignore(payer => payer.Contacts);
        builder.Property(payer => payer.IsActive).IsRequired();
        builder.Property(payer => payer.IsDefault).IsRequired();

        builder.HasIndex(payer => new { payer.TenantId, payer.SearchKey }).IsUnique().HasDatabaseName("ux_payers_search_key");
        builder.HasIndex(payer => new { payer.TenantId, payer.WebUsername }).IsUnique().HasFilter("web_username IS NOT NULL").HasDatabaseName("ux_payers_web_username");
        builder.HasIndex(payer => payer.TenantId).IsUnique().HasFilter("is_default").HasDatabaseName("ux_payers_default");
        builder.HasIndex(payer => new { payer.TenantId, payer.IsActive });

        builder.HasMany(payer => payer.Tiers).WithOne()
            .HasForeignKey(tier => new { tier.TenantId, tier.PayerId })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_contract_tiers_payer");
        builder.Navigation(payer => payer.Tiers).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary><c>app.contract_tiers</c> (name kept for other modules): the tiers of a payer.</summary>
internal sealed class ContractTierConfiguration : IEntityTypeConfiguration<ContractTier>
{
    public void Configure(EntityTypeBuilder<ContractTier> builder)
    {
        builder.ToTable("contract_tiers", AppDbContext.AppSchema);

        builder.OwnsOne(tier => tier.Name, name =>
        {
            name.Property(text => text.En).HasColumnName("name_en").HasMaxLength(LocalizedText.MaxLength).IsRequired();
            name.Property(text => text.Ar).HasColumnName("name_ar").HasMaxLength(LocalizedText.MaxLength).IsRequired();
        });
        builder.Navigation(tier => tier.Name).IsRequired();

        builder.Property(tier => tier.PayerId).IsRequired();
        builder.Property(tier => tier.IsActive).IsRequired();
        builder.Property(tier => tier.IsDefault).IsRequired();
        builder.HasIndex(tier => new { tier.TenantId, tier.PayerId });
    }
}

/// <summary><c>app.base_price_lists</c>: code unique per tenant (INV-PRC-005).</summary>
internal sealed class BasePriceListConfiguration : IEntityTypeConfiguration<BasePriceList>
{
    public void Configure(EntityTypeBuilder<BasePriceList> builder)
    {
        builder.ToTable("base_price_lists", AppDbContext.AppSchema);

        builder.Property(list => list.Code).HasMaxLength(BasePriceList.MaxCodeLength).IsRequired();
        builder.HasIndex(list => new { list.TenantId, list.Code }).IsUnique().HasDatabaseName("ux_base_price_lists_code");

        builder.OwnsOne(list => list.Name, name =>
        {
            name.Property(text => text.En).HasColumnName("name_en").HasMaxLength(LocalizedText.MaxLength).IsRequired();
            name.Property(text => text.Ar).HasColumnName("name_ar").HasMaxLength(LocalizedText.MaxLength).IsRequired();
        });
        builder.Navigation(list => list.Name).IsRequired();

        builder.Property(list => list.Year).IsRequired();
        builder.Property(list => list.IsActive).IsRequired();
        builder.HasIndex(list => new { list.TenantId, list.IsActive, list.Year });

        builder.HasMany(list => list.Lines).WithOne()
            .HasForeignKey(line => new { line.TenantId, line.PriceListId })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_base_price_lines_list");
        builder.Navigation(list => list.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary><c>app.base_price_lines</c>: one line per service and list (INV-PRC-006); the service is the catalogue's (no FK this wave).</summary>
internal sealed class PriceLineConfiguration : IEntityTypeConfiguration<PriceLine>
{
    public void Configure(EntityTypeBuilder<PriceLine> builder)
    {
        builder.ToTable("base_price_lines", AppDbContext.AppSchema);

        builder.Property(line => line.PriceListId).IsRequired();
        builder.Property(line => line.ServiceId).IsRequired();
        builder.Property(line => line.Price).HasPrecision(12, PriceMath.AmountScale).IsRequired();
        builder.Property(line => line.IsActive).IsRequired();

        builder.HasIndex(line => new { line.TenantId, line.PriceListId, line.ServiceId }).IsUnique().HasDatabaseName("ux_base_price_lines_service");
        builder.HasIndex(line => new { line.TenantId, line.ServiceId });
    }
}

/// <summary><c>app.contract_pricings</c>: one per tier (INV-PRC-007), referencing the payer and the tier.</summary>
internal sealed class ContractPricingConfiguration : IEntityTypeConfiguration<ContractPricing>
{
    public void Configure(EntityTypeBuilder<ContractPricing> builder)
    {
        builder.ToTable("contract_pricings", AppDbContext.AppSchema);

        builder.Property(pricing => pricing.EffectiveFrom).IsRequired();
        builder.HasIndex(pricing => new { pricing.TenantId, pricing.ContractTierId }).IsUnique().HasDatabaseName("ux_contract_pricings_tier");
        builder.HasIndex(pricing => new { pricing.TenantId, pricing.PayerId });

        builder.HasOne<Payer>().WithMany()
            .HasForeignKey(pricing => new { pricing.TenantId, pricing.PayerId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_contract_pricings_payer");
        builder.HasOne<ContractTier>().WithMany()
            .HasForeignKey(pricing => new { pricing.TenantId, pricing.ContractTierId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_contract_pricings_tier");

        builder.HasMany(pricing => pricing.Sources).WithOne()
            .HasForeignKey(source => new { source.TenantId, source.ContractPricingId })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_contract_pricing_sources_pricing");
        builder.Navigation(pricing => pricing.Sources).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(pricing => pricing.Prices).WithOne()
            .HasForeignKey(price => new { price.TenantId, price.ContractPricingId })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_contract_prices_pricing");
        builder.Navigation(pricing => pricing.Prices).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary><c>app.contract_pricing_sources</c>: the 1–3 terms of a formula; each references a base list (Restrict).</summary>
internal sealed class PricingSourceConfiguration : IEntityTypeConfiguration<PricingSource>
{
    public void Configure(EntityTypeBuilder<PricingSource> builder)
    {
        builder.ToTable("contract_pricing_sources", AppDbContext.AppSchema);

        builder.Property(source => source.ContractPricingId).IsRequired();
        builder.Property(source => source.Priority).IsRequired();
        builder.Property(source => source.PatientFactor).HasPrecision(9, PriceMath.FactorScale).IsRequired();
        builder.Property(source => source.PayerFactor).HasPrecision(9, PriceMath.FactorScale).IsRequired();
        builder.ToTable(table => table.HasCheckConstraint("ck_contract_pricing_sources_priority", "priority BETWEEN 1 AND 3"));

        builder.HasOne<BasePriceList>().WithMany()
            .HasForeignKey(source => new { source.TenantId, source.BasePriceListId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_contract_pricing_sources_list");
        builder.HasIndex(source => new { source.TenantId, source.ContractPricingId }).HasDatabaseName("ix_contract_pricing_sources_pricing");
        builder.HasIndex(source => new { source.TenantId, source.BasePriceListId }).HasDatabaseName("ix_contract_pricing_sources_list");
    }
}

/// <summary><c>app.contract_prices</c>: one row per service and tier (INV-PRC-009), read by the quote.</summary>
internal sealed class ContractPriceConfiguration : IEntityTypeConfiguration<ContractPrice>
{
    public void Configure(EntityTypeBuilder<ContractPrice> builder)
    {
        builder.ToTable("contract_prices", AppDbContext.AppSchema);

        builder.Property(price => price.ContractPricingId).IsRequired();
        builder.Property(price => price.ServiceId).IsRequired();
        builder.Property(price => price.ListPrice).HasPrecision(12, PriceMath.AmountScale).IsRequired();
        builder.Property(price => price.PatientShare).HasPrecision(12, PriceMath.AmountScale).IsRequired();
        builder.Property(price => price.PayerShare).HasPrecision(12, PriceMath.AmountScale).IsRequired();
        builder.Property(price => price.IsExcluded).IsRequired();
        builder.Property(price => price.AlertCondition).HasMaxLength(ContractPricing.MaxAlertLength);

        builder.HasIndex(price => new { price.TenantId, price.ContractPricingId, price.ServiceId }).IsUnique().HasDatabaseName("ux_contract_prices_service");
    }
}
