using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Billing;
using Platform.Domain.Identity;

namespace Platform.Infrastructure.Persistence.Billing;

/// <summary>
/// <c>app.visit_accounts</c>: one row per visit that has money (id = the visit id; the link to <c>app.visits</c> is added by
/// <c>Billing.migration.sql</c> because Registration is built in the same wave). The running totals change with every
/// movement, so the <c>xmin</c> token of this row serialises concurrent cashiers (INV-BIL-001/002).
/// </summary>
internal sealed class VisitAccountConfiguration : IEntityTypeConfiguration<VisitAccount>
{
    public void Configure(EntityTypeBuilder<VisitAccount> builder)
    {
        builder.ToTable("visit_accounts", AppDbContext.AppSchema, table =>
            table.HasCheckConstraint("ck_visit_accounts_totals", "paid_total >= 0 AND refunded_total >= 0"));

        builder.Property(account => account.PaidTotal).HasPrecision(BillingAmounts.Precision, BillingAmounts.Scale).IsRequired();
        builder.Property(account => account.RefundedTotal).HasPrecision(BillingAmounts.Precision, BillingAmounts.Scale).IsRequired();
        builder.Ignore(account => account.VisitId);

        builder.HasMany(account => account.Payments).WithOne()
            .HasForeignKey(payment => new { payment.TenantId, payment.VisitId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(account => account.Payments).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        builder.HasMany(account => account.Refunds).WithOne()
            .HasForeignKey(refund => new { refund.TenantId, refund.VisitId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(account => account.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

/// <summary>
/// <c>app.payments</c> — the MODULES.md §5 contract (<c>id, tenant_id, visit_id, receipt_number, amount, method, status,
/// paid_at_utc, cashier_user_id</c>) plus reference, branch, batch and the void columns. Receipt numbers are unique per
/// tenant (INV-BIL-003); CHECK constraints repeat the aggregate's rules; a trigger keeps the row immutable except for the
/// void (<c>Billing.migration.sql</c>). <c>treasury_id</c> and <c>device_id</c> are history of the withdrawn treasury
/// (LIS-CR-004, 2026-10-20): nullable, no foreign key, never written by a new payment.
/// </summary>
internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    /// <summary>The length of the historical <c>device_id</c> column (the withdrawn BR-27 device identity).</summary>
    internal const int LegacyDeviceIdLength = 128;

    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", AppDbContext.AppSchema, table =>
        {
            table.HasCheckConstraint("ck_payments_amount_positive", "amount > 0");
            // without a definition every kind but cash needs a reference; with one (method_code) the definition decided (change request 2026-10-19)
            table.HasCheckConstraint("ck_payments_reference_for_non_cash", "method = 'Cash' OR method_code IS NOT NULL OR reference IS NOT NULL");
            table.HasCheckConstraint(
                "ck_payments_void_consistency",
                "(status = 'Voided') = (void_reason IS NOT NULL AND voided_at_utc IS NOT NULL AND voided_by_user_id IS NOT NULL)");
        });

        builder.Property(payment => payment.ReceiptNumber).HasMaxLength(Payment.MaxNumberLength).IsRequired();
        builder.HasIndex(payment => new { payment.TenantId, payment.ReceiptNumber }).IsUnique();

        builder.Property(payment => payment.Amount).HasPrecision(BillingAmounts.Precision, BillingAmounts.Scale).IsRequired();
        builder.Property(payment => payment.Method).IsRequired();
        builder.Property(payment => payment.MethodCode).HasMaxLength(Payment.MaxMethodCodeLength);   // the definition's code at posting time; null on legacy rows
        builder.Property(payment => payment.Reference).HasMaxLength(Payment.MaxReferenceLength);
        builder.Property(payment => payment.TreasuryId);   // history only (LIS-CR-004): app.treasuries is an orphan table
        builder.Property(payment => payment.DeviceId).HasMaxLength(LegacyDeviceIdLength);
        builder.Property(payment => payment.BranchId).IsRequired();   // the session branch; FK to app.branches belongs to the Organization package
        builder.Property(payment => payment.PaidAtUtc).IsRequired();
        builder.Property(payment => payment.Status).IsRequired();
        builder.Property(payment => payment.VoidReason).HasMaxLength(Payment.MaxReasonLength);

        builder.HasOne<PlatformUser>().WithMany().HasForeignKey(payment => payment.CashierUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlatformUser>().WithMany().HasForeignKey(payment => payment.VoidedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(payment => new { payment.TenantId, payment.VisitId });
        builder.HasIndex(payment => new { payment.TenantId, payment.PaidAtUtc });
        builder.HasIndex(payment => new { payment.TenantId, payment.BranchId, payment.PaidAtUtc });
        builder.HasIndex(payment => new { payment.TenantId, payment.CashierUserId, payment.PaidAtUtc });
        builder.HasIndex(payment => new { payment.TenantId, payment.BatchId }).HasFilter("batch_id IS NOT NULL");

        builder.Ignore(payment => payment.IsPosted);
    }
}

/// <summary><c>app.refunds</c> — the MODULES.md §5 contract (<c>id, tenant_id, visit_id, number, amount, method, refunded_at_utc</c>) plus reference, reason, note, cashier and branch; <c>treasury_id</c> / <c>device_id</c> are nullable history (LIS-CR-004). Immutable (trigger).</summary>
internal sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("refunds", AppDbContext.AppSchema, table =>
        {
            table.HasCheckConstraint("ck_refunds_amount_positive", "amount > 0");
            table.HasCheckConstraint("ck_refunds_reference_for_non_cash", "method = 'Cash' OR method_code IS NOT NULL OR reference IS NOT NULL");
        });

        builder.Property(refund => refund.Number).HasMaxLength(Refund.MaxNumberLength).IsRequired();
        builder.HasIndex(refund => new { refund.TenantId, refund.Number }).IsUnique();

        builder.Property(refund => refund.Amount).HasPrecision(BillingAmounts.Precision, BillingAmounts.Scale).IsRequired();
        builder.Property(refund => refund.Method).IsRequired();
        builder.Property(refund => refund.MethodCode).HasMaxLength(Payment.MaxMethodCodeLength);
        builder.Property(refund => refund.Reference).HasMaxLength(Payment.MaxReferenceLength);
        builder.Property(refund => refund.ReasonId).IsRequired();   // app.reasons (Catalogue B), referenced by id
        builder.Property(refund => refund.Note).HasMaxLength(Refund.MaxNoteLength);
        builder.Property(refund => refund.TreasuryId);   // history only (LIS-CR-004)
        builder.Property(refund => refund.DeviceId).HasMaxLength(PaymentConfiguration.LegacyDeviceIdLength);
        builder.Property(refund => refund.BranchId).IsRequired();
        builder.Property(refund => refund.RefundedAtUtc).IsRequired();

        builder.HasOne<PlatformUser>().WithMany().HasForeignKey(refund => refund.CashierUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(refund => new { refund.TenantId, refund.VisitId });
        builder.HasIndex(refund => new { refund.TenantId, refund.RefundedAtUtc });
        builder.HasIndex(refund => new { refund.TenantId, refund.BranchId, refund.RefundedAtUtc });
    }
}

/// <summary><c>app.cash_closings</c>: the totals snapshot lives in columns of the same row; immutable (trigger). The scope is the branch (+ cashier); <c>treasury_id</c> is nullable history (LIS-CR-004).</summary>
internal sealed class CashClosingConfiguration : IEntityTypeConfiguration<CashClosing>
{
    public void Configure(EntityTypeBuilder<CashClosing> builder)
    {
        builder.ToTable("cash_closings", AppDbContext.AppSchema, table =>
            table.HasCheckConstraint("ck_cash_closings_period", "period_from_utc < period_to_utc"));

        builder.Property(closing => closing.Number).HasMaxLength(CashClosing.MaxNumberLength).IsRequired();
        builder.HasIndex(closing => new { closing.TenantId, closing.Number }).IsUnique();

        builder.Property(closing => closing.BranchId).IsRequired();
        builder.Property(closing => closing.TreasuryId);   // history only (LIS-CR-004)
        builder.Property(closing => closing.PeriodFromUtc).IsRequired();
        builder.Property(closing => closing.PeriodToUtc).IsRequired();
        builder.Property(closing => closing.ExpectedCash).HasPrecision(BillingAmounts.Precision, BillingAmounts.Scale).IsRequired();
        builder.Property(closing => closing.CountedCash).HasPrecision(BillingAmounts.Precision, BillingAmounts.Scale);
        builder.Property(closing => closing.Difference).HasPrecision(BillingAmounts.Precision, BillingAmounts.Scale);
        builder.Property(closing => closing.Note).HasMaxLength(CashClosing.MaxNoteLength);
        builder.Property(closing => closing.ClosedAtUtc).IsRequired();

        builder.OwnsOne(closing => closing.Totals, totals =>
        {
            Money(totals.Property(value => value.CashPayments).HasColumnName("cash_payments"));
            Money(totals.Property(value => value.CardPayments).HasColumnName("card_payments"));
            Money(totals.Property(value => value.PosPayments).HasColumnName("pos_payments"));
            Money(totals.Property(value => value.TransferPayments).HasColumnName("transfer_payments"));
            totals.Property(value => value.PaymentCount).HasColumnName("payment_count").IsRequired();
            Money(totals.Property(value => value.CashRefunds).HasColumnName("cash_refunds"));
            Money(totals.Property(value => value.CardRefunds).HasColumnName("card_refunds"));
            Money(totals.Property(value => value.PosRefunds).HasColumnName("pos_refunds"));
            Money(totals.Property(value => value.TransferRefunds).HasColumnName("transfer_refunds"));
            totals.Property(value => value.RefundCount).HasColumnName("refund_count").IsRequired();
            Money(totals.Property(value => value.OutstandingBalance).HasColumnName("outstanding_balance"));
            Money(totals.Property(value => value.DiscountTotal).HasColumnName("discount_total"));
            totals.Property(value => value.VisitCount).HasColumnName("visit_count").IsRequired();
        });
        builder.Navigation(closing => closing.Totals).IsRequired();

        builder.HasOne<PlatformUser>().WithMany().HasForeignKey(closing => closing.CashierUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlatformUser>().WithMany().HasForeignKey(closing => closing.ClosedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(closing => new { closing.TenantId, closing.BranchId, closing.PeriodFromUtc });
        builder.HasIndex(closing => new { closing.TenantId, closing.ClosedAtUtc });
    }

    private static void Money(PropertyBuilder<decimal> property) => property.HasPrecision(BillingAmounts.Precision, BillingAmounts.Scale).IsRequired();
}
