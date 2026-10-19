using Platform.Domain.Billing;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Billing;

/// <summary>
/// T1 and T3 with a payment method definition (change request 2026-10-19, P-C10): the reference rule is the
/// definition's, the movement stores the definition's kind and code, and a movement without a definition keeps the
/// legacy rule "every kind but cash needs a reference" (BR-26, BIL-003).
/// </summary>
public sealed class VisitAccountMethodTests
{
    private static readonly DateTimeOffset Now = FixedClock.Instant;
    private static readonly Guid Cashier = Guid.CreateVersion7();
    private static readonly Guid Branch = Guid.CreateVersion7();
    private static readonly Guid Treasury = Guid.CreateVersion7();
    private static readonly Guid Reason = Guid.CreateVersion7();

    private static CashierStation Station() => new(Treasury, Branch, true, true, "device-1", Cashier, Branch);

    private static VisitCharge Charge(decimal share) => new(share, false);

    private static VisitAccount Account() => VisitAccount.Open(Guid.CreateVersion7());

    [Fact]
    public void Legacy_use_requires_a_reference_for_every_kind_but_cash()
    {
        Assert.Equal(new PaymentMethodUse(PaymentMethod.Cash, null, false), PaymentMethodUse.Legacy(PaymentMethod.Cash));
        Assert.Equal(new PaymentMethodUse(PaymentMethod.Card, null, true), PaymentMethodUse.Legacy(PaymentMethod.Card));
        Assert.Equal(new PaymentMethodUse(PaymentMethod.Pos, null, true), PaymentMethodUse.Legacy(PaymentMethod.Pos));
        Assert.Equal(new PaymentMethodUse(PaymentMethod.Transfer, null, true), PaymentMethodUse.Legacy(PaymentMethod.Transfer));
    }

    [Fact]
    public void T1_A_card_definition_without_the_reference_rule_accepts_a_payment_without_reference_and_stores_its_code()
    {
        var account = Account();
        var method = new PaymentMethodUse(PaymentMethod.Card, "CARDNR", RequiresReference: false);

        var payment = account.RecordPayment("RCT-2026-00001", 100m, method, " ", Station(), Charge(300m), Now);

        Assert.Equal((PaymentMethod.Card, "CARDNR", null), (payment.Method, payment.MethodCode, payment.Reference));
        var recorded = Assert.IsType<PaymentRecorded>(Assert.Single(account.DomainEvents));
        Assert.Equal("Card", recorded.Method);
    }

    [Fact]
    public void T1_A_cash_definition_with_the_reference_rule_refuses_a_payment_without_reference_BIL_003()
    {
        var account = Account();
        var method = new PaymentMethodUse(PaymentMethod.Cash, "CASHREF", RequiresReference: true);

        var refusal = Assert.Throws<DomainException>(() => account.RecordPayment("RCT-2026-00001", 100m, method, null, Station(), Charge(300m), Now));

        Assert.Equal((BillingCodes.ReferenceRequired, DomainFault.Validation), (refusal.Code, refusal.Fault));
        Assert.Empty(account.Payments);
        Assert.Equal(0m, account.PaidTotal);

        var payment = account.RecordPayment("RCT-2026-00001", 100m, method, " V-12 ", Station(), Charge(300m), Now);
        Assert.Equal((PaymentMethod.Cash, "CASHREF", "V-12"), (payment.Method, payment.MethodCode, payment.Reference));
    }

    [Fact]
    public void T1_CheckPayment_with_a_definition_refuses_before_anything_is_recorded()
    {
        var account = Account();

        var refusal = Assert.Throws<DomainException>(() => account.CheckPayment(100m, new PaymentMethodUse(PaymentMethod.Transfer, "TRANSFER", true), "", Station(), Charge(300m)));

        Assert.Equal(BillingCodes.ReferenceRequired, refusal.Code);
        account.CheckPayment(100m, new PaymentMethodUse(PaymentMethod.Transfer, "TRANSFER", true), "TRX-1", Station(), Charge(300m));
        Assert.Empty(account.Payments);
    }

    [Fact]
    public void T1_The_legacy_overload_still_records_a_payment_without_a_code()
    {
        var payment = Account().RecordPayment("RCT-2026-00001", 10m, PaymentMethod.Cash, null, Station(), Charge(100m), Now);

        Assert.Equal((PaymentMethod.Cash, null), (payment.Method, payment.MethodCode));
    }

    [Fact]
    public void T3_A_refund_with_a_definition_applies_its_reference_rule_and_stores_its_code()
    {
        var account = Account();
        account.RecordPayment("RCT-2026-00001", 100m, PaymentMethod.Cash, null, Station(), Charge(100m), Now);
        var transfer = new PaymentMethodUse(PaymentMethod.Transfer, "TRANSFER", RequiresReference: true);

        Assert.Equal(BillingCodes.ReferenceRequired, Assert.Throws<DomainException>(() => account.CheckRefund(40m, transfer, null, Reason, null, Station(), Charge(60m))).Code);
        Assert.Equal(BillingCodes.ReferenceRequired, Assert.Throws<DomainException>(() => account.RecordRefund("RFD-1", 40m, transfer, null, Reason, null, Station(), Charge(60m), Now)).Code);
        Assert.Empty(account.Refunds);

        var refund = account.RecordRefund("RFD-1", 40m, transfer, "TRX-9", Reason, null, Station(), Charge(60m), Now);
        Assert.Equal((PaymentMethod.Transfer, "TRANSFER", "TRX-9", 40m), (refund.Method, refund.MethodCode, refund.Reference, account.RefundedTotal));
        Assert.Equal("Transfer", Assert.IsType<RefundRecorded>(account.DomainEvents.Last()).Method);

        var noReference = new PaymentMethodUse(PaymentMethod.Card, "CARDNR", RequiresReference: false);
        var second = account.RecordRefund("RFD-2", 20m, noReference, null, Reason, null, Station(), Charge(40m), Now);
        Assert.Equal((PaymentMethod.Card, "CARDNR", null), (second.Method, second.MethodCode, second.Reference));
    }
}
