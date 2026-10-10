using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Pricing;
using Platform.Domain.Authorization;
using Platform.Domain.Pricing;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Pricing.Commands;

// Contract pricing of a tier (C1–C6). The tier is the address (one pricing per tier, INV-PRC-007).

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.Edit)]
public sealed record SetContractFormulaCommand(Guid TierId, DateOnly EffectiveFrom, DateOnly? EffectiveTo, IReadOnlyList<PricingFormulaTermRequest> Terms) : ICommand;

public sealed class SetContractFormulaValidator : AbstractValidator<SetContractFormulaCommand>
{
    public SetContractFormulaValidator()
    {
        RuleFor(command => command.TierId).NotEmpty();
        RuleFor(command => command.EffectiveFrom).NotEmpty();
        RuleFor(command => command.Terms).NotNull().Must(terms => terms is { Count: >= 1 and <= ContractPricing.MaxFormulaTerms })
            .WithMessage($"A formula uses 1 to {ContractPricing.MaxFormulaTerms} base lists.");
        RuleForEach(command => command.Terms).ChildRules(term =>
        {
            term.RuleFor(item => item.BaseListId).NotEmpty();
            term.RuleFor(item => item.PatientFactor).InclusiveBetween(0m, PriceMath.MaxFactor).PrecisionScale(9, PriceMath.FactorScale, ignoreTrailingZeros: true);
            term.RuleFor(item => item.PayerFactor).InclusiveBetween(0m, PriceMath.MaxFactor).PrecisionScale(9, PriceMath.FactorScale, ignoreTrailingZeros: true);
        });
    }
}

