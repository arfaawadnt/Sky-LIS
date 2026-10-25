using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Application.Billing;
using Platform.Domain.Billing;
using Platform.Domain.Identity;

namespace Platform.Infrastructure.Persistence.Billing;

/// <summary>
/// <c>app.cash_entries</c> (change request LIS-CR-011, C-15): key <c>(tenant_id, id)</c>, query filter and <c>xmin</c> by
/// convention, the entry number unique per tenant, enum columns as strings with their domain CHECKs (convention), RLS declared
/// in the creating migration. The kind / category pairing, the reference rule and the void columns are repeated as CHECKs;
/// a trigger keeps the row immutable except for its void (<c>Billing.migration.sql</c>, 2026-10-24 block).
/// </summary>
internal sealed class CashEntryConfiguration : IEntityTypeConfiguration<CashEntry>
{
    public void Configure(EntityTypeBuilder<CashEntry> builder)
    {
        builder.ToTable("cash_entries", AppDbContext.AppSchema, table =>
        {
            table.HasCheckConstraint("ck_cash_entries_amount_positive", "amount > 0");
            table.HasCheckConstraint(
                "ck_cash_entries_category_of_kind",
                "(kind = 'CashOut' AND category IN ('Supplier', 'PettyCash', 'BankDeposit', 'Other')) OR (kind = 'CashIn' AND category IN ('OpeningFloat', 'OtherIncome', 'Other'))");
            table.HasCheckConstraint("ck_cash_entries_reference_for_non_cash", "method = 'Cash' OR method_code IS NOT NULL OR reference IS NOT NULL");
            table.HasCheckConstraint(
                "ck_cash_entries_void_consistency",
                "(status = 'Voided') = (void_reason IS NOT NULL AND voided_at_utc IS NOT NULL AND voided_by_user_id IS NOT NULL)");
        });

        builder.Property(entry => entry.Number).HasMaxLength(CashEntry.MaxNumberLength).IsRequired();
        builder.HasIndex(entry => new { entry.TenantId, entry.Number }).IsUnique();

        builder.Property(entry => entry.Kind).IsRequired();
        builder.Property(entry => entry.Category).IsRequired();
        builder.Property(entry => entry.Amount).HasPrecision(BillingAmounts.Precision, BillingAmounts.Scale).IsRequired();
        builder.Property(entry => entry.Method).IsRequired();
        builder.Property(entry => entry.MethodCode).HasMaxLength(Payment.MaxMethodCodeLength);
        builder.Property(entry => entry.Reference).HasMaxLength(Payment.MaxReferenceLength);
        builder.Property(entry => entry.Reason).HasMaxLength(CashEntry.MaxReasonLength).IsRequired();
        builder.Property(entry => entry.Note).HasMaxLength(CashEntry.MaxNoteLength);
        builder.Property(entry => entry.BranchId).IsRequired();   // the session branch; no FK, as payments and refunds
        builder.Property(entry => entry.PostedAtUtc).IsRequired();
        builder.Property(entry => entry.Status).IsRequired();
        builder.Property(entry => entry.VoidReason).HasMaxLength(CashEntry.MaxVoidReasonLength);

        builder.HasOne<PlatformUser>().WithMany().HasForeignKey(entry => entry.CashierUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlatformUser>().WithMany().HasForeignKey(entry => entry.VoidedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entry => new { entry.TenantId, entry.BranchId, entry.PostedAtUtc });
        builder.HasIndex(entry => new { entry.TenantId, entry.CashierUserId, entry.PostedAtUtc });

        builder.Ignore(entry => entry.IsPosted);
        builder.Ignore(entry => entry.CashEffect);
    }
}

internal sealed class CashEntryRepository(AppDbContext context) : Repository<CashEntry>(context), ICashEntryRepository;

/// <summary>
/// The cash ledger over one UNION ALL of the scope's payments (posted and voided), refunds, cash entries (posted and voided) and
/// closings. Statements are constants; every one names the tenant besides RLS. "Every cashier" is passed as the flag
/// <c>has_cashier = false</c> with an empty id, so no untyped null parameter reaches the server.
/// </summary>
internal sealed class CashLedgerReader(AppDbContext context) : ICashLedgerReader
{
    public async Task<IReadOnlyList<CashLedgerLine>> LinesAsync(CashLedgerScope scope, int maxLines, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var rows = await context.Database.SqlQueryRaw<CashLedgerSql.LineRow>(
                CashLedgerSql.Lines,
                context.CurrentTenantIdOrNil,
                scope.BranchId,
                scope.CashierUserId ?? Guid.Empty,
                scope.CashierUserId is not null,
                scope.FromUtc,
                scope.ToUtc,
                maxLines)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(row => row.ToLine()).ToList();
    }

