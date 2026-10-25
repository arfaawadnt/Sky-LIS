using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Billing;
using Platform.Domain.Authorization;
using Platform.Domain.Billing;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.Billing.Commands;

// Cash entries of the cash ledger (SRS CP-70, change request LIS-CR-011 point C-15; intake LIS-INT-BIL-001 §18). Validators
// check shape; the aggregate decides (pitfall P-10). A refused entry is decided before a number is drawn (CheckPost).

/// <summary>
/// T6 Post cash entry: money out of the drawer (supplier, petty cash, bank deposit) or into it (opening float, other income).
/// Cashier and branch from the session; the method defaults to cash; refused inside a closed period of the cashier (BIL-061).
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.Create)]
public sealed record PostCashEntryCommand(string Kind, string Category, decimal Amount, string Reason, string? Method = null, string? MethodCode = null, string? Reference = null, string? Note = null) : ICommand<CashEntryCreated>;

public sealed class PostCashEntryValidator : AbstractValidator<PostCashEntryCommand>
{
    public PostCashEntryValidator()
    {
        RuleFor(command => command.Kind).NotEmpty().IsEnumName(typeof(CashEntryKind), caseSensitive: false);
        RuleFor(command => command.Category).NotEmpty().IsEnumName(typeof(CashEntryCategory), caseSensitive: false);
        RuleFor(command => command.Method).IsEnumName(typeof(PaymentMethod), caseSensitive: false).When(command => !string.IsNullOrEmpty(command.Method));
        RuleFor(command => command.MethodCode).MaximumLength(Payment.MaxMethodCodeLength);
        RuleFor(command => command.Reference).MaximumLength(Payment.MaxReferenceLength);
        RuleFor(command => command.Reason).MaximumLength(CashEntry.MaxReasonLength);
        RuleFor(command => command.Note).MaximumLength(CashEntry.MaxNoteLength);
    }
}

public sealed class PostCashEntryHandler(
    IPaymentMethodRepository methods,
    ICashClosingRepository closings,
    ICashEntryRepository entries,
    IReferenceNumbers numbers,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    IClock clock) : ICommandHandler<PostCashEntryCommand, CashEntryCreated>
{
    public async Task<CashEntryCreated> Handle(PostCashEntryCommand command, CancellationToken cancellationToken)
    {
        var actor = BillingActor.Of(user);
        var cashier = BillingCashier.Of(actor, user);
        var kind = Enum.Parse<CashEntryKind>(command.Kind, ignoreCase: true);
        var category = Enum.Parse<CashEntryCategory>(command.Category, ignoreCase: true);
        var method = await BillingMethods.ResolveAsync(methods, string.IsNullOrWhiteSpace(command.Method) ? nameof(PaymentMethod.Cash) : command.Method, command.MethodCode, cancellationToken);

        var now = clock.UtcNow;
        var closed = await closings.CoversAsync(cashier.BranchId, cashier.UserId, now, cancellationToken);
        CashEntry.CheckPost(kind, category, command.Amount, method, command.Reference, command.Reason, command.Note, cashier, closed);

        var number = await numbers.NextAsync(CashEntry.NumberKind, CashEntry.NumberPrefix, now, cancellationToken);
        var entry = CashEntry.Post(number, kind, category, command.Amount, method, command.Reference, command.Reason, command.Note, cashier, closed, now);
        entries.Add(entry);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CashEntryCreated(entry.Id, entry.Number);
    }
}

/// <summary>T7 Void cash entry: the reason travels in the body (pitfall P-16); frozen once a closing covers the entry (BIL-061).</summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.Approve)]
public sealed record VoidCashEntryCommand(Guid EntryId, string Reason) : ICommand;

public sealed class VoidCashEntryValidator : AbstractValidator<VoidCashEntryCommand>
{
    public VoidCashEntryValidator()
    {
        RuleFor(command => command.EntryId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(CashEntry.MaxVoidReasonLength);
    }
}

public sealed class VoidCashEntryHandler(ICashEntryRepository entries, ICashClosingRepository closings, IUnitOfWork unitOfWork, ICurrentUser user, IClock clock) : ICommandHandler<VoidCashEntryCommand>
{
    public async Task Handle(VoidCashEntryCommand command, CancellationToken cancellationToken)
    {
        var actor = BillingActor.Of(user);
        var entry = await entries.GetForUpdateAsync(command.EntryId, cancellationToken) ?? throw BillingNotFound.CashEntry();
        var frozen = await closings.CoversAsync(entry.BranchId, entry.CashierUserId, entry.PostedAtUtc, cancellationToken);

        entry.Void(actor, command.Reason, frozen, clock.UtcNow);   // state guard, frozen guard, reason, event
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
