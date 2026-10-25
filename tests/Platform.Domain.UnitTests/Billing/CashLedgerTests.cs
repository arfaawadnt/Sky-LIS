using Platform.Domain.Billing;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Billing;

/// <summary>The cash ledger arithmetic (CP-70, change request LIS-CR-011, intake LIS-INT-BIL-001 §18 scenarios L1–L6).</summary>
public sealed class CashLedgerTests
{
    private static readonly DateTimeOffset Start = FixedClock.Instant.AddHours(-10);
    private static readonly Guid Cashier = Guid.CreateVersion7();

    private static CashLedgerLine Line(CashLedgerLineKind kind, int minute, decimal amount, PaymentMethod? method = PaymentMethod.Cash, bool voided = false, string? number = null) =>
        new(kind, Guid.CreateVersion7(), number ?? $"{kind}-{minute:000}", Start.AddMinutes(minute), Cashier, kind is CashLedgerLineKind.Receipt or CashLedgerLineKind.Refund ? Guid.CreateVersion7() : null, method, null, null, null, amount, voided);

    private static CashLedgerLine Closing(int minute, decimal expected, decimal? counted) =>
        new(CashLedgerLineKind.Closing, Guid.CreateVersion7(), $"CLS-{minute}", Start.AddMinutes(minute), Cashier, null, null, null, null, null, counted ?? expected, false, expected, counted, counted - expected);

    [Fact]
    public void L1_The_running_balance_starts_at_the_opening_and_moves_with_cash_only()
    {
        var opening = new CashLedgerOpening(500m, Guid.CreateVersion7(), "CLS-2026-00009", Start.AddHours(-2));
        var (rows, summary) = CashLedger.Build(opening,
        [
            Line(CashLedgerLineKind.CashOut, 30, 120m),                                  // supplier, cash
            Line(CashLedgerLineKind.Receipt, 10, 300m),                                  // ordered by time, not by input
            Line(CashLedgerLineKind.Receipt, 20, 200m, PaymentMethod.Card),              // card: not in the drawer
            Line(CashLedgerLineKind.Refund, 40, 50m),
            Line(CashLedgerLineKind.CashIn, 50, 80m),
            Line(CashLedgerLineKind.Refund, 60, 20m, PaymentMethod.Pos),
        ]);

        Assert.Equal([800m, 800m, 680m, 630m, 710m, 710m], rows.Select(row => row.Balance).ToArray());
        Assert.Equal([300m, 0m, 0m, 0m, 80m, 0m], rows.Select(row => row.MoneyIn).ToArray());
        Assert.Equal([0m, 0m, 120m, 50m, 0m, 0m], rows.Select(row => row.MoneyOut).ToArray());

        Assert.Equal((500m, 710m, 210m), (summary.OpeningBalance, summary.ClosingBalance, summary.NetCash));
        Assert.Equal((300m, 200m, 500m, 2), (summary.Receipts.Cash, summary.Receipts.Card, summary.Receipts.Total, summary.ReceiptCount));
        Assert.Equal((50m, 20m, 70m, 2), (summary.Refunds.Cash, summary.Refunds.Pos, summary.Refunds.Total, summary.RefundCount));
        Assert.Equal((80m, 120m, 2), (summary.CashIn, summary.CashOut, summary.EntryCount));
    }

    [Fact]
    public void L2_Voided_receipts_and_entries_are_listed_but_counted_nowhere()
    {
        var (rows, summary) = CashLedger.Build(CashLedgerOpening.None,
        [
            Line(CashLedgerLineKind.Receipt, 1, 100m, voided: true),
            Line(CashLedgerLineKind.CashOut, 2, 40m, voided: true),
            Line(CashLedgerLineKind.Receipt, 3, 60m),
        ]);

        Assert.Equal([0m, 0m, 60m], rows.Select(row => row.Balance).ToArray());
        Assert.Equal((2, 140m, 1, 0, 60m), (summary.VoidedCount, summary.VoidedAmount, summary.ReceiptCount, summary.EntryCount, summary.ClosingBalance));
    }

