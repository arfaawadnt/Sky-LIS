using Platform.Domain.Billing;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Billing;

/// <summary>T6 Post cash entry, T7 Void cash entry and the closed-period rule (change request LIS-CR-011, intake LIS-INT-BIL-001 §18).</summary>
public sealed class CashEntryTests
{
    private static readonly DateTimeOffset Now = FixedClock.Instant;
    private static readonly Cashier Desk = new(Guid.CreateVersion7(), Guid.CreateVersion7());
    private static readonly Guid Supervisor = Guid.CreateVersion7();
    private static readonly PaymentMethodUse Cash = PaymentMethodUse.Legacy(PaymentMethod.Cash);

    private static CashEntry Post(
        CashEntryKind kind = CashEntryKind.CashOut,
        CashEntryCategory category = CashEntryCategory.Supplier,
        decimal amount = 150m,
        PaymentMethodUse? method = null,
        string? reference = null,
        string? reason = "Reagent supplier, invoice 77",
        string? note = null,
        Cashier? cashier = null,
        bool closed = false) =>
        CashEntry.Post("CSH-2026-00001", kind, category, amount, method ?? Cash, reference, reason, note, cashier ?? Desk, closed, Now);

    [Fact]
    public void T6_A_cash_out_is_posted_for_the_session_cashier_and_branch_and_takes_cash_out_of_the_drawer()
    {
        var entry = Post(note: "  paid at the door ");

        Assert.Equal(("CSH-2026-00001", CashEntryKind.CashOut, CashEntryCategory.Supplier, 150m, PaymentMethod.Cash), (entry.Number, entry.Kind, entry.Category, entry.Amount, entry.Method));
        Assert.Equal((Desk.UserId, Desk.BranchId, Now, CashEntryStatus.Posted), (entry.CashierUserId, entry.BranchId, entry.PostedAtUtc, entry.Status));
        Assert.Equal(("Reagent supplier, invoice 77", "paid at the door"), (entry.Reason, entry.Note));
        Assert.Equal(-150m, entry.CashEffect);
        var raised = Assert.IsType<CashEntryPosted>(Assert.Single(entry.DomainEvents));
        Assert.Equal((entry.Id, "CashOut", "Supplier", 150m, "Cash", Desk.BranchId), (raised.EntryId, raised.Kind, raised.Category, raised.Amount, raised.Method, raised.BranchId));
    }

    [Fact]
    public void T6_A_cash_in_adds_to_the_drawer_and_another_method_does_not_move_it()
    {
        Assert.Equal(200m, Post(CashEntryKind.CashIn, CashEntryCategory.OpeningFloat, 200m).CashEffect);

        var transfer = Post(method: PaymentMethodUse.Legacy(PaymentMethod.Transfer), reference: "TRX-1");
        Assert.Equal((PaymentMethod.Transfer, "TRX-1", 0m), (transfer.Method, transfer.Reference, transfer.CashEffect));
    }

    [Theory]
    [InlineData(CashEntryKind.CashOut, CashEntryCategory.OpeningFloat)]
    [InlineData(CashEntryKind.CashOut, CashEntryCategory.OtherIncome)]
    [InlineData(CashEntryKind.CashIn, CashEntryCategory.Supplier)]
    [InlineData(CashEntryKind.CashIn, CashEntryCategory.BankDeposit)]
    [InlineData(CashEntryKind.CashIn, (CashEntryCategory)99)]
    public void T6_A_category_of_the_other_kind_is_refused_BIL_062(CashEntryKind kind, CashEntryCategory category)
    {
        var refusal = Assert.Throws<DomainException>(() => Post(kind, category));
        Assert.Equal((BillingCodes.CashEntryCategoryInvalid, DomainFault.Validation), (refusal.Code, refusal.Fault));
    }

    [Theory]
    [InlineData(CashEntryKind.CashOut, CashEntryCategory.PettyCash)]
    [InlineData(CashEntryKind.CashOut, CashEntryCategory.BankDeposit)]
    [InlineData(CashEntryKind.CashOut, CashEntryCategory.Other)]
    [InlineData(CashEntryKind.CashIn, CashEntryCategory.OtherIncome)]
    [InlineData(CashEntryKind.CashIn, CashEntryCategory.Other)]
    public void T6_Every_category_of_its_kind_is_accepted(CashEntryKind kind, CashEntryCategory category) =>
        Assert.Equal(category, Post(kind, category).Category);

