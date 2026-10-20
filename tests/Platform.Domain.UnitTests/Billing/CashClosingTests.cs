using Platform.Domain.Billing;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Billing;

/// <summary>T5 Close cash drawer and T4 pay-all distribution (intake LIS-INT-BIL-001 §16).</summary>
public sealed class CashClosingTests
{
    private static readonly DateTimeOffset Now = FixedClock.Instant;
    private static readonly Guid Branch = Guid.CreateVersion7();
    private static readonly Guid Cashier = Guid.CreateVersion7();
    private static readonly Guid Manager = Guid.CreateVersion7();
    private static readonly DateTimeOffset From = Now.AddHours(-8);

    // cash 500, card 200, POS 30, transfer 20 in; cash 50 and card 10 back
    private static readonly ClosingTotals Day = new(500m, 200m, 30m, 20m, 7, 50m, 10m, 0m, 0m, 2, 120m, 35m, 6);

    private static CashClosing Close(decimal? counted = 450m, string? note = null, Guid? cashier = null, Guid? sessionBranch = null) =>
        CashClosing.Close("CLS-2026-00001", sessionBranch ?? Branch, cashier ?? Cashier, From, Now, Day, counted, note, Manager, Now);

    [Fact]
    public void BR_16_expected_cash_is_cash_payments_minus_cash_refunds()
    {
        Assert.Equal(450m, Day.ExpectedCash);
        Assert.Equal(750m, Day.TotalPayments);
        Assert.Equal(60m, Day.TotalRefunds);
    }

    [Fact]
    public void T5_Close_records_the_snapshot_without_difference_when_the_count_matches()
    {
        var closing = Close();

        Assert.Equal((450m, 450m, 0m), (closing.ExpectedCash, closing.CountedCash, closing.Difference));
        Assert.Null(closing.TreasuryId);   // LIS-CR-004: the scope is the session branch + cashier
        Assert.Equal((Branch, Cashier, From, Now, Manager, Now), (closing.BranchId, closing.CashierUserId, closing.PeriodFromUtc, closing.PeriodToUtc, closing.ClosedByUserId, closing.ClosedAtUtc));
        Assert.Same(Day, closing.Totals);
        var raised = Assert.IsType<CashClosingClosed>(Assert.Single(closing.DomainEvents));
        Assert.Equal(("CLS-2026-00001", 450m, 0m, Branch, (Guid?)Cashier), (raised.Number, raised.ExpectedCash, raised.Difference, raised.BranchId, raised.CashierUserId));
    }

    [Fact]
    public void T5_A_difference_needs_a_note_BIL_034()
    {
        var refusal = Assert.Throws<DomainException>(() => Close(counted: 440m));
        Assert.Equal((BillingCodes.DifferenceNeedsNote, DomainFault.Validation), (refusal.Code, refusal.Fault));

        var explained = Close(counted: 440m, note: "  10 short, cashier informed ");
        Assert.Equal((-10m, "10 short, cashier informed"), (explained.Difference, explained.Note));
    }

