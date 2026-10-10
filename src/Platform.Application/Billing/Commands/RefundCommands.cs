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
public sealed record RecordRefundCommand(Guid VisitId, decimal Amount, string Method, string? Reference, Guid ReasonId, string? Note, Guid TreasuryId, string DeviceId) : ICommand<RefundCreated>;

public sealed class RecordRefundValidator : AbstractValidator<RecordRefundCommand>
{
    public RecordRefundValidator()
    {
        RuleFor(command => command.VisitId).NotEmpty();
        RuleFor(command => command.Method).NotEmpty().IsEnumName(typeof(PaymentMethod), caseSensitive: false);
        RuleFor(command => command.Reference).MaximumLength(Payment.MaxReferenceLength);
        RuleFor(command => command.ReasonId).NotEmpty();
        RuleFor(command => command.Note).MaximumLength(Refund.MaxNoteLength);
        RuleFor(command => command.TreasuryId).NotEmpty();
        RuleFor(command => command.DeviceId).NotEmpty().MaximumLength(CashierStation.MaxDeviceIdLength);
    }
}

public sealed class RecordRefundHandler(
    IBillableVisitLookup visits,
    ITreasuryStationLookup stations,
    IRefundReasonLookup reasons,
    IVisitAccountRepository accounts,
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
        var branch = BranchScope.Require(user);
        var visit = await visits.GetAsync(command.VisitId, cancellationToken) ?? throw BillingNotFound.Visit();
        var station = await BillingStations.ResolveAsync(stations, command.TreasuryId, actor, command.DeviceId, branch, cancellationToken);
        var method = BillingNames.Method(command.Method);
        if (command.ReasonId != Guid.Empty && await reasons.GetActiveAsync(command.ReasonId, cancellationToken) is null)
        {
            throw new DomainException(BillingCodes.RefundReasonInvalid, "Choose an active reason of kind Refund.", DomainFault.Validation);
        }

        var existing = await accounts.GetForUpdateAsync(visit.VisitId, cancellationToken);
        var account = existing ?? VisitAccount.Open(visit.VisitId);
        account.CheckRefund(command.Amount, method, command.Reference, command.ReasonId, command.Note, station, visit.Charge);

        var now = clock.UtcNow;
        var number = await numbers.NextAsync(Refund.NumberKind, Refund.NumberPrefix, now, cancellationToken);
        var refund = account.RecordRefund(number, command.Amount, method, command.Reference, command.ReasonId, command.Note, station, visit.Charge, now);
        if (existing is null)
        {
            accounts.Add(account);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new RefundCreated(refund.Id, refund.Number);
    }
}
