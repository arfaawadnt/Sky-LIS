using Platform.Domain.Billing;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Billing;

/// <summary>Acceptance scenarios T1–T3 of intake LIS-INT-BIL-001 §16: one test per transition, refused precondition and invariant (03 §10).</summary>
public sealed class VisitAccountTests
{
    private static readonly DateTimeOffset Now = FixedClock.Instant;
    private static readonly Guid Cashier = Guid.CreateVersion7();
    private static readonly Guid Supervisor = Guid.CreateVersion7();
    private static readonly Guid Branch = Guid.CreateVersion7();
    private static readonly Guid Treasury = Guid.CreateVersion7();
    private static readonly Guid Reason = Guid.CreateVersion7();

    private static CashierStation Station(bool active = true, bool approved = true, Guid? treasuryBranch = null, string device = " device-1 ") =>
        new(Treasury, treasuryBranch ?? Branch, active, approved, device, Cashier, Branch);

    private static VisitCharge Charge(decimal share, bool claims = false) => new(share, claims);

    private static VisitAccount Account() => VisitAccount.Open(Guid.CreateVersion7());

    [Fact]
    public void T1_Record_payment_partial_then_full_settles_the_balance()
    {
        var account = Account();

        var cash = account.RecordPayment("RCT-2026-00001", 100m, PaymentMethod.Cash, null, Station(), Charge(300m), Now);
        var card = account.RecordPayment("RCT-2026-00002", 200m, PaymentMethod.Card, " SLIP-1 ", Station(), Charge(300m), Now.AddMinutes(1));

        Assert.Equal(300m, account.PaidTotal);
        var balance = account.BalanceFor(300m);
        Assert.Equal((0m, 0m, BalanceStatus.Paid), (balance.Remaining, balance.Refundable, balance.Status));
        Assert.Equal(PaymentStatus.Posted, cash.Status);
        Assert.Equal(("device-1", Treasury, Cashier, Branch, account.VisitId), (cash.DeviceId, cash.TreasuryId, cash.CashierUserId, cash.BranchId, cash.VisitId));
        Assert.Equal("SLIP-1", card.Reference);
        Assert.Equal(2, account.Payments.Count);
        var events = account.DomainEvents.OfType<PaymentRecorded>().ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal(("RCT-2026-00002", 200m, "Card", Treasury, Branch), (events[1].ReceiptNumber, events[1].Amount, events[1].Method, events[1].TreasuryId, events[1].BranchId));
    }

    [Fact]
    public void T1_Partial_payment_leaves_status_Partial()
    {
        var account = Account();
        Assert.Equal(BalanceStatus.Unpaid, account.BalanceFor(80m).Status);

        account.RecordPayment("RCT-2026-00001", 30m, PaymentMethod.Cash, null, Station(), Charge(80m), Now);

        Assert.Equal((50m, BalanceStatus.Partial), (account.BalanceFor(80m).Remaining, account.BalanceFor(80m).Status));
    }

