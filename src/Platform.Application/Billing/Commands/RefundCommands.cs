using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Billing;
using Platform.Domain.Authorization;
using Platform.Domain.Billing;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Billing.Commands;

/// <summary>
/// T3 Record refund (CP-24, BR-14; LDM <c>Return Money</c> §10.8): permission <c>billing.approve</c> and the CP-37 special
/// right <c>CanRefund</c> (BIL-041, intake Q1).
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.Approve)]
public sealed record RecordRefundCommand(Guid VisitId, decimal Amount, string Method, string? Reference, Guid ReasonId, string? Note, string? MethodCode = null) : ICommand<RefundCreated>;

public sealed class RecordRefundValidator : AbstractValidator<RecordRefundCommand>
{
    public RecordRefundValidator()
    {
        RuleFor(command => command.VisitId).NotEmpty();
        RuleFor(command => command.Method).NotEmpty().IsEnumName(typeof(PaymentMethod), caseSensitive: false);
        RuleFor(command => command.MethodCode).MaximumLength(Payment.MaxMethodCodeLength);
        RuleFor(command => command.Reference).MaximumLength(Payment.MaxReferenceLength);
        RuleFor(command => command.ReasonId).NotEmpty();
        RuleFor(command => command.Note).MaximumLength(Refund.MaxNoteLength);
    }
}

public sealed class RecordRefundHandler(
    IBillableVisitLookup visits,
    IRefundReasonLookup reasons,
    IPaymentMethodRepository methods,
    IVisitAccountRepository accounts,
    ICashClosingRepository closings,
    IReferenceNumbers numbers,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    IUserPrivileges privileges,
    IClock clock) : ICommandHandler<RecordRefundCommand, RefundCreated>
{
    public async Task<RefundCreated> Handle(RecordRefundCommand command, CancellationToken cancellationToken)
    {
        var actor = BillingActor.Of(user);
        BillingRights.RequireRefund(privileges);
        var cashier = BillingCashier.Of(actor, user);
        var visit = await visits.GetAsync(command.VisitId, cancellationToken) ?? throw BillingNotFound.Visit();
        var method = await BillingMethods.ResolveAsync(methods, command.Method, command.MethodCode, cancellationToken);
        if (command.ReasonId != Guid.Empty && await reasons.GetActiveAsync(command.ReasonId, cancellationToken) is null)
        {
            throw new DomainException(BillingCodes.RefundReasonInvalid, "Choose an active reason of kind Refund.", DomainFault.Validation);
        }

        var now = clock.UtcNow;
        ClosedPeriods.RequireOpen(await closings.CoversAsync(cashier.BranchId, cashier.UserId, now, cancellationToken));   // BIL-061 (LIS-CR-011)

        var existing = await accounts.GetForUpdateAsync(visit.VisitId, cancellationToken);
        var account = existing ?? VisitAccount.Open(visit.VisitId);
        account.CheckRefund(command.Amount, method, command.Reference, command.ReasonId, command.Note, cashier, visit.Charge);

        var number = await numbers.NextAsync(Refund.NumberKind, Refund.NumberPrefix, now, cancellationToken);
        var refund = account.RecordRefund(number, command.Amount, method, command.Reference, command.ReasonId, command.Note, cashier, visit.Charge, now);
        if (existing is null)
        {
            accounts.Add(account);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new RefundCreated(refund.Id, refund.Number);
    }
}
