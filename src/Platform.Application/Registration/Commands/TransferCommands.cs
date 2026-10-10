using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Catalogue;
using Platform.Application.Pricing;
using Platform.Domain.Authorization;
using Platform.Domain.Catalogue;
using Platform.Domain.Registration;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Registration.Commands;

// Money transfer (CP-25, WF-09 step 3): V7 contract transfer and V8 line share adjustment. Both need the Transfer
// special right besides registration.approve (LDM §7.2 "sensitive rights narrowly granted").

[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.Approve)]
public sealed record TransferVisitContractCommand(Guid VisitId, Guid PayerId, Guid ContractTierId, Guid? ReasonId, string? Reason) : ICommand;

public sealed class TransferVisitContractValidator : AbstractValidator<TransferVisitContractCommand>
{
    public TransferVisitContractValidator()
    {
        RuleFor(command => command.VisitId).NotEmpty();
        RuleFor(command => command.PayerId).NotEmpty();
        RuleFor(command => command.ContractTierId).NotEmpty();
        RuleFor(command => command.Reason).MaximumLength(Visit.MaxReasonLength);
        RuleFor(command => command).Must(command => command.ReasonId is not null || !string.IsNullOrWhiteSpace(command.Reason)).WithName("reason").WithMessage("Choose a transfer reason or type one.");
    }
}

/// <summary>V7: Transfer right (REG-062); new contract valid; every live service priced by it (BR-06); logged with reason.</summary>
public sealed class TransferVisitContractHandler(
    IVisitRepository visits,
    IPayerLookup payers,
    IPriceLookup prices,
    IOrderRulesLookup rules,
    IUserPrivileges privileges,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    IClock clock) : ICommandHandler<TransferVisitContractCommand>
{
    public async Task Handle(TransferVisitContractCommand command, CancellationToken cancellationToken)
    {
        var actor = RegistrationActor.Of(user);
        TransferRights.Require(privileges);
        var visit = await visits.GetForUpdateAsync(command.VisitId, cancellationToken)
            ?? throw new DomainException(RegistrationCodes.VisitNotFound, "Visit not found.", DomainFault.NotFound);

        await RegistrationRules.RequireContractAsync(payers, command.PayerId, command.ContractTierId, cancellationToken);
        var reason = await RegistrationRules.ReasonTextAsync(rules, command.ReasonId, command.Reason, ReasonKind.Transfer, RegistrationCodes.TransferReasonRequired, cancellationToken);
        var priced = await TransferRights.PricesAsync(prices, visit, command.PayerId, command.ContractTierId, cancellationToken);

        visit.TransferContract(command.PayerId, command.ContractTierId, priced, command.ReasonId, reason, actor, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Registration, PermissionAction.Approve)]
public sealed record AdjustVisitLineSharesCommand(Guid VisitId, Guid LineId, decimal PatientShare, decimal PayerShare, Guid? ReasonId, string? Reason) : ICommand;

public sealed class AdjustVisitLineSharesValidator : AbstractValidator<AdjustVisitLineSharesCommand>
{
    public AdjustVisitLineSharesValidator()
    {
        RuleFor(command => command.VisitId).NotEmpty();
        RuleFor(command => command.LineId).NotEmpty();
        RuleFor(command => command.PatientShare).GreaterThanOrEqualTo(0).LessThanOrEqualTo(99_999_999m);
        RuleFor(command => command.PayerShare).GreaterThanOrEqualTo(0).LessThanOrEqualTo(99_999_999m);
        RuleFor(command => command.Reason).MaximumLength(Visit.MaxReasonLength);
        RuleFor(command => command).Must(command => command.ReasonId is not null || !string.IsNullOrWhiteSpace(command.Reason)).WithName("reason").WithMessage("Choose a transfer reason or type one.");
    }
}

/// <summary>V8: Transfer right; the line's total is unchanged (INV-REG-009); logged with reason.</summary>
public sealed class AdjustVisitLineSharesHandler(
    IVisitRepository visits,
    IOrderRulesLookup rules,
    IUserPrivileges privileges,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    IClock clock) : ICommandHandler<AdjustVisitLineSharesCommand>
{
    public async Task Handle(AdjustVisitLineSharesCommand command, CancellationToken cancellationToken)
    {
        var actor = RegistrationActor.Of(user);
        TransferRights.Require(privileges);
        var visit = await visits.GetForUpdateAsync(command.VisitId, cancellationToken)
            ?? throw new DomainException(RegistrationCodes.VisitNotFound, "Visit not found.", DomainFault.NotFound);
        var reason = await RegistrationRules.ReasonTextAsync(rules, command.ReasonId, command.Reason, ReasonKind.Transfer, RegistrationCodes.TransferReasonRequired, cancellationToken);

        visit.AdjustLineShares(command.LineId, command.PatientShare, command.PayerShare, command.ReasonId, reason, actor, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>The Transfer special right and the new contract's prices of a visit's live services, shared by the transfer and its preview.</summary>
internal static class TransferRights
{
    public static void Require(IUserPrivileges privileges)
    {
        if (!privileges.SpecialRights.CanTransfer)
        {
            throw new DomainException(RegistrationCodes.TransferRightRequired, "Money transfers need the Transfer special right.", DomainFault.Refused);
        }
    }

    /// <summary>The priced services of the visit's live top-level lines under the contract (unpriced ones are absent).</summary>
    public static async Task<IReadOnlyDictionary<Guid, LinePrice>> PricesAsync(IPriceLookup prices, Visit visit, Guid payerId, Guid tierId, CancellationToken cancellationToken)
    {
        var serviceIds = visit.Lines.Where(line => line.IsLive && line.IsTopLevel).Select(line => line.ServiceId).Distinct().ToList();
        if (serviceIds.Count == 0)
        {
            return new Dictionary<Guid, LinePrice>();
        }

        var quote = await prices.QuoteAsync(payerId, tierId, serviceIds, cancellationToken);
        return quote.Where(line => line.Priced).GroupBy(line => line.ServiceId).ToDictionary(group => group.Key, group => new LinePrice(group.First().ListPrice, group.First().PatientShare, group.First().PayerShare));
    }
}