    [Fact]
    public void T1_refused_above_the_remaining_balance_BIL_010_and_nothing_is_recorded()
    {
        var account = Account();
        account.RecordPayment("RCT-2026-00001", 250m, PaymentMethod.Cash, null, Station(), Charge(300m), Now);
        account.ClearDomainEvents();

        var refusal = Assert.Throws<DomainException>(() => account.RecordPayment("RCT-2026-00002", 60m, PaymentMethod.Cash, null, Station(), Charge(300m), Now));

        Assert.Equal((BillingCodes.AmountExceedsRemaining, DomainFault.Conflict), (refusal.Code, refusal.Fault));
        Assert.Single(account.Payments);
        Assert.Equal(250m, account.PaidTotal);
        Assert.Empty(account.DomainEvents);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void T1_refused_for_a_non_positive_amount_BIL_001(int amount)
    {
        var refusal = Assert.Throws<DomainException>(() => Account().RecordPayment("RCT-2026-00001", amount, PaymentMethod.Cash, null, Station(), Charge(100m), Now));
        Assert.Equal((BillingCodes.AmountInvalid, DomainFault.Validation), (refusal.Code, refusal.Fault));
    }

    [Fact]
    public void T1_refused_with_more_than_two_decimals_BIL_002()
    {
        var refusal = Assert.Throws<DomainException>(() => Account().RecordPayment("RCT-2026-00001", 10.005m, PaymentMethod.Cash, null, Station(), Charge(100m), Now));
        Assert.Equal(BillingCodes.AmountScale, refusal.Code);
    }

    [Theory]
    [InlineData(PaymentMethod.Card)]
    [InlineData(PaymentMethod.Pos)]
    [InlineData(PaymentMethod.Transfer)]
    public void T1_refused_without_a_reference_for_non_cash_BIL_003(PaymentMethod method)
    {
        var refusal = Assert.Throws<DomainException>(() => Account().RecordPayment("RCT-2026-00001", 10m, method, "  ", Station(), Charge(100m), Now));
        Assert.Equal((BillingCodes.ReferenceRequired, DomainFault.Validation), (refusal.Code, refusal.Fault));
    }

    [Fact]
    public void T1_cash_needs_no_reference_and_a_blank_one_is_dropped()
    {
        var payment = Account().RecordPayment("RCT-2026-00001", 10m, PaymentMethod.Cash, " ", Station(), Charge(100m), Now);
        Assert.Null(payment.Reference);
    }

    [Fact]
    public void T1_station_refusals_follow_BR_27_in_order()
    {
        Assert.Equal(BillingCodes.DeviceRequired, Assert.Throws<DomainException>(() => Account().RecordPayment("R-1", 10m, PaymentMethod.Cash, null, Station(device: " "), Charge(100m), Now)).Code);

        var inactive = Assert.Throws<DomainException>(() => Account().RecordPayment("R-1", 10m, PaymentMethod.Cash, null, Station(active: false, approved: false), Charge(100m), Now));
        Assert.Equal((BillingCodes.TreasuryInactive, DomainFault.Conflict), (inactive.Code, inactive.Fault));

        var otherBranch = Assert.Throws<DomainException>(() => Account().RecordPayment("R-1", 10m, PaymentMethod.Cash, null, Station(treasuryBranch: Guid.CreateVersion7()), Charge(100m), Now));
        Assert.Equal((BillingCodes.TreasuryOfAnotherBranch, DomainFault.Refused), (otherBranch.Code, otherBranch.Fault));

        var notApproved = Assert.Throws<DomainException>(() => Account().RecordPayment("R-1", 10m, PaymentMethod.Cash, null, Station(approved: false), Charge(100m), Now));
        Assert.Equal((BillingCodes.TreasuryNotApproved, DomainFault.Refused), (notApproved.Code, notApproved.Fault));
    }

    [Fact]
    public void T1_refused_on_a_visit_billed_through_claims_BIL_015()
    {
        var refusal = Assert.Throws<DomainException>(() => Account().RecordPayment("RCT-2026-00001", 10m, PaymentMethod.Cash, null, Station(), Charge(100m, claims: true), Now));
        Assert.Equal((BillingCodes.BilledThroughClaims, DomainFault.Conflict), (refusal.Code, refusal.Fault));
    }

    [Fact]
    public void T1_CheckPayment_refuses_like_RecordPayment_and_changes_nothing()
    {
        var account = Account();

        account.CheckPayment(100m, PaymentMethod.Cash, null, Station(), Charge(100m));
        Assert.Equal(BillingCodes.AmountExceedsRemaining, Assert.Throws<DomainException>(() => account.CheckPayment(101m, PaymentMethod.Cash, null, Station(), Charge(100m))).Code);

        Assert.Empty(account.Payments);
        Assert.Equal(0m, account.PaidTotal);
        Assert.Empty(account.DomainEvents);
    }

    [Fact]
    public void T1_refused_without_a_receipt_number_BIL_008()
    {
        Assert.Equal(BillingCodes.NumberRequired, Assert.Throws<DomainException>(() => Account().RecordPayment(" ", 10m, PaymentMethod.Cash, null, Station(), Charge(100m), Now)).Code);
    }

    [Fact]
    public void T2_Void_payment_records_reason_voider_and_time_and_restores_the_balance()
    {
        var account = Account();
        var payment = account.RecordPayment("RCT-2026-00001", 100m, PaymentMethod.Cash, null, Station(), Charge(100m), Now);
        account.ClearDomainEvents();

        account.VoidPayment(payment.Id, Supervisor, "  wrong visit ", frozenByClosing: false, Now.AddMinutes(5));

        Assert.Equal(PaymentStatus.Voided, payment.Status);
        Assert.Equal(("wrong visit", Supervisor, Now.AddMinutes(5)), (payment.VoidReason, payment.VoidedByUserId, payment.VoidedAtUtc));
        Assert.Equal(0m, account.PaidTotal);
        Assert.Equal(100m, account.BalanceFor(100m).Remaining);
        var voided = Assert.IsType<PaymentVoided>(Assert.Single(account.DomainEvents));
        Assert.Equal((payment.Id, "wrong visit", 100m), (voided.PaymentId, voided.Reason, voided.Amount));
    }

    [Fact]
    public void T2_Voiding_a_voided_payment_is_refused_BIL_012()
    {
        var account = Account();
        var payment = account.RecordPayment("RCT-2026-00001", 100m, PaymentMethod.Cash, null, Station(), Charge(100m), Now);
        account.VoidPayment(payment.Id, Supervisor, "first", frozenByClosing: false, Now);

        var refusal = Assert.Throws<InvalidStateTransitionException>(() => account.VoidPayment(payment.Id, Supervisor, "again", frozenByClosing: false, Now));

        Assert.Equal(BillingCodes.PaymentNotPosted, refusal.Code);
        Assert.Equal(0m, account.PaidTotal);
    }

    [Fact]
    public void T2_Void_refused_inside_a_closed_cash_closing_BIL_013()
    {
        var account = Account();
        var payment = account.RecordPayment("RCT-2026-00001", 100m, PaymentMethod.Cash, null, Station(), Charge(100m), Now);

        var refusal = Assert.Throws<DomainException>(() => account.VoidPayment(payment.Id, Supervisor, "late", frozenByClosing: true, Now));

        Assert.Equal((BillingCodes.PaymentFrozen, DomainFault.Conflict), (refusal.Code, refusal.Fault));
        Assert.Equal(PaymentStatus.Posted, payment.Status);
    }

    [Fact]
    public void T2_Void_needs_a_reason_BIL_005_of_at_most_500_characters_BIL_007()
    {
        var account = Account();
        var payment = account.RecordPayment("RCT-2026-00001", 100m, PaymentMethod.Cash, null, Station(), Charge(100m), Now);

        Assert.Equal(BillingCodes.VoidReasonRequired, Assert.Throws<DomainException>(() => account.VoidPayment(payment.Id, Supervisor, " ", false, Now)).Code);
        Assert.Equal(BillingCodes.TextTooLong, Assert.Throws<DomainException>(() => account.VoidPayment(payment.Id, Supervisor, new string('x', 501), false, Now)).Code);
        Assert.Equal(BillingCodes.PaymentNotFound, Assert.Throws<DomainException>(() => account.VoidPayment(Guid.CreateVersion7(), Supervisor, "reason", false, Now)).Code);
        Assert.Equal(PaymentStatus.Posted, payment.Status);
    }

    [Fact]
    public void T3_Record_refund_returns_a_negative_balance_BR_14()
    {
        var account = Account();
        account.RecordPayment("RCT-2026-00001", 300m, PaymentMethod.Cash, null, Station(), Charge(300m), Now);
        account.ClearDomainEvents();
        var reprised = Charge(200m);   // a contract transfer lowered the patient share (VisitTransferred)
        Assert.Equal((-100m, 100m, BalanceStatus.RefundDue), (account.BalanceFor(200m).Remaining, account.BalanceFor(200m).Refundable, account.BalanceFor(200m).Status));

        var refund = account.RecordRefund("RFD-2026-00001", 100m, PaymentMethod.Cash, null, Reason, "  patient transferred ", Station(), reprised, Now);

        Assert.Equal((100m, "patient transferred", Reason, "RFD-2026-00001"), (refund.Amount, refund.Note, refund.ReasonId, refund.Number));
        Assert.Equal((0m, BalanceStatus.Paid), (account.BalanceFor(200m).Remaining, account.BalanceFor(200m).Status));
        var raised = Assert.IsType<RefundRecorded>(Assert.Single(account.DomainEvents));
        Assert.Equal(("patient transferred", 100m, "Cash"), (raised.Comment, raised.Amount, raised.Method));
    }

    [Fact]
    public void T3_Refund_refused_above_the_refundable_amount_BIL_011()
    {
        var account = Account();
        account.RecordPayment("RCT-2026-00001", 300m, PaymentMethod.Cash, null, Station(), Charge(300m), Now);

        var over = Assert.Throws<DomainException>(() => account.RecordRefund("RFD-1", 101m, PaymentMethod.Cash, null, Reason, null, Station(), Charge(200m), Now));
        Assert.Equal((BillingCodes.AmountExceedsRefundable, DomainFault.Conflict), (over.Code, over.Fault));

        var nothingDue = Assert.Throws<DomainException>(() => account.RecordRefund("RFD-1", 1m, PaymentMethod.Cash, null, Reason, null, Station(), Charge(300m), Now));
        Assert.Equal(BillingCodes.AmountExceedsRefundable, nothingDue.Code);
        Assert.Empty(account.Refunds);
    }

    [Fact]
    public void T3_Refund_needs_a_reason_BIL_004_and_a_reference_for_non_cash_BIL_003()
    {
        var account = Account();
        account.RecordPayment("RCT-2026-00001", 300m, PaymentMethod.Cash, null, Station(), Charge(300m), Now);

        Assert.Equal(BillingCodes.RefundReasonRequired, Assert.Throws<DomainException>(() => account.RecordRefund("RFD-1", 10m, PaymentMethod.Cash, null, Guid.Empty, null, Station(), Charge(200m), Now)).Code);
        Assert.Equal(BillingCodes.ReferenceRequired, Assert.Throws<DomainException>(() => account.RecordRefund("RFD-1", 10m, PaymentMethod.Card, null, Reason, null, Station(), Charge(200m), Now)).Code);
        Assert.Equal(BillingCodes.TreasuryNotApproved, Assert.Throws<DomainException>(() => account.RecordRefund("RFD-1", 10m, PaymentMethod.Cash, null, Reason, null, Station(approved: false), Charge(200m), Now)).Code);
        Assert.Empty(account.Refunds);
        Assert.Equal(0m, account.RefundedTotal);
    }

    [Fact]
    public void T3_Payments_and_refunds_follow_the_single_balance_definition()
    {
        var account = Account();
        account.RecordPayment("RCT-1", 300m, PaymentMethod.Cash, null, Station(), Charge(300m), Now);
        account.RecordRefund("RFD-1", 50m, PaymentMethod.Cash, null, Reason, null, Station(), Charge(250m), Now);

        // remaining = share − posted payments + refunds; a later re-pricing upwards makes the patient owe again
        var figures = account.BalanceFor(280m);
        Assert.Equal((280m, 300m, 50m, 30m, 0m, BalanceStatus.Partial), (figures.PatientShareTotal, figures.Paid, figures.Refunded, figures.Remaining, figures.Refundable, figures.Status));
        Assert.Throws<DomainException>(() => account.RecordPayment("RCT-2", 31m, PaymentMethod.Cash, null, Station(), Charge(280m), Now));
        account.RecordPayment("RCT-2", 30m, PaymentMethod.Cash, null, Station(), Charge(280m), Now);
        Assert.Equal(BalanceStatus.Paid, account.BalanceFor(280m).Status);
    }

    [Fact]
    public void A_visit_account_needs_a_visit()
    {
        Assert.Equal(BillingCodes.VisitNotFound, Assert.Throws<DomainException>(() => VisitAccount.Open(Guid.Empty)).Code);
        var visit = Guid.CreateVersion7();
        Assert.Equal(visit, VisitAccount.Open(visit).VisitId);
    }
}
