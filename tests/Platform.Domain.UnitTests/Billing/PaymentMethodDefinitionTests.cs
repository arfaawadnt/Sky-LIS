using Platform.Domain.Billing;
using Platform.Domain.UnitTests.Support;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.UnitTests.Billing;

/// <summary>Payment method definitions (change request 2026-10-19, P-C10; intake LIS-INT-BIL-001 §17 T10–T13): one test per transition, refused precondition and invariant.</summary>
public sealed class PaymentMethodDefinitionTests
{
    private static readonly DateTimeOffset Now = FixedClock.Instant;

    private static LocalizedText Name(string en = "Visa", string ar = "فيزا") => new(en, ar);

    [Fact]
    public void T10_Define_cleans_the_code_starts_active_and_raises_PaymentMethodDefined()
    {
        var definition = PaymentMethodDefinition.Define(" visa-pos ", Name(), PaymentMethod.Card, requiresReference: true, sequence: 7, Now);

        Assert.Equal(("VISA-POS", PaymentMethod.Card, true, 7, true), (definition.Code, definition.Kind, definition.RequiresReference, definition.Sequence, definition.IsActive));
        Assert.Equal(("Visa", "فيزا"), (definition.Name.En, definition.Name.Ar));
        var defined = Assert.IsType<PaymentMethodDefined>(Assert.Single(definition.DomainEvents));
        Assert.Equal((definition.Id, "VISA-POS", "Card", Now), (defined.MethodId, defined.Code, defined.Kind, defined.OccurredAtUtc));
        Assert.Equal(new PaymentMethodUse(PaymentMethod.Card, "VISA-POS", true), definition.AsUse());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad code")]
    [InlineData("VISA/1")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU")]
    public void T10_refused_for_a_bad_code_BIL_051(string code)
    {
        var refusal = Assert.Throws<DomainException>(() => PaymentMethodDefinition.Define(code, Name(), PaymentMethod.Card, true, 1, Now));
        Assert.Equal((BillingCodes.MethodCodeInvalid, DomainFault.Validation), (refusal.Code, refusal.Fault));
    }

    [Fact]
    public void T10_refused_for_a_long_name_BIL_007_a_bad_sequence_BIL_054_and_an_unknown_kind_BIL_055()
    {
        Assert.Equal(BillingCodes.TextTooLong, Assert.Throws<DomainException>(() => PaymentMethodDefinition.Define("X", Name(new string('n', 101)), PaymentMethod.Card, true, 1, Now)).Code);
        Assert.Equal(BillingCodes.MethodSequenceInvalid, Assert.Throws<DomainException>(() => PaymentMethodDefinition.Define("X", Name(), PaymentMethod.Card, true, -1, Now)).Code);
        Assert.Equal(BillingCodes.MethodSequenceInvalid, Assert.Throws<DomainException>(() => PaymentMethodDefinition.Define("X", Name(), PaymentMethod.Card, true, 10_000, Now)).Code);
        Assert.Equal(BillingCodes.MethodKindInvalid, Assert.Throws<DomainException>(() => PaymentMethodDefinition.Define("X", Name(), (PaymentMethod)9, true, 1, Now)).Code);
    }

    [Fact]
    public void Defaults_are_CASH_CARD_POS_and_TRANSFER_in_order_and_only_cash_needs_no_reference()
    {
        var defaults = PaymentMethodDefinition.Defaults(Now);

        Assert.Equal(["CASH", "CARD", "POS", "TRANSFER"], defaults.Select(method => method.Code));
        Assert.Equal([1, 2, 3, 4], defaults.Select(method => method.Sequence));
        Assert.Equal([PaymentMethod.Cash, PaymentMethod.Card, PaymentMethod.Pos, PaymentMethod.Transfer], defaults.Select(method => method.Kind));
        Assert.Equal([false, true, true, true], defaults.Select(method => method.RequiresReference));
        Assert.Equal(("Bank transfer", "تحويل بنكي"), (defaults[3].Name.En, defaults[3].Name.Ar));
        Assert.All(defaults, method => Assert.True(method.IsActive && method.DomainEvents.Count == 1));
    }

    [Fact]
    public void T11_Update_changes_names_kind_rule_and_order_and_raises_once_only_when_something_changed()
    {
        var definition = PaymentMethodDefinition.Define("CARD", Name("Card", "بطاقة"), PaymentMethod.Card, true, 2, Now);
        definition.ClearDomainEvents();

        definition.Update(Name("Card", "بطاقة"), PaymentMethod.Card, true, 2, otherActiveCashExists: false, Now);
        Assert.Empty(definition.DomainEvents);   // nothing changed

        definition.Update(Name("Credit card", "بطاقة ائتمان"), PaymentMethod.Pos, false, 9, otherActiveCashExists: false, Now.AddMinutes(1));
        Assert.Equal(("Credit card", PaymentMethod.Pos, false, 9, "CARD"), (definition.Name.En, definition.Kind, definition.RequiresReference, definition.Sequence, definition.Code));
        var updated = Assert.IsType<PaymentMethodUpdated>(Assert.Single(definition.DomainEvents));
        Assert.Equal((definition.Id, "CARD"), (updated.MethodId, updated.Code));
    }

    [Fact]
    public void T11_Re_kinding_the_last_active_cash_method_is_refused_BIL_052_unless_another_exists()
    {
        var cash = PaymentMethodDefinition.Define("CASH", Name("Cash", "نقدي"), PaymentMethod.Cash, false, 1, Now);

        var refusal = Assert.Throws<DomainException>(() => cash.Update(Name("Cash", "نقدي"), PaymentMethod.Transfer, true, 1, otherActiveCashExists: false, Now));
        Assert.Equal((BillingCodes.LastActiveCashMethod, DomainFault.Conflict), (refusal.Code, refusal.Fault));
        Assert.Equal(PaymentMethod.Cash, cash.Kind);

        cash.Update(Name("Cash", "نقدي"), PaymentMethod.Transfer, true, 1, otherActiveCashExists: true, Now);
        Assert.Equal(PaymentMethod.Transfer, cash.Kind);
    }

    [Fact]
    public void T12_Deactivate_is_idempotent_keeps_the_last_cash_method_BIL_052_and_T13_Reactivate_restores()
    {
        var cash = PaymentMethodDefinition.Define("CASH", Name("Cash", "نقدي"), PaymentMethod.Cash, false, 1, Now);
        var card = PaymentMethodDefinition.Define("CARD", Name("Card", "بطاقة"), PaymentMethod.Card, true, 2, Now);
        cash.ClearDomainEvents();
        card.ClearDomainEvents();

        var refusal = Assert.Throws<DomainException>(() => cash.Deactivate(otherActiveCashExists: false, Now));
        Assert.Equal(BillingCodes.LastActiveCashMethod, refusal.Code);
        Assert.True(cash.IsActive);

        cash.Deactivate(otherActiveCashExists: true, Now);
        Assert.False(cash.IsActive);
        Assert.IsType<PaymentMethodDeactivated>(Assert.Single(cash.DomainEvents));
        cash.Deactivate(otherActiveCashExists: false, Now);   // already inactive: nothing to refuse, nothing raised
        Assert.Single(cash.DomainEvents);

        card.Deactivate(otherActiveCashExists: false, Now);   // not a cash method: the invariant does not apply
        Assert.False(card.IsActive);

        card.Reactivate(Now.AddMinutes(1));
        Assert.True(card.IsActive);
        Assert.IsType<PaymentMethodReactivated>(card.DomainEvents.Last());
        card.Reactivate(Now.AddMinutes(2));
        Assert.Equal(2, card.DomainEvents.Count);
    }

    [Fact]
    public void A_deactivated_cash_method_may_be_re_kinded_freely()
    {
        var cash = PaymentMethodDefinition.Define("CASH", Name("Cash", "نقدي"), PaymentMethod.Cash, false, 1, Now);
        cash.Deactivate(otherActiveCashExists: true, Now);

        cash.Update(Name("Wallet", "محفظة"), PaymentMethod.Transfer, true, 3, otherActiveCashExists: false, Now);

        Assert.Equal(PaymentMethod.Transfer, cash.Kind);
    }
}
