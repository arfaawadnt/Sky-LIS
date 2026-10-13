using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Contracts.Pricing;
using Platform.Domain.Authorization;
using Platform.Domain.Pricing;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Pricing.Commands;

// Base price lists (L1–L6). Validators check shape; the aggregate decides (pitfall P-10).

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.Create)]
public sealed record CreateBasePriceListCommand(string Code, string NameEn, string NameAr, int Year) : ICommand<CreatedResource>;

public sealed class CreateBasePriceListValidator : AbstractValidator<CreateBasePriceListCommand>
{
    public CreateBasePriceListValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(BasePriceList.MaxCodeLength);
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.Year).InclusiveBetween(BasePriceList.MinYear, BasePriceList.MaxYear);
    }
}

public sealed class CreateBasePriceListHandler(IBasePriceListRepository lists, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreateBasePriceListCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateBasePriceListCommand command, CancellationToken cancellationToken)
    {
        var code = BasePriceList.CleanCode(command.Code);
        await BasePriceListUniqueness.RequireFreeCodeAsync(lists, code, cancellationToken);

        var list = BasePriceList.Create(code, new LocalizedText(command.NameEn, command.NameAr), command.Year, clock.UtcNow);
        lists.Add(list);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(list.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.Edit)]
public sealed record UpdateBasePriceListCommand(Guid ListId, string NameEn, string NameAr, int Year) : ICommand;

public sealed class UpdateBasePriceListValidator : AbstractValidator<UpdateBasePriceListCommand>
{
    public UpdateBasePriceListValidator()
    {
        RuleFor(command => command.ListId).NotEmpty();
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.Year).InclusiveBetween(BasePriceList.MinYear, BasePriceList.MaxYear);
    }
}

