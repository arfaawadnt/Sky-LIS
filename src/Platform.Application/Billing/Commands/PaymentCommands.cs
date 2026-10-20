using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Billing;
using Platform.Domain.Authorization;
using Platform.Domain.Billing;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.Billing.Commands;

// Payments (intake LIS-INT-BIL-001 T1, T2, T4). Validators check shape; the aggregate decides (pitfall P-10). A refused
// movement is decided before a receipt number is drawn (CheckPayment), so refusals leave no gaps in the sequence.

/// <summary>T1 Record payment (CP-23; BR-26: the registration page calls it right after saving the visit); cashier and branch from the session.</summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.Create)]
public sealed record RecordPaymentCommand(Guid VisitId, decimal Amount, string Method, string? Reference, string? MethodCode = null) : ICommand<PaymentCreated>;

public sealed class RecordPaymentValidator : AbstractValidator<RecordPaymentCommand>
{
    public RecordPaymentValidator()
    {
        RuleFor(command => command.VisitId).NotEmpty();
        RuleFor(command => command.Method).NotEmpty().IsEnumName(typeof(PaymentMethod), caseSensitive: false);
        RuleFor(command => command.MethodCode).MaximumLength(Payment.MaxMethodCodeLength);
        RuleFor(command => command.Reference).MaximumLength(Payment.MaxReferenceLength);
    }
}

public sealed class RecordPaymentHandler(
    IBillableVisitLookup visits,
    IPaymentMethodRepository methods,
    IVisitAccountRepository accounts,
    IReferenceNumbers numbers,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    IUserPrivileges privileges,
    IClock clock) : ICommandHandler<RecordPaymentCommand, PaymentCreated>
{
    public async Task<PaymentCreated> Handle(RecordPaymentCommand command, CancellationToken cancellationToken)
    {
        var actor = BillingActor.Of(user);
        BillingRights.RequirePay(privileges);
        var cashier = BillingCashier.Of(actor, user);
        var visit = await visits.GetAsync(command.VisitId, cancellationToken) ?? throw BillingNotFound.Visit();
        var method = await BillingMethods.ResolveAsync(methods, command.Method, command.MethodCode, cancellationToken);

        var existing = await accounts.GetForUpdateAsync(visit.VisitId, cancellationToken);
        var account = existing ?? VisitAccount.Open(visit.VisitId);
        account.CheckPayment(command.Amount, method, command.Reference, cashier, visit.Charge);

        var now = clock.UtcNow;
        var number = await numbers.NextAsync(Payment.NumberKind, Payment.NumberPrefix, now, cancellationToken);
        var payment = account.RecordPayment(number, command.Amount, method, command.Reference, cashier, visit.Charge, now);
        if (existing is null)
        {
            accounts.Add(account);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new PaymentCreated(payment.Id, payment.ReceiptNumber);
    }
}

/// <summary>T2 Void payment: the reason travels in the body (pitfall P-16); frozen once a closing covers the payment.</summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.Cancel)]
public sealed record VoidPaymentCommand(Guid PaymentId, string Reason) : ICommand;

public sealed class VoidPaymentValidator : AbstractValidator<VoidPaymentCommand>
{
    public VoidPaymentValidator()
    {
        RuleFor(command => command.PaymentId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(Payment.MaxReasonLength);
    }
}

public sealed class VoidPaymentHandler(IVisitAccountRepository accounts, ICashClosingRepository closings, IUnitOfWork unitOfWork, ICurrentUser user, IUserPrivileges privileges, IClock clock) : ICommandHandler<VoidPaymentCommand>
{
    public async Task Handle(VoidPaymentCommand command, CancellationToken cancellationToken)
    {
        var actor = BillingActor.Of(user);
        BillingRights.RequireCancel(privileges);
        var account = await accounts.GetByPaymentIdForUpdateAsync(command.PaymentId, cancellationToken) ?? throw BillingNotFound.Payment();
        var payment = account.Payments.First(candidate => candidate.Id == command.PaymentId);
        var frozen = await closings.CoversAsync(payment.BranchId, payment.CashierUserId, payment.PaidAtUtc, cancellationToken);

        account.VoidPayment(command.PaymentId, actor, command.Reason, frozen, clock.UtcNow);   // state guard, frozen guard, reason, event
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>T4 Pay all (CP-27): the amount is distributed across the members in accession order (one receipt per member).</summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.Create)]
public sealed record PayBatchCommand(Guid BatchId, decimal Amount, string Method, string? Reference, string? MethodCode = null) : ICommand<BatchPaymentResult>;

public sealed class PayBatchValidator : AbstractValidator<PayBatchCommand>
{
    public PayBatchValidator()
    {
        RuleFor(command => command.BatchId).NotEmpty();
        RuleFor(command => command.Method).NotEmpty().IsEnumName(typeof(PaymentMethod), caseSensitive: false);
        RuleFor(command => command.MethodCode).MaximumLength(Payment.MaxMethodCodeLength);
        RuleFor(command => command.Reference).MaximumLength(Payment.MaxReferenceLength);
    }
}

public sealed class PayBatchHandler(
    IBillableVisitLookup visits,
    IPaymentMethodRepository methods,
    IVisitAccountRepository accounts,
    IReferenceNumbers numbers,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    IUserPrivileges privileges,
    IClock clock) : ICommandHandler<PayBatchCommand, BatchPaymentResult>
{
    public async Task<BatchPaymentResult> Handle(PayBatchCommand command, CancellationToken cancellationToken)
    {
        var actor = BillingActor.Of(user);
        BillingRights.RequirePay(privileges);
        var cashier = BillingCashier.Of(actor, user);
        var batch = await visits.GetBatchAsync(command.BatchId, cancellationToken) ?? throw BillingNotFound.Batch();
        var method = await BillingMethods.ResolveAsync(methods, command.Method, command.MethodCode, cancellationToken);

        var members = await visits.BatchMembersAsync(batch.BatchId, cancellationToken);
        var existing = await accounts.GetManyForUpdateAsync(members.Select(member => member.VisitId).ToList(), cancellationToken);
        var dues = members
            .Select(member => new BatchMemberDue(member.VisitId, existing.TryGetValue(member.VisitId, out var account) ? account.BalanceFor(member.PatientShareTotal).Remaining : member.PatientShareTotal))
            .ToList();
        var shares = BatchAllocation.Distribute(command.Amount, dues, batch.BilledThroughClaims);

        var byVisit = members.ToDictionary(member => member.VisitId);
        var plan = shares.Select(share =>
        {
            var member = byVisit[share.VisitId];
            var isNew = !existing.TryGetValue(share.VisitId, out var account);
            account ??= VisitAccount.Open(share.VisitId);
            account.CheckPayment(share.Amount, method, command.Reference, cashier, member.Charge);
            return (Share: share, Member: member, Account: account, IsNew: isNew);
        }).ToList();

        var now = clock.UtcNow;
        var payments = new List<BatchMemberPayment>(plan.Count);
        foreach (var (share, member, account, isNew) in plan)
        {
            var number = await numbers.NextAsync(Payment.NumberKind, Payment.NumberPrefix, now, cancellationToken);
            var payment = account.RecordPayment(number, share.Amount, method, command.Reference, cashier, member.Charge, now, batch.BatchId);
            if (isNew)
            {
                accounts.Add(account);
            }

            payments.Add(new BatchMemberPayment(member.VisitId, member.Accession, payment.Id, payment.ReceiptNumber, share.Amount));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new BatchPaymentResult(batch.BatchId, command.Amount, payments);
    }
}
