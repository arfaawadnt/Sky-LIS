using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.Organization;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Organization.Commands;

// Branches (SRS CP-36; transitions B1–B4 of intake LIS-INT-ORG-001). Each handler: load, call the aggregate, commit once.
// LIS-CR-008 C-04: create and update take the optional city / district / area (none clears them; GEO-020…022).

[RequirePermissionPolicy(PermissionCatalog.Organization, PermissionAction.Create)]
public sealed record CreateBranchCommand(string Code, string NameEn, string NameAr, Guid? CountryId, Guid? GovernorateId, string? Phone, string? Email, string Currency, Guid? CityId = null, Guid? DistrictId = null, Guid? AreaId = null) : ICommand<CreatedResource>;

public sealed class CreateBranchValidator : AbstractValidator<CreateBranchCommand>
{
    public CreateBranchValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(Branch.MaxCodeLength);
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.Phone).MaximumLength(Branch.MaxPhoneLength);
        RuleFor(command => command.Email).MaximumLength(Branch.MaxEmailLength);
        RuleFor(command => command.Currency).NotEmpty().Length(Branch.CurrencyLength);
    }
}

public sealed class CreateBranchHandler(IBranchRepository branches, IBranchGeography geography, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreateBranchCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateBranchCommand command, CancellationToken cancellationToken)
    {
        var code = Branch.CleanCode(command.Code);
        if (await branches.CodeExistsAsync(code, cancellationToken))
        {
            throw new DomainException(OrganizationCodes.BranchCodeExists, $"A branch with code '{code}' already exists.", DomainFault.Conflict);
        }

        var branch = Branch.Create(code, new LocalizedText(command.NameEn, command.NameAr), command.CountryId, command.GovernorateId, command.Phone, command.Email, command.Currency, clock.UtcNow);
        await OrganizationGuards.EnsureGovernorateInCountryAsync(geography, branch.CountryId, branch.GovernorateId, cancellationToken);
        branch.Locate(await OrganizationGuards.ResolveLocalityAsync(geography, branch.GovernorateId, command.CityId, command.DistrictId, command.AreaId, cancellationToken));
        branches.Add(branch);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(branch.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Organization, PermissionAction.Edit)]
public sealed record UpdateBranchCommand(Guid BranchId, string NameEn, string NameAr, Guid? CountryId, Guid? GovernorateId, string? Phone, string? Email, string Currency, Guid? CityId = null, Guid? DistrictId = null, Guid? AreaId = null) : ICommand;

public sealed class UpdateBranchValidator : AbstractValidator<UpdateBranchCommand>
{
    public UpdateBranchValidator()
    {
        RuleFor(command => command.BranchId).NotEmpty();
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.Phone).MaximumLength(Branch.MaxPhoneLength);
        RuleFor(command => command.Email).MaximumLength(Branch.MaxEmailLength);
        RuleFor(command => command.Currency).NotEmpty().Length(Branch.CurrencyLength);
    }
}

public sealed class UpdateBranchHandler(IBranchRepository branches, IBranchGeography geography, IUnitOfWork unitOfWork) : ICommandHandler<UpdateBranchCommand>
{
    public async Task Handle(UpdateBranchCommand command, CancellationToken cancellationToken)
    {
        var branch = await branches.GetForUpdateAsync(command.BranchId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.BranchNotFound, "Branch not found.", DomainFault.NotFound);

        branch.Update(new LocalizedText(command.NameEn, command.NameAr), command.CountryId, command.GovernorateId, command.Phone, command.Email, command.Currency);
        await OrganizationGuards.EnsureGovernorateInCountryAsync(geography, branch.CountryId, branch.GovernorateId, cancellationToken);
        branch.Locate(await OrganizationGuards.ResolveLocalityAsync(geography, branch.GovernorateId, command.CityId, command.DistrictId, command.AreaId, cancellationToken));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Organization, PermissionAction.Manage)]
public sealed record DeactivateBranchCommand(Guid BranchId) : ICommand;

public sealed class DeactivateBranchValidator : AbstractValidator<DeactivateBranchCommand>
{
    public DeactivateBranchValidator() => RuleFor(command => command.BranchId).NotEmpty();
}

public sealed class DeactivateBranchHandler(IBranchRepository branches, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<DeactivateBranchCommand>
{
    public async Task Handle(DeactivateBranchCommand command, CancellationToken cancellationToken)
    {
        var branch = await branches.GetForUpdateAsync(command.BranchId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.BranchNotFound, "Branch not found.", DomainFault.NotFound);

        // INV-ORG-002: a laboratory keeps one branch to work in.
        if (branch.IsActive && await branches.CountActiveAsync(cancellationToken) <= 1)
        {
            throw new DomainException(OrganizationCodes.LastActiveBranch, "The last active branch cannot be deactivated.", DomainFault.Conflict);
        }

        branch.Deactivate(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Organization, PermissionAction.Manage)]
public sealed record ReactivateBranchCommand(Guid BranchId) : ICommand;

public sealed class ReactivateBranchValidator : AbstractValidator<ReactivateBranchCommand>
{
    public ReactivateBranchValidator() => RuleFor(command => command.BranchId).NotEmpty();
}

public sealed class ReactivateBranchHandler(IBranchRepository branches, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<ReactivateBranchCommand>
{
    public async Task Handle(ReactivateBranchCommand command, CancellationToken cancellationToken)
    {
        var branch = await branches.GetForUpdateAsync(command.BranchId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.BranchNotFound, "Branch not found.", DomainFault.NotFound);

        branch.Reactivate(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
