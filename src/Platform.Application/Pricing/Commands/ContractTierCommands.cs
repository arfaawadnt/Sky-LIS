using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.Pricing;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Pricing.Commands;

// Contract tiers of a payer (P5–P7): children of the Payer aggregate, changed through the root.

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.Create)]
public sealed record AddContractTierCommand(Guid PayerId, string NameEn, string NameAr) : ICommand<CreatedResource>;

public sealed class AddContractTierValidator : AbstractValidator<AddContractTierCommand>
{
    public AddContractTierValidator()
    {
        RuleFor(command => command.PayerId).NotEmpty();
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
    }
}

public sealed class AddContractTierHandler(IPayerRepository payers, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<AddContractTierCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(AddContractTierCommand command, CancellationToken cancellationToken)
    {
        var payer = await payers.GetForUpdateAsync(command.PayerId, cancellationToken)
            ?? throw new DomainException(PricingCodes.PayerNotFound, "Payer not found.", DomainFault.NotFound);

        var tier = payer.AddTier(new LocalizedText(command.NameEn, command.NameAr), clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(tier.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.Edit)]
public sealed record RenameContractTierCommand(Guid PayerId, Guid TierId, string NameEn, string NameAr) : ICommand;

public sealed class RenameContractTierValidator : AbstractValidator<RenameContractTierCommand>
{
    public RenameContractTierValidator()
    {
        RuleFor(command => command.PayerId).NotEmpty();
        RuleFor(command => command.TierId).NotEmpty();
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
    }
}

public sealed class RenameContractTierHandler(IPayerRepository payers, IUnitOfWork unitOfWork) : ICommandHandler<RenameContractTierCommand>
{
    public async Task Handle(RenameContractTierCommand command, CancellationToken cancellationToken)
    {
        var payer = await payers.GetForUpdateAsync(command.PayerId, cancellationToken)
            ?? throw new DomainException(PricingCodes.PayerNotFound, "Payer not found.", DomainFault.NotFound);

        payer.RenameTier(command.TierId, new LocalizedText(command.NameEn, command.NameAr));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.Manage)]
public sealed record DeactivateContractTierCommand(Guid PayerId, Guid TierId) : ICommand;

public sealed class DeactivateContractTierValidator : AbstractValidator<DeactivateContractTierCommand>
{
    public DeactivateContractTierValidator()
    {
        RuleFor(command => command.PayerId).NotEmpty();
        RuleFor(command => command.TierId).NotEmpty();
    }
}

public sealed class DeactivateContractTierHandler(IPayerRepository payers, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<DeactivateContractTierCommand>
{
    public async Task Handle(DeactivateContractTierCommand command, CancellationToken cancellationToken)
    {
        var payer = await payers.GetForUpdateAsync(command.PayerId, cancellationToken)
            ?? throw new DomainException(PricingCodes.PayerNotFound, "Payer not found.", DomainFault.NotFound);

        payer.DeactivateTier(command.TierId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Pricing, PermissionAction.Manage)]
public sealed record ReactivateContractTierCommand(Guid PayerId, Guid TierId) : ICommand;

public sealed class ReactivateContractTierValidator : AbstractValidator<ReactivateContractTierCommand>
{
    public ReactivateContractTierValidator()
    {
        RuleFor(command => command.PayerId).NotEmpty();
        RuleFor(command => command.TierId).NotEmpty();
    }
}

public sealed class ReactivateContractTierHandler(IPayerRepository payers, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<ReactivateContractTierCommand>
{
    public async Task Handle(ReactivateContractTierCommand command, CancellationToken cancellationToken)
    {
        var payer = await payers.GetForUpdateAsync(command.PayerId, cancellationToken)
            ?? throw new DomainException(PricingCodes.PayerNotFound, "Payer not found.", DomainFault.NotFound);

        payer.ReactivateTier(command.TierId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