    public Task<CashLedgerOpening> OpeningAsync(Guid branchId, Guid? cashierUserId, DateTimeOffset atUtc, CancellationToken cancellationToken) =>
        CashLedgerSql.OpeningAsync(context, branchId, cashierUserId, atUtc, cancellationToken);
}

/// <summary>The SQL of the ledger, shared by <see cref="CashLedgerReader"/> and the closing totals of <see cref="CashDrawerQueries"/>.</summary>
internal static class CashLedgerSql
{
    // {0} tenant, {1} branch, {2} cashier (empty when every cashier), {3} has_cashier, {4} from (inclusive), {5} to (exclusive).
    private const string MovementFilter = "tenant_id = {0} AND branch_id = {1} AND (NOT {3} OR cashier_user_id = {2})";

    // A closing belongs to the scope's own book: the cashier's closings, or the branch-wide ones (no cashier) for every cashier.
    private const string ClosingFilter = "tenant_id = {0} AND branch_id = {1} AND ((NOT {3} AND cashier_user_id IS NULL) OR ({3} AND cashier_user_id = {2}))";

    /// <summary>The lines in time order (a closing, at the end of its period, before a movement of the same instant); {6} = the row limit.</summary>
    public const string Lines = $"""
        SELECT x.kind, x.id, x.number, x.at_utc, x.cashier_user_id, x.visit_id, x.method, x.method_code, x.category, x.line_text, x.amount, x.is_voided,
               x.expected, x.counted, x.difference
        FROM (
            SELECT 'Receipt' AS kind, p.id, p.receipt_number AS number, p.paid_at_utc AS at_utc, p.cashier_user_id, p.visit_id, p.method, p.method_code,
                   NULL::text AS category, p.void_reason AS line_text, p.amount, (p.status = 'Voided') AS is_voided,
                   NULL::numeric AS expected, NULL::numeric AS counted, NULL::numeric AS difference, 1 AS sort_group
            FROM app.payments p
            WHERE {MovementFilter} AND p.paid_at_utc >= {"{4}"} AND p.paid_at_utc < {"{5}"}
            UNION ALL
            SELECT 'Refund', r.id, r.number, r.refunded_at_utc, r.cashier_user_id, r.visit_id, r.method, r.method_code,
                   NULL::text, r.note, r.amount, false, NULL::numeric, NULL::numeric, NULL::numeric, 1
            FROM app.refunds r
            WHERE {MovementFilter} AND r.refunded_at_utc >= {"{4}"} AND r.refunded_at_utc < {"{5}"}
            UNION ALL
            SELECT e.kind, e.id, e.number, e.posted_at_utc, e.cashier_user_id, NULL::uuid, e.method, e.method_code,
                   e.category, CASE WHEN e.status = 'Voided' THEN e.reason || ' — ' || e.void_reason ELSE e.reason END, e.amount, (e.status = 'Voided'),
                   NULL::numeric, NULL::numeric, NULL::numeric, 1
            FROM app.cash_entries e
            WHERE {MovementFilter} AND e.posted_at_utc >= {"{4}"} AND e.posted_at_utc < {"{5}"}
            UNION ALL
            SELECT 'Closing', c.id, c.number, c.period_to_utc, c.closed_by_user_id, NULL::uuid, NULL::text, NULL::text,
                   NULL::text, c.note, COALESCE(c.counted_cash, c.expected_cash), false, c.expected_cash, c.counted_cash, c.difference, 0
            FROM app.cash_closings c
            WHERE {ClosingFilter} AND c.period_to_utc > {"{4}"} AND c.period_to_utc <= {"{5}"}
        ) x
        ORDER BY x.at_utc, x.sort_group, x.number, x.id
        LIMIT {"{6}"}
        """;