public sealed class UpdateBasePriceListHandler(IBasePriceListRepository lists, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<UpdateBasePriceListCommand>
{
    public async Task Handle(UpdateBasePriceListCommand command, CancellationToken cancellationToken)
    {
        var list = await lists.GetForUpdateAsync(command.ListId, cancellationToken)
            ?? throw new DomainException(PricingCodes.BaseListNotFound, "Base price list not found.", DomainFault.NotFound);

        list.Update(new LocalizedText(command.NameEn, command.NameAr), command.Year, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.Edit)]
public sealed record UpsertPriceLinesCommand(Guid ListId, IReadOnlyList<PriceLineRequest> Lines) : ICommand<UpsertPriceLinesResult>;

public sealed class UpsertPriceLinesValidator : AbstractValidator<UpsertPriceLinesCommand>
{
    public UpsertPriceLinesValidator()
    {
        RuleFor(command => command.ListId).NotEmpty();
        RuleFor(command => command.Lines).NotNull().Must(lines => lines is { Count: >= 1 and <= BasePriceList.MaxLinesPerUpsert })
            .WithMessage($"Send between 1 and {BasePriceList.MaxLinesPerUpsert} lines.");
        RuleForEach(command => command.Lines).ChildRules(line =>
        {
            line.RuleFor(item => item.ServiceId).NotEmpty();
            line.RuleFor(item => item.Price).InclusiveBetween(0m, PriceMath.MaxAmount).PrecisionScale(12, PriceMath.AmountScale, ignoreTrailingZeros: true);
        });
    }
}

/// <summary>L3: services the list does not hold yet must exist in the catalogue (PRC-015); prices are the aggregate's to check.</summary>
public sealed class UpsertPriceLinesHandler(IBasePriceListRepository lists, IPricingServiceCatalog catalog, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<UpsertPriceLinesCommand, UpsertPriceLinesResult>
{
    public async Task<UpsertPriceLinesResult> Handle(UpsertPriceLinesCommand command, CancellationToken cancellationToken)
    {
        var list = await lists.GetForUpdateAsync(command.ListId, cancellationToken)
            ?? throw new DomainException(PricingCodes.BaseListNotFound, "Base price list not found.", DomainFault.NotFound);

        var held = list.Lines.Select(line => line.ServiceId).ToHashSet();
        await ServiceIds.RequireKnownAsync(catalog, command.Lines.Select(line => line.ServiceId).Where(id => !held.Contains(id)).Distinct().ToList(), cancellationToken);

        var changes = list.UpsertLines(command.Lines.Select(line => new PriceLineInput(line.ServiceId, line.Price, line.IsActive)).ToList(), clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new UpsertPriceLinesResult(changes.Added, changes.Changed);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.Create)]
public sealed record CopyBasePriceListCommand(Guid SourceListId, string Code, string NameEn, string NameAr, int Year, string Operation, decimal Value, decimal RoundingStep) : ICommand<CreatedResource>;

public sealed class CopyBasePriceListValidator : AbstractValidator<CopyBasePriceListCommand>
{
    public CopyBasePriceListValidator()
    {
        RuleFor(command => command.SourceListId).NotEmpty();
        RuleFor(command => command.Code).NotEmpty().MaximumLength(BasePriceList.MaxCodeLength);
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.Year).InclusiveBetween(BasePriceList.MinYear, BasePriceList.MaxYear);
        RuleFor(command => command.Operation).NotEmpty().IsEnumName(typeof(CopyOperation), caseSensitive: false);
        RuleFor(command => command.RoundingStep).Must(step => PriceMath.RoundingSteps.Contains(step)).WithMessage("The rounding step must be 0.01, 0.25, 0.5 or 1.");
    }
}

public sealed class CopyBasePriceListHandler(IBasePriceListRepository lists, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CopyBasePriceListCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CopyBasePriceListCommand command, CancellationToken cancellationToken)
    {
        var source = await lists.GetForUpdateAsync(command.SourceListId, cancellationToken)
            ?? throw new DomainException(PricingCodes.BaseListNotFound, "Base price list not found.", DomainFault.NotFound);

        var code = BasePriceList.CleanCode(command.Code);
        await BasePriceListUniqueness.RequireFreeCodeAsync(lists, code, cancellationToken);

        var copy = BasePriceList.CopyFrom(
            source, code, new LocalizedText(command.NameEn, command.NameAr), command.Year,
            Enum.Parse<CopyOperation>(command.Operation, ignoreCase: true), command.Value, command.RoundingStep, clock.UtcNow);
        lists.Add(copy);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(copy.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.Manage)]
public sealed record ActivateBasePriceListCommand(Guid ListId) : ICommand;

public sealed class ActivateBasePriceListValidator : AbstractValidator<ActivateBasePriceListCommand>
{
    public ActivateBasePriceListValidator() => RuleFor(command => command.ListId).NotEmpty();
}

public sealed class ActivateBasePriceListHandler(IBasePriceListRepository lists, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<ActivateBasePriceListCommand>
{
    public async Task Handle(ActivateBasePriceListCommand command, CancellationToken cancellationToken)
    {
        var list = await lists.GetForUpdateAsync(command.ListId, cancellationToken)
            ?? throw new DomainException(PricingCodes.BaseListNotFound, "Base price list not found.", DomainFault.NotFound);

        list.Activate(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.Manage)]
public sealed record DeactivateBasePriceListCommand(Guid ListId) : ICommand;

public sealed class DeactivateBasePriceListValidator : AbstractValidator<DeactivateBasePriceListCommand>
{
    public DeactivateBasePriceListValidator() => RuleFor(command => command.ListId).NotEmpty();
}

public sealed class DeactivateBasePriceListHandler(IBasePriceListRepository lists, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<DeactivateBasePriceListCommand>
{
    public async Task Handle(DeactivateBasePriceListCommand command, CancellationToken cancellationToken)
    {
        var list = await lists.GetForUpdateAsync(command.ListId, cancellationToken)
            ?? throw new DomainException(PricingCodes.BaseListNotFound, "Base price list not found.", DomainFault.NotFound);

        list.Deactivate(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// L6 Apply a list (CP-33, <c>pricing.approve</c>, gate G1): re-prices every tier whose formula references the list, in
/// one transaction (intake Q11). The list must be active (PRC-031); tiers none of whose lists is active are skipped.
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.Approve)]
public sealed record ApplyBasePriceListCommand(Guid ListId) : ICommand<ApplyBasePriceListResult>;

public sealed class ApplyBasePriceListValidator : AbstractValidator<ApplyBasePriceListCommand>
{
    public ApplyBasePriceListValidator() => RuleFor(command => command.ListId).NotEmpty();
}

public sealed class ApplyBasePriceListHandler(IBasePriceListRepository lists, IContractPricingRepository contracts, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<ApplyBasePriceListCommand, ApplyBasePriceListResult>
{
    public async Task<ApplyBasePriceListResult> Handle(ApplyBasePriceListCommand command, CancellationToken cancellationToken)
    {
        var own = await lists.SnapshotsAsync([command.ListId], cancellationToken);
        if (!own.TryGetValue(command.ListId, out var list))
        {
            throw new DomainException(PricingCodes.BaseListNotFound, "Base price list not found.", DomainFault.NotFound);
        }

        if (!list.IsActive)
        {
            throw new DomainException(PricingCodes.NoActiveList, "Activate the list before applying it to the contracts.", DomainFault.Conflict);
        }

        var derived = await contracts.ListReferencingForUpdateAsync(command.ListId, cancellationToken);
        var snapshots = await lists.SnapshotsAsync(derived.SelectMany(pricing => pricing.Sources.Select(source => source.BasePriceListId)).Distinct().ToList(), cancellationToken);

        var now = clock.UtcNow;
        var (applied, changed) = (0, 0);
        foreach (var pricing in derived)
        {
            var outcome = pricing.Apply(snapshots, now);
            applied++;
            changed += outcome.Added + outcome.Updated + outcome.Removed;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new ApplyBasePriceListResult(applied, changed);
    }
}

/// <summary>INV-PRC-005 decided by the handler with its code; the unique index is the backstop.</summary>
internal static class BasePriceListUniqueness
{
    public static async Task RequireFreeCodeAsync(IBasePriceListRepository lists, string code, CancellationToken cancellationToken)
    {
        if (await lists.CodeExistsAsync(code, cancellationToken))
        {
            throw new DomainException(PricingCodes.ListCodeExists, $"A base price list with code '{code}' already exists.", DomainFault.Conflict);
        }
    }
}

/// <summary>PRC-015: every referenced service must be a catalogue service of the tenant.</summary>
internal static class ServiceIds
{
    public static async Task RequireKnownAsync(IPricingServiceCatalog catalog, IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken)
    {
        if (serviceIds.Count == 0)
        {
            return;
        }

        var known = await catalog.ExistingServiceIdsAsync(serviceIds, cancellationToken);
        var unknown = serviceIds.Where(id => !known.Contains(id)).ToList();
        if (unknown.Count > 0)
        {
            throw new DomainException(PricingCodes.ServiceUnknown, $"{unknown.Count} service(s) are not in the catalogue: {string.Join(", ", unknown.Take(5))}.", DomainFault.Validation);
        }
    }
}