    [Fact]
    public void T6_The_guards_have_their_codes()
    {
        Assert.Equal(BillingCodes.CashEntryReasonRequired, Assert.Throws<DomainException>(() => Post(reason: "   ")).Code);
        Assert.Equal(BillingCodes.TextTooLong, Assert.Throws<DomainException>(() => Post(reason: new string('x', CashEntry.MaxReasonLength + 1))).Code);
        Assert.Equal(BillingCodes.TextTooLong, Assert.Throws<DomainException>(() => Post(note: new string('x', CashEntry.MaxNoteLength + 1))).Code);
        Assert.Equal(BillingCodes.AmountInvalid, Assert.Throws<DomainException>(() => Post(amount: 0m)).Code);
        Assert.Equal(BillingCodes.AmountScale, Assert.Throws<DomainException>(() => Post(amount: 1.005m)).Code);
        Assert.Equal(BillingCodes.ReferenceRequired, Assert.Throws<DomainException>(() => Post(method: PaymentMethodUse.Legacy(PaymentMethod.Card))).Code);
        Assert.Equal("TENANT-004", Assert.Throws<DomainException>(() => Post(cashier: new Cashier(Desk.UserId, Guid.Empty))).Code);
        Assert.Equal("AUTH-001", Assert.Throws<DomainException>(() => Post(cashier: new Cashier(Guid.Empty, Desk.BranchId))).Code);
    }

    [Fact]
    public void T6_Nothing_is_posted_inside_a_closed_period_BIL_061()
    {
        var refusal = Assert.Throws<DomainException>(() => Post(closed: true));
        Assert.Equal((BillingCodes.PeriodClosed, DomainFault.Conflict), (refusal.Code, refusal.Fault));
        Assert.Equal(BillingCodes.PeriodClosed, Assert.Throws<DomainException>(() => CashEntry.CheckPost(CashEntryKind.CashIn, CashEntryCategory.Other, 1m, Cash, null, "x", null, Desk, insideClosedPeriod: true)).Code);
        Assert.Equal(BillingCodes.PeriodClosed, Assert.Throws<DomainException>(() => ClosedPeriods.RequireOpen(true)).Code);
        ClosedPeriods.RequireOpen(false);
    }

    [Fact]
    public void T7_A_void_needs_a_reason_counts_nowhere_afterwards_and_happens_once()
    {
        var entry = Post();
        entry.ClearDomainEvents();

        Assert.Equal(BillingCodes.VoidReasonRequired, Assert.Throws<DomainException>(() => entry.Void(Supervisor, " ", frozenByClosing: false, Now)).Code);
        entry.Void(Supervisor, " wrong amount ", frozenByClosing: false, Now.AddMinutes(5));

        Assert.Equal((CashEntryStatus.Voided, "wrong amount", (Guid?)Supervisor, (DateTimeOffset?)Now.AddMinutes(5), 0m), (entry.Status, entry.VoidReason, entry.VoidedByUserId, entry.VoidedAtUtc, entry.CashEffect));
        var raised = Assert.IsType<CashEntryVoided>(Assert.Single(entry.DomainEvents));
        Assert.Equal((entry.Id, 150m, "wrong amount"), (raised.EntryId, raised.Amount, raised.Reason));

        var again = Assert.Throws<InvalidStateTransitionException>(() => entry.Void(Supervisor, "again", frozenByClosing: false, Now));
        Assert.Equal(BillingCodes.CashEntryNotPosted, again.Code);
    }

    [Fact]
    public void T7_An_entry_inside_a_closed_period_is_frozen_BIL_061()
    {
        var entry = Post();
        var refusal = Assert.Throws<DomainException>(() => entry.Void(Supervisor, "late", frozenByClosing: true, Now));
        Assert.Equal((BillingCodes.PeriodClosed, DomainFault.Conflict), (refusal.Code, refusal.Fault));
        Assert.True(entry.IsPosted);
    }

    [Fact]
    public void The_effect_rule_is_the_same_for_readers_of_the_columns()
    {
        Assert.Equal(10m, CashEntry.EffectOf(CashEntryKind.CashIn, PaymentMethod.Cash, CashEntryStatus.Posted, 10m));
        Assert.Equal(-10m, CashEntry.EffectOf(CashEntryKind.CashOut, PaymentMethod.Cash, CashEntryStatus.Posted, 10m));
        Assert.Equal(0m, CashEntry.EffectOf(CashEntryKind.CashOut, PaymentMethod.Cash, CashEntryStatus.Voided, 10m));
        Assert.Equal(0m, CashEntry.EffectOf(CashEntryKind.CashIn, PaymentMethod.Pos, CashEntryStatus.Posted, 10m));
    }

    [Fact]
    public void BR_16_extended_expected_cash_adds_the_opening_and_the_cash_entries()
    {
        var day = new ClosingTotals(500m, 200m, 0m, 0m, 3, 50m, 0m, 0m, 0m, 1, 0m, 0m, 2, OpeningBalance: 300m, CashIn: 100m, CashOut: 175m, EntryCount: 3);

        Assert.Equal(300m + 500m - 50m + 100m - 175m, day.ExpectedCash);
        Assert.Equal(450m, (day with { OpeningBalance = 0m, CashIn = 0m, CashOut = 0m }).ExpectedCash);   // the original rule
    }
}