    /// <summary>The scope's last closing ending at or before {4}.</summary>
    private const string PreviousClosing = $"""
        SELECT c.id, c.number, c.period_to_utc, COALESCE(c.counted_cash, c.expected_cash) AS balance
        FROM app.cash_closings c
        WHERE {ClosingFilter} AND c.period_to_utc <= {"{4}"}
        ORDER BY c.period_to_utc DESC, c.id DESC
        LIMIT 1
        """;

    /// <summary>The cash the scope's movements added to the drawer in [{4}, {5}): the rule of <see cref="CashLedgerLine.CashEffect"/> in SQL.</summary>
    private const string CashMoved = $"""
        SELECT COALESCE(SUM(x.effect), 0) AS value
        FROM (
            SELECT CASE WHEN p.status = 'Posted' AND p.method = 'Cash' THEN p.amount ELSE 0 END AS effect
            FROM app.payments p
            WHERE {MovementFilter} AND p.paid_at_utc >= {"{4}"} AND p.paid_at_utc < {"{5}"}
            UNION ALL
            SELECT CASE WHEN r.method = 'Cash' THEN -r.amount ELSE 0 END
            FROM app.refunds r
            WHERE {MovementFilter} AND r.refunded_at_utc >= {"{4}"} AND r.refunded_at_utc < {"{5}"}
            UNION ALL
            SELECT CASE WHEN e.status = 'Posted' AND e.method = 'Cash' THEN CASE WHEN e.kind = 'CashIn' THEN e.amount ELSE -e.amount END ELSE 0 END
            FROM app.cash_entries e
            WHERE {MovementFilter} AND e.posted_at_utc >= {"{4}"} AND e.posted_at_utc < {"{5}"}
        ) x
        """;

    /// <summary>The previous closing's count (else its expected cash) plus the cash moved from its end to <paramref name="atUtc"/>; none when never closed.</summary>
    public static async Task<CashLedgerOpening> OpeningAsync(AppDbContext context, Guid branchId, Guid? cashierUserId, DateTimeOffset atUtc, CancellationToken cancellationToken)
    {
        var tenant = context.CurrentTenantIdOrNil;
        var cashier = cashierUserId ?? Guid.Empty;
        var hasCashier = cashierUserId is not null;
        var previous = await context.Database.SqlQueryRaw<PreviousRow>(PreviousClosing, tenant, branchId, cashier, hasCashier, atUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (previous.Count == 0)
        {
            return CashLedgerOpening.None;
        }

        var closing = previous[0];
        var moved = closing.PeriodToUtc < atUtc
            ? (await context.Database.SqlQueryRaw<ValueRow>(CashMoved, tenant, branchId, cashier, hasCashier, closing.PeriodToUtc, atUtc).ToListAsync(cancellationToken).ConfigureAwait(false))[0].Value
            : 0m;
        return new CashLedgerOpening(closing.Balance + moved, closing.Id, closing.Number, closing.PeriodToUtc);
    }

    internal sealed class LineRow
    {
        public string Kind { get; set; } = string.Empty;

        public Guid Id { get; set; }

        public string Number { get; set; } = string.Empty;

        public DateTimeOffset AtUtc { get; set; }

        public Guid CashierUserId { get; set; }

        public Guid? VisitId { get; set; }

        public string? Method { get; set; }

        public string? MethodCode { get; set; }

        public string? Category { get; set; }

        public string? LineText { get; set; }

        public decimal Amount { get; set; }

        public bool IsVoided { get; set; }

        public decimal? Expected { get; set; }

        public decimal? Counted { get; set; }

        public decimal? Difference { get; set; }

        public CashLedgerLine ToLine() => new(
            Enum.Parse<CashLedgerLineKind>(Kind),
            Id,
            Number,
            AtUtc,
            CashierUserId,
            VisitId,
            Method is null ? null : Enum.Parse<PaymentMethod>(Method),
            MethodCode,
            Category,
            LineText,
            Amount,
            IsVoided,
            Expected,
            Counted,
            Difference);
    }

    private sealed class PreviousRow
    {
        public Guid Id { get; set; }

        public string Number { get; set; } = string.Empty;

        public DateTimeOffset PeriodToUtc { get; set; }

        public decimal Balance { get; set; }
    }

    private sealed class ValueRow
    {
        public decimal Value { get; set; }
    }
}
