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
/// T5 Close cash drawer (CP-30, BR-16, WF-09): the totals are computed from the movements at the moment of the close and
/// stored as the closing's snapshot; overlapping closed periods are refused first (BIL-120). The scope is the session branch
/// and one cashier (or every cashier of the branch with <paramref name="CashierUserId"/> null) — LIS-CR-004, 2026-10-20.
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Billing, PermissionAction.Manage)]
public sealed record CloseCashDrawerCommand(Guid? CashierUserId, DateTimeOffset PeriodFromUtc, DateTimeOffset PeriodToUtc, decimal? CountedCash, string? Note) : ICommand<CashClosingCreated>;

public sealed class CloseCashDrawerValidator : AbstractValidator<CloseCashDrawerCommand>
{
    public CloseCashDrawerValidator()
    {
        RuleFor(command => command.CashierUserId).NotEqual(Guid.Empty).When(command => command.CashierUserId is not null);
        RuleFor(command => command.PeriodFromUtc).NotEmpty();
        RuleFor(command => command.PeriodToUtc).NotEmpty();
        RuleFor(command => command.Note).MaximumLength(CashClosing.MaxNoteLength);
    }
}

public sealed class CloseCashDrawerHandler(
    ICashClosingRepository closings,
    ICashDrawerQueries drawer,
    IReferenceNumbers numbers,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    IClock clock) : ICommandHandler<CloseCashDrawerCommand, CashClosingCreated>
{
    public async Task<CashClosingCreated> Handle(CloseCashDrawerCommand command, CancellationToken cancellationToken)
    {
        var actor = BillingActor.Of(user);
        var branch = BranchScope.Require(user);
        var now = clock.UtcNow;
        CashClosing.RequireScope(branch, command.PeriodFromUtc, command.PeriodToUtc, now);

        // INV-BIL-005 decided here with its code; nothing else can close the same drawer twice.
        if (await closings.OverlapsAsync(branch, command.CashierUserId, command.PeriodFromUtc, command.PeriodToUtc, cancellationToken))
        {
            throw new DomainException(BillingCodes.ClosingOverlaps, "A closed period of this branch overlaps the requested period for this cashier.", DomainFault.Conflict);
        }

        var totals = await drawer.TotalsAsync(new DrawerScope(branch, command.CashierUserId, command.PeriodFromUtc, command.PeriodToUtc), cancellationToken);
        CashClosing.CheckClose(branch, command.PeriodFromUtc, command.PeriodToUtc, totals, command.CountedCash, command.Note, now);

        var number = await numbers.NextAsync(CashClosing.NumberKind, CashClosing.NumberPrefix, now, cancellationToken);
        var closing = CashClosing.Close(number, branch, command.CashierUserId, command.PeriodFromUtc, command.PeriodToUtc, totals, command.CountedCash, command.Note, actor, now);
        closings.Add(closing);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CashClosingCreated(closing.Id, closing.Number);
    }
}