/// <summary>C1: creates the tier's pricing on first use (upsert, INV-PRC-007); every referenced list must exist (PRC-038).</summary>
public sealed class SetContractFormulaHandler(IPayerLookup payers, IBasePriceListRepository lists, IContractPricingRepository contracts, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<SetContractFormulaCommand>
{
    public async Task Handle(SetContractFormulaCommand command, CancellationToken cancellationToken)
    {
        var tier = await payers.FindTierAsync(command.TierId, cancellationToken)
            ?? throw new DomainException(PricingCodes.TierNotFound, "Contract tier not found.", DomainFault.NotFound);

        var listIds = command.Terms.Select(term => term.BaseListId).Distinct().ToList();
        var existing = await lists.ExistingIdsAsync(listIds, cancellationToken);
        if (listIds.Any(id => !existing.Contains(id)))
        {
            throw new DomainException(PricingCodes.FormulaListUnknown, "A base list of the formula does not exist.", DomainFault.Validation);
        }

        var terms = command.Terms.Select(term => new PricingFormulaTerm(term.BaseListId, term.PatientFactor, term.PayerFactor)).ToList();
        var pricing = await contracts.GetForUpdateByTierAsync(tier.Id, cancellationToken);
        if (pricing is null)
        {
            contracts.Add(ContractPricing.Create(tier.PayerId, tier.Id, command.EffectiveFrom, command.EffectiveTo, terms, clock.UtcNow));
        }
        else
        {
            pricing.SetFormula(command.EffectiveFrom, command.EffectiveTo, terms, clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.Approve)]
public sealed record ApplyContractPricingCommand(Guid TierId) : ICommand<ContractApplyResult>;

public sealed class ApplyContractPricingValidator : AbstractValidator<ApplyContractPricingCommand>
{
    public ApplyContractPricingValidator() => RuleFor(command => command.TierId).NotEmpty();
}

/// <summary>C2 Apply (gate G1): recomputes the tier from its active lists; excluded prices are kept (BR-07).</summary>
public sealed class ApplyContractPricingHandler(IBasePriceListRepository lists, IContractPricingRepository contracts, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<ApplyContractPricingCommand, ContractApplyResult>
{
    public async Task<ContractApplyResult> Handle(ApplyContractPricingCommand command, CancellationToken cancellationToken)
    {
        var pricing = await ContractPricingLoader.RequireAsync(contracts, command.TierId, cancellationToken);
        var snapshots = await lists.SnapshotsAsync(pricing.Sources.Select(source => source.BasePriceListId).ToList(), cancellationToken);

        var outcome = pricing.Apply(snapshots, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new ContractApplyResult(outcome.Priced, outcome.Added, outcome.Updated, outcome.Removed, outcome.ExcludedKept);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.Edit)]
public sealed record NegotiateContractPriceCommand(Guid TierId, Guid ServiceId, decimal PatientShare, decimal PayerShare) : ICommand;

public sealed class NegotiateContractPriceValidator : AbstractValidator<NegotiateContractPriceCommand>
{
    public NegotiateContractPriceValidator()
    {
        RuleFor(command => command.TierId).NotEmpty();
        RuleFor(command => command.ServiceId).NotEmpty();
        RuleFor(command => command.PatientShare).InclusiveBetween(0m, PriceMath.MaxAmount).PrecisionScale(12, PriceMath.AmountScale, ignoreTrailingZeros: true);
        RuleFor(command => command.PayerShare).InclusiveBetween(0m, PriceMath.MaxAmount).PrecisionScale(12, PriceMath.AmountScale, ignoreTrailingZeros: true);
    }
}

/// <summary>C3: a negotiated price for a service the contract does not price yet must name a catalogue service (PRC-015).</summary>
public sealed class NegotiateContractPriceHandler(IContractPricingRepository contracts, IPricingServiceCatalog catalog, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<NegotiateContractPriceCommand>
{
    public async Task Handle(NegotiateContractPriceCommand command, CancellationToken cancellationToken)
    {
        var pricing = await ContractPricingLoader.RequireAsync(contracts, command.TierId, cancellationToken);
        if (pricing.FindPrice(command.ServiceId) is null)
        {
            await ServiceIds.RequireKnownAsync(catalog, [command.ServiceId], cancellationToken);
        }

        pricing.Negotiate(command.ServiceId, command.PatientShare, command.PayerShare, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.Edit)]
public sealed record ClearPriceExclusionCommand(Guid TierId, Guid ServiceId) : ICommand;

public sealed class ClearPriceExclusionValidator : AbstractValidator<ClearPriceExclusionCommand>
{
    public ClearPriceExclusionValidator()
    {
        RuleFor(command => command.TierId).NotEmpty();
        RuleFor(command => command.ServiceId).NotEmpty();
    }
}

/// <summary>C4: restores the service from the formula's active lists now (intake Q7).</summary>
public sealed class ClearPriceExclusionHandler(IBasePriceListRepository lists, IContractPricingRepository contracts, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<ClearPriceExclusionCommand>
{
    public async Task Handle(ClearPriceExclusionCommand command, CancellationToken cancellationToken)
    {
        var pricing = await ContractPricingLoader.RequireAsync(contracts, command.TierId, cancellationToken);
        var snapshots = await lists.SnapshotsAsync(pricing.Sources.Select(source => source.BasePriceListId).ToList(), cancellationToken);

        pricing.ClearExclusion(command.ServiceId, snapshots, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.Edit)]
public sealed record SetPriceAlertCommand(Guid TierId, Guid ServiceId, string? AlertCondition) : ICommand;

public sealed class SetPriceAlertValidator : AbstractValidator<SetPriceAlertCommand>
{
    public SetPriceAlertValidator()
    {
        RuleFor(command => command.TierId).NotEmpty();
        RuleFor(command => command.ServiceId).NotEmpty();
        RuleFor(command => command.AlertCondition).MaximumLength(ContractPricing.MaxAlertLength);
    }
}

public sealed class SetPriceAlertHandler(IContractPricingRepository contracts, IUnitOfWork unitOfWork) : ICommandHandler<SetPriceAlertCommand>
{
    public async Task Handle(SetPriceAlertCommand command, CancellationToken cancellationToken)
    {
        var pricing = await ContractPricingLoader.RequireAsync(contracts, command.TierId, cancellationToken);

        pricing.SetAlert(command.ServiceId, command.AlertCondition);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>C6 Copy from another tier (LDM "Client Copy"); <paramref name="Factor"/> defaults to 1.</summary>
[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.Edit)]
public sealed record CopyContractPricingCommand(Guid TierId, Guid SourceTierId, decimal? Factor) : ICommand;

public sealed class CopyContractPricingValidator : AbstractValidator<CopyContractPricingCommand>
{
    public CopyContractPricingValidator()
    {
        RuleFor(command => command.TierId).NotEmpty();
        RuleFor(command => command.SourceTierId).NotEmpty();
        RuleFor(command => command.Factor).GreaterThan(0m).LessThanOrEqualTo(PriceMath.MaxFactor).PrecisionScale(9, PriceMath.FactorScale, ignoreTrailingZeros: true).When(command => command.Factor is not null);
    }
}

public sealed class CopyContractPricingHandler(IPayerLookup payers, IContractPricingRepository contracts, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CopyContractPricingCommand>
{
    public async Task Handle(CopyContractPricingCommand command, CancellationToken cancellationToken)
    {
        if (command.TierId == command.SourceTierId)
        {
            throw new DomainException(PricingCodes.CopySameTier, "A tier cannot be copied onto itself.", DomainFault.Conflict);
        }

        var tier = await payers.FindTierAsync(command.TierId, cancellationToken)
            ?? throw new DomainException(PricingCodes.TierNotFound, "Contract tier not found.", DomainFault.NotFound);
        var source = await contracts.GetForUpdateByTierAsync(command.SourceTierId, cancellationToken)
            ?? throw new DomainException(PricingCodes.SourceNotConfigured, "The source tier has no contract pricing to copy.", DomainFault.Conflict);

        var factor = command.Factor ?? 1m;
        var target = await contracts.GetForUpdateByTierAsync(tier.Id, cancellationToken);
        if (target is null)
        {
            contracts.Add(ContractPricing.CopyOf(source, tier.PayerId, tier.Id, factor, clock.UtcNow));
        }
        else
        {
            target.CopyFrom(source, factor, clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

internal static class ContractPricingLoader
{
    public static async Task<ContractPricing> RequireAsync(IContractPricingRepository contracts, Guid tierId, CancellationToken cancellationToken) =>
        await contracts.GetForUpdateByTierAsync(tierId, cancellationToken)
        ?? throw new DomainException(PricingCodes.ContractPricingNotFound, "The tier has no contract pricing yet; set its formula first.", DomainFault.NotFound);
}