    [Fact]
    public void L3_A_closing_with_a_count_resets_the_balance_and_reports_the_difference()
    {
        var (rows, summary) = CashLedger.Build(CashLedgerOpening.None,
        [
            Line(CashLedgerLineKind.Receipt, 10, 300m),
            Closing(60, expected: 300m, counted: 290m),
            Line(CashLedgerLineKind.Receipt, 60, 50m, number: "RCT-AFTER"),               // same instant: after the closing (half-open period)
        ]);

        Assert.Equal([CashLedgerLineKind.Receipt, CashLedgerLineKind.Closing, CashLedgerLineKind.Receipt], rows.Select(row => row.Line.Kind).ToArray());
        Assert.Equal([300m, 290m, 340m], rows.Select(row => row.Balance).ToArray());
        Assert.Equal((0m, 10m), (rows[1].MoneyIn, rows[1].MoneyOut));
        Assert.Equal((-10m, 1, 340m), (summary.CountDifferences, summary.ClosingCount, summary.ClosingBalance));
    }

    [Fact]
    public void L3_A_closing_without_a_count_leaves_the_balance()
    {
        var (rows, summary) = CashLedger.Build(new CashLedgerOpening(20m, null, null, null), [Line(CashLedgerLineKind.Receipt, 1, 30m), Closing(5, expected: 50m, counted: null)]);
        Assert.Equal([50m, 50m], rows.Select(row => row.Balance).ToArray());
        Assert.Equal(0m, summary.CountDifferences);
    }

    [Fact]
    public void L4_The_close_window_starts_after_the_last_closing_of_the_scope_and_ends_now()
    {
        var from = Start;
        var to = Start.AddDays(1);
        var now = Start.AddHours(9);

        Assert.Equal((from, now), CashLedger.CloseWindow(from, to, CashLedgerOpening.None, [], now));
        Assert.Equal((Start.AddHours(1), now), CashLedger.CloseWindow(from, to, new CashLedgerOpening(5m, Guid.CreateVersion7(), "CLS-1", Start.AddHours(1)), [], now));
        Assert.Equal((Start.AddMinutes(300), now), CashLedger.CloseWindow(from, to, CashLedgerOpening.None, [Closing(120, 1m, 1m), Closing(300, 1m, 1m), Line(CashLedgerLineKind.Receipt, 400, 1m)], now));

        Assert.Null(CashLedger.CloseWindow(from, to, CashLedgerOpening.None, [Closing(540, 1m, 1m)], now));   // closed up to now
        Assert.Null(CashLedger.CloseWindow(from.AddDays(-2), to.AddDays(-2), CashLedgerOpening.None, [], now));   // a past day is read-only
        Assert.Null(CashLedger.CloseWindow(from.AddDays(1), to.AddDays(1), CashLedgerOpening.None, [], now));    // a future day
    }

    [Fact]
    public void L5_The_ledger_refuses_too_many_lines_and_too_long_a_period()
    {
        CashLedger.RequireWithinLimit(CashLedger.MaxLines);
        Assert.Equal(BillingCodes.LedgerTooLarge, Assert.Throws<DomainException>(() => CashLedger.RequireWithinLimit(CashLedger.MaxLines + 1)).Code);

        var first = new DateOnly(2026, 10, 1);
        CashLedger.RequirePeriodLength(first, first.AddDays(CashLedger.MaxPeriodDays - 1));
        Assert.Equal(BillingCodes.PeriodTooLong, Assert.Throws<DomainException>(() => CashLedger.RequirePeriodLength(first, first.AddDays(CashLedger.MaxPeriodDays))).Code);
    }

    [Fact]
    public void L6_The_line_effect_follows_the_entry_rule()
    {
        Assert.Equal(0m, Line(CashLedgerLineKind.Closing, 1, 10m).CashEffect);
        Assert.Equal(-10m, Line(CashLedgerLineKind.Refund, 1, 10m).CashEffect);
        Assert.Equal(0m, Line(CashLedgerLineKind.CashIn, 1, 10m, PaymentMethod.Transfer).CashEffect);
        Assert.Equal(CashEntry.EffectOf(CashEntryKind.CashOut, PaymentMethod.Cash, CashEntryStatus.Posted, 10m), Line(CashLedgerLineKind.CashOut, 1, 10m).CashEffect);
    }
}
