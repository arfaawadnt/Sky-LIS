using Platform.Application.Abstractions.Security;
using Platform.Application.Billing.Commands;
using Platform.Application.Billing.Policies;
using Platform.Application.Billing.Queries;
using Platform.Domain.Billing;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.UnitTests.Billing;

/// <summary>
/// Payment method definitions over in-memory ports (change request 2026-10-19, P-C10; intake LIS-INT-BIL-001 §17):
/// the set-up commands, the defaults, the provisioning policy, and how a movement takes its kind and reference rule
/// from the chosen definition while a request without a code keeps the legacy rule.
/// </summary>
public sealed class PaymentMethodUseCaseTests
{
    private static RecordPaymentHandler PaymentHandler(BillingWorld world) =>
        new(world.Visits, world.Methods, world.Accounts, world.Closings, world.Numbers, world.UnitOfWork, world.User, world.Privileges, world.Clock);

    private static RecordRefundHandler RefundHandler(BillingWorld world) =>
        new(world.Visits, world.Reasons, world.Methods, world.Accounts, world.Closings, world.Numbers, world.UnitOfWork, world.User, world.Privileges, world.Clock);

    [Fact]
    public async Task T10_Define_upper_cases_the_code_orders_after_the_last_option_and_refuses_a_duplicate_BIL_121()
    {
        var world = new BillingWorld();
        var handler = new CreatePaymentMethodHandler(world.Methods, world.UnitOfWork, world.Clock);

        var created = await handler.Handle(new CreatePaymentMethodCommand(" visa ", "Visa", "فيزا", "card", RequiresReference: true, Sequence: null), CancellationToken.None);
        var defined = Assert.Single(world.Methods.Items);
        Assert.Equal((created.Id, "VISA", PaymentMethod.Card, true, 1, true), (defined.Id, defined.Code, defined.Kind, defined.RequiresReference, defined.Sequence, defined.IsActive));
        Assert.Equal(1, world.UnitOfWork.Commits);

        var refusal = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new CreatePaymentMethodCommand("VISA", "Visa 2", "فيزا ٢", "Card", true, null), CancellationToken.None));
        Assert.Equal((BillingCodes.MethodCodeExists, DomainFault.Conflict), (refusal.Code, refusal.Fault));
        Assert.Single(world.Methods.Items);
    }

    [Fact]
    public async Task T14_Defaults_add_only_the_missing_codes_and_a_second_call_adds_nothing()
    {
        var world = new BillingWorld();
        world.Methods.Add(PaymentMethodDefinition.Define("CASH", new LocalizedText("Till", "الصندوق"), PaymentMethod.Cash, false, 1, BillingWorld.Now));
        var handler = new EnsureDefaultPaymentMethodsHandler(world.Methods, world.UnitOfWork, world.Clock);

        var first = await handler.Handle(new EnsureDefaultPaymentMethodsCommand(), CancellationToken.None);
        var second = await handler.Handle(new EnsureDefaultPaymentMethodsCommand(), CancellationToken.None);

        Assert.Equal((3, 0), (first.Created, second.Created));
        Assert.Equal(["CASH", "CARD", "POS", "TRANSFER"], world.Methods.Items.OrderBy(method => method.Sequence).Select(method => method.Code));
        Assert.Equal("Till", world.Methods.Items.Single(method => method.Code == "CASH").Name.En);   // the laboratory's own row is kept
        var options = await new ListPaymentMethodOptionsHandler(world.Methods).Handle(new ListPaymentMethodOptionsQuery(), CancellationToken.None);
        Assert.Equal(["CASH", "CARD", "POS", "TRANSFER"], options.Select(option => option.Code));
    }

    [Fact]
    public async Task T12_Deactivating_the_last_active_cash_method_is_refused_BIL_052_and_another_kind_is_not()
    {
        var world = new BillingWorld();
        world.Methods.Items.AddRange(PaymentMethodDefinition.Defaults(BillingWorld.Now));
        var cash = world.Methods.Items.Single(method => method.Code == "CASH");
        var card = world.Methods.Items.Single(method => method.Code == "CARD");
        var handler = new DeactivatePaymentMethodHandler(world.Methods, world.UnitOfWork, world.Clock);

        var refusal = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new DeactivatePaymentMethodCommand(cash.Id), CancellationToken.None));
        Assert.Equal(BillingCodes.LastActiveCashMethod, refusal.Code);
        Assert.True(cash.IsActive);

        await handler.Handle(new DeactivatePaymentMethodCommand(card.Id), CancellationToken.None);
        Assert.False(card.IsActive);

        world.Methods.Add(PaymentMethodDefinition.Define("TILL2", new LocalizedText("Second till", "صندوق ٢"), PaymentMethod.Cash, false, 9, BillingWorld.Now));
        await handler.Handle(new DeactivatePaymentMethodCommand(cash.Id), CancellationToken.None);   // another active cash method exists now
        Assert.False(cash.IsActive);

        var missing = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new DeactivatePaymentMethodCommand(Guid.CreateVersion7()), CancellationToken.None));
        Assert.Equal((BillingCodes.MethodNotFound, DomainFault.NotFound), (missing.Code, missing.Fault));
    }

    [Fact]
    public async Task T1_A_payment_with_a_method_code_takes_the_definition_s_kind_and_reference_rule()
    {
        var world = new BillingWorld();
        world.Methods.Add(PaymentMethodDefinition.Define("CARDNR", new LocalizedText("Card, no slip", "بطاقة بلا إيصال"), PaymentMethod.Card, requiresReference: false, 5, BillingWorld.Now));
        world.Methods.Add(PaymentMethodDefinition.Define("CASHREF", new LocalizedText("Cash with voucher", "نقدي بقسيمة"), PaymentMethod.Cash, requiresReference: true, 6, BillingWorld.Now));
        var visit = world.AddVisit(300m, "ACC-2026-00010");

        // a card definition without the reference rule: the request's kind "Cash" is ignored, the definition's Card is stored
        var noSlip = await PaymentHandler(world).Handle(new RecordPaymentCommand(visit.VisitId, 100m, "Cash", null, "cardnr"), CancellationToken.None);
        var payment = world.Accounts.Items.Single().Payments.Single(candidate => candidate.Id == noSlip.Id);
        Assert.Equal((PaymentMethod.Card, "CARDNR", null), (payment.Method, payment.MethodCode, payment.Reference));

        // a cash definition that demands a voucher number
        var refusal = await Assert.ThrowsAsync<DomainException>(() => PaymentHandler(world).Handle(new RecordPaymentCommand(visit.VisitId, 50m, "Cash", null, "CASHREF"), CancellationToken.None));
        Assert.Equal(BillingCodes.ReferenceRequired, refusal.Code);
        Assert.Equal(1, world.Numbers.Drawn);   // refused before a receipt number is drawn

        // an unknown or inactive code
        var unknown = await Assert.ThrowsAsync<DomainException>(() => PaymentHandler(world).Handle(new RecordPaymentCommand(visit.VisitId, 50m, "Cash", null, "WALLET"), CancellationToken.None));
        Assert.Equal((BillingCodes.MethodUnknownOrInactive, DomainFault.Validation), (unknown.Code, unknown.Fault));

        // a legacy request without a code keeps the kind rule: card needs a reference, cash does not
        var legacy = await Assert.ThrowsAsync<DomainException>(() => PaymentHandler(world).Handle(new RecordPaymentCommand(visit.VisitId, 50m, "Card", null), CancellationToken.None));
        Assert.Equal(BillingCodes.ReferenceRequired, legacy.Code);
        var cash = await PaymentHandler(world).Handle(new RecordPaymentCommand(visit.VisitId, 50m, "Cash", null), CancellationToken.None);
        Assert.Null(world.Accounts.Items.Single().Payments.Single(candidate => candidate.Id == cash.Id).MethodCode);
    }

    [Fact]
    public async Task T3_A_refund_with_a_method_code_stores_the_definition_s_code()
    {
        var world = new BillingWorld();
        world.Methods.Items.AddRange(PaymentMethodDefinition.Defaults(BillingWorld.Now));
        var visit = world.AddVisit(100m, "ACC-2026-00011");
        await PaymentHandler(world).Handle(new RecordPaymentCommand(visit.VisitId, 100m, "Cash", null, "CASH"), CancellationToken.None);
        world.Visits.Items[0] = visit with { PatientShareTotal = 60m };

        var refusal = await Assert.ThrowsAsync<DomainException>(() => RefundHandler(world).Handle(new RecordRefundCommand(visit.VisitId, 40m, "Cash", null, world.RefundReasonId, null, "TRANSFER"), CancellationToken.None));
        Assert.Equal(BillingCodes.ReferenceRequired, refusal.Code);

        var refunded = await RefundHandler(world).Handle(new RecordRefundCommand(visit.VisitId, 40m, "Cash", "TRX-1", world.RefundReasonId, null, "TRANSFER"), CancellationToken.None);
        var refund = world.Accounts.Items.Single().Refunds.Single(candidate => candidate.Id == refunded.Id);
        Assert.Equal((PaymentMethod.Transfer, "TRANSFER", "TRX-1"), (refund.Method, refund.MethodCode, refund.Reference));
    }

    [Fact]
    public async Task P2_A_provisioned_client_starts_with_the_defaults_idempotently_and_the_platform_tenant_takes_none()
    {
        var world = new BillingWorld();
        var setter = new RecordingTenantSetter();
        var policy = new SeedPaymentMethodsPolicy(world.Methods, setter, world.UnitOfWork, world.Clock);
        var tenant = Guid.CreateVersion7();
        var provisioned = new TenantProvisioned(tenant, "lab-one", "Manual", Guid.CreateVersion7(), BillingWorld.Now);

        await policy.HandleAsync(provisioned, CancellationToken.None);
        Assert.Equal(["CASH", "CARD", "POS", "TRANSFER"], world.Methods.Items.OrderBy(method => method.Sequence).Select(method => method.Code));
        Assert.Equal([tenant], setter.Scoped);   // the event carries no tenant: the policy scopes itself to the new one
        Assert.Equal(1, world.UnitOfWork.Commits);

        await policy.HandleAsync(provisioned, CancellationToken.None);   // the outbox retries: nothing doubles, nothing is committed
        Assert.Equal(4, world.Methods.Items.Count);
        Assert.Equal(1, world.UnitOfWork.Commits);

        await policy.HandleAsync(new TenantProvisioned(TenantIds.Platform, "platform", "Manual", Guid.CreateVersion7(), BillingWorld.Now), CancellationToken.None);
        Assert.Equal(4, world.Methods.Items.Count);
        Assert.Equal([tenant, tenant], setter.Scoped);   // once per client event (the retry reads the codes under the tenant); never for the platform tenant
    }

    private sealed class RecordingTenantSetter : ICurrentTenantSetter
    {
        public List<Guid> Scoped { get; } = [];

        public IDisposable ForTenant(Guid tenantId)
        {
            Scoped.Add(tenantId);
            return new Noop();
        }

        public IDisposable Elevate() => throw new InvalidOperationException("The policy never elevates.");

        private sealed class Noop : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