    [Fact]
    public void T5_Without_a_count_there_is_no_difference()
    {
        var closing = Close(counted: null);
        Assert.Null(closing.CountedCash);
        Assert.Null(closing.Difference);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10.001)]
    public void T5_Counted_cash_is_zero_or_more_with_two_decimals_BIL_032(double counted)
    {
        Assert.Equal(BillingCodes.CountedCashInvalid, Assert.Throws<DomainException>(() => Close(counted: (decimal)counted)).Code);
    }

    [Fact]
    public void T5_Period_rules_BIL_030_031_033()
    {
        Assert.Equal(BillingCodes.PeriodInvalid, Assert.Throws<DomainException>(() => CashClosing.Close("CLS-1", Branch, null, Now, Now, Day, null, null, Manager, Now)).Code);
        Assert.Equal(BillingCodes.PeriodInFuture, Assert.Throws<DomainException>(() => CashClosing.Close("CLS-1", Branch, null, From, Now.AddMinutes(1), Day, null, null, Manager, Now)).Code);
        Assert.Equal(BillingCodes.PeriodTooLong, Assert.Throws<DomainException>(() => CashClosing.Close("CLS-1", Branch, null, Now.AddDays(-32), Now, Day, null, null, Manager, Now)).Code);
        CashClosing.RequirePeriod(Now.AddDays(-31), Now, Now);
    }

    [Fact]
    public void T5_The_scope_is_the_session_branch_TENANT_004_without_one()
    {
        var refusal = Assert.Throws<DomainException>(() => Close(sessionBranch: Guid.Empty));
        Assert.Equal(("TENANT-004", DomainFault.Refused), (refusal.Code, refusal.Fault));

        var otherBranch = Guid.CreateVersion7();
        Assert.Equal(otherBranch, Close(sessionBranch: otherBranch).BranchId);
    }

    [Fact]
    public void T5_CheckClose_refuses_like_Close_without_creating_anything()
    {
        CashClosing.CheckClose(Branch, From, Now, Day, 450m, null, Now);
        Assert.Equal(BillingCodes.DifferenceNeedsNote, Assert.Throws<DomainException>(() => CashClosing.CheckClose(Branch, From, Now, Day, 1m, null, Now)).Code);
        Assert.Equal("TENANT-004", Assert.Throws<DomainException>(() => CashClosing.CheckClose(Guid.Empty, From, Now, Day, 450m, null, Now)).Code);
    }

    [Fact]
    public void INV_BIL_006_a_closing_covers_its_branch_cashier_scope_and_half_open_period()
    {
        var mine = Close();
        Assert.True(mine.Covers(Branch, Cashier, From));
        Assert.True(mine.Covers(Branch, Cashier, Now.AddTicks(-1)));
        Assert.False(mine.Covers(Branch, Cashier, Now));
        Assert.False(mine.Covers(Branch, Guid.CreateVersion7(), From.AddHours(1)));
        Assert.False(mine.Covers(Guid.CreateVersion7(), Cashier, From.AddHours(1)));

        var all = CashClosing.Close("CLS-2", Branch, null, From, Now, Day, null, null, Manager, Now);
        Assert.True(all.Covers(Branch, Guid.CreateVersion7(), From.AddHours(1)));
    }

    [Fact]
    public void INV_BIL_005_closed_periods_overlap_on_the_same_branch_for_intersecting_cashier_scopes()
    {
        var mine = Close();

        Assert.True(mine.Overlaps(Branch, Cashier, From.AddHours(-1), From.AddMinutes(1)));
        Assert.True(mine.Overlaps(Branch, null, From.AddHours(1), Now.AddHours(1)));
        Assert.False(mine.Overlaps(Branch, Guid.CreateVersion7(), From, Now));
        Assert.False(mine.Overlaps(Branch, Cashier, Now, Now.AddHours(1)));
        Assert.False(mine.Overlaps(Guid.CreateVersion7(), Cashier, From, Now));
    }

    [Fact]
    public void T4_Pay_all_distributes_in_accession_order_up_to_each_remaining()
    {
        var (a, b, c, d) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var members = new List<BatchMemberDue> { new(a, 100m), new(b, 0m), new(c, 150m), new(d, -20m) };

        var shares = BatchAllocation.Distribute(200m, members, billedThroughClaims: false);

        Assert.Equal([new BatchShare(a, 100m), new BatchShare(c, 100m)], shares);
        Assert.Equal([new BatchShare(a, 100m), new BatchShare(c, 150m)], BatchAllocation.Distribute(250m, members, false));
    }

    [Fact]
    public void T4_Pay_all_is_refused_above_the_batch_remaining_BIL_014_and_on_lab_to_lab_batches_BIL_015()
    {
        var members = new List<BatchMemberDue> { new(Guid.CreateVersion7(), 100m) };

        var over = Assert.Throws<DomainException>(() => BatchAllocation.Distribute(100.01m, members, false));
        Assert.Equal((BillingCodes.AmountExceedsBatchRemaining, DomainFault.Conflict), (over.Code, over.Fault));
        Assert.Equal(BillingCodes.BilledThroughClaims, Assert.Throws<DomainException>(() => BatchAllocation.Distribute(10m, members, true)).Code);
        Assert.Equal(BillingCodes.AmountInvalid, Assert.Throws<DomainException>(() => BatchAllocation.Distribute(0m, members, false)).Code);
    }
}
