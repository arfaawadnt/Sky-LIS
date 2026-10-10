using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.Catalogue;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;

namespace Platform.Application.Catalogue.Commands;

// Microbiology masters of Catalogue B (CP-56, SA-14): drug types, commercial names, antibiotics, organisms and
// sensitivity values, T1/T2 of intake LIS-INT-CTB-001. Each handler: check, load or create, call the aggregate, commit once.

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Create)]
public sealed record CreateDrugTypeCommand(string NameEn, string NameAr) : ICommand<CreatedResource>;

public sealed class CreateDrugTypeValidator : AbstractValidator<CreateDrugTypeCommand>
{
    public CreateDrugTypeValidator()
    {
        RuleFor(command => command.NameEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.NameAr).RequiredText(CatalogueBGuard.MaxNameLength);
    }
}

public sealed class CreateDrugTypeHandler(IDrugTypeRepository drugTypes, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreateDrugTypeCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateDrugTypeCommand command, CancellationToken cancellationToken)
    {
        var name = new LocalizedText(command.NameEn, command.NameAr);
        if (await drugTypes.NameExistsAsync(name.En, null, cancellationToken))
        {
            throw CatalogueBRules.NameExists(name.En);
        }

        var drugType = DrugType.Create(name, clock.UtcNow);
        drugTypes.Add(drugType);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(drugType.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record UpdateDrugTypeCommand(Guid DrugTypeId, string NameEn, string NameAr) : ICommand;

public sealed class UpdateDrugTypeValidator : AbstractValidator<UpdateDrugTypeCommand>
{
    public UpdateDrugTypeValidator()
    {
        RuleFor(command => command.DrugTypeId).NotEmpty();
        RuleFor(command => command.NameEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.NameAr).RequiredText(CatalogueBGuard.MaxNameLength);
    }
}

public sealed class UpdateDrugTypeHandler(IDrugTypeRepository drugTypes, IUnitOfWork unitOfWork) : ICommandHandler<UpdateDrugTypeCommand>
{
    public async Task Handle(UpdateDrugTypeCommand command, CancellationToken cancellationToken)
    {
        var drugType = await drugTypes.GetForUpdateAsync(command.DrugTypeId, cancellationToken)
            ?? throw CatalogueBRules.NotFound(CatalogueBCodes.DrugTypeNotFound, "Drug type");

        var name = new LocalizedText(command.NameEn, command.NameAr);
        if (await drugTypes.NameExistsAsync(name.En, drugType.Id, cancellationToken))
        {
            throw CatalogueBRules.NameExists(name.En);
        }

        drugType.Rename(name);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Create)]
public sealed record CreateCommercialNameCommand(Guid DrugTypeId, string NameEn, string NameAr) : ICommand<CreatedResource>;

public sealed class CreateCommercialNameValidator : AbstractValidator<CreateCommercialNameCommand>
{
    public CreateCommercialNameValidator()
    {
        RuleFor(command => command.DrugTypeId).NotEmpty();
        RuleFor(command => command.NameEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.NameAr).RequiredText(CatalogueBGuard.MaxNameLength);
    }
}

public sealed class CreateCommercialNameHandler(ICommercialNameRepository commercialNames, IDrugTypeRepository drugTypes, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreateCommercialNameCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateCommercialNameCommand command, CancellationToken cancellationToken)
    {
        var name = new LocalizedText(command.NameEn, command.NameAr);
        if (await commercialNames.NameExistsAsync(name.En, null, cancellationToken))
        {
            throw CatalogueBRules.NameExists(name.En);
        }

        var drugType = await drugTypes.GetForUpdateAsync(command.DrugTypeId, cancellationToken)
            ?? throw CatalogueBRules.Unknown(CatalogueBCodes.DrugTypeUnknown, "The drug type does not exist.");

        var commercialName = CommercialName.Create(drugType, name, clock.UtcNow);
        commercialNames.Add(commercialName);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(commercialName.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record UpdateCommercialNameCommand(Guid CommercialNameId, Guid DrugTypeId, string NameEn, string NameAr) : ICommand;

public sealed class UpdateCommercialNameValidator : AbstractValidator<UpdateCommercialNameCommand>
{
    public UpdateCommercialNameValidator()
    {
        RuleFor(command => command.CommercialNameId).NotEmpty();
        RuleFor(command => command.DrugTypeId).NotEmpty();
        RuleFor(command => command.NameEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.NameAr).RequiredText(CatalogueBGuard.MaxNameLength);
    }
}

public sealed class UpdateCommercialNameHandler(ICommercialNameRepository commercialNames, IDrugTypeRepository drugTypes, IUnitOfWork unitOfWork) : ICommandHandler<UpdateCommercialNameCommand>
{
    public async Task Handle(UpdateCommercialNameCommand command, CancellationToken cancellationToken)
    {
        var commercialName = await commercialNames.GetForUpdateAsync(command.CommercialNameId, cancellationToken)
            ?? throw CatalogueBRules.NotFound(CatalogueBCodes.CommercialNameNotFound, "Commercial name");

        var name = new LocalizedText(command.NameEn, command.NameAr);
        if (await commercialNames.NameExistsAsync(name.En, commercialName.Id, cancellationToken))
        {
            throw CatalogueBRules.NameExists(name.En);
        }

        var drugType = await drugTypes.GetForUpdateAsync(command.DrugTypeId, cancellationToken)
            ?? throw CatalogueBRules.Unknown(CatalogueBCodes.DrugTypeUnknown, "The drug type does not exist.");

        commercialName.Update(drugType, name);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Create)]
public sealed record CreateAntibioticCommand(string Code, string NameEn, string NameAr, string Abbreviation, Guid? CommercialNameId) : ICommand<CreatedResource>;

public sealed class CreateAntibioticValidator : AbstractValidator<CreateAntibioticCommand>
{
    public CreateAntibioticValidator()
    {
        RuleFor(command => command.Code).RequiredText(CatalogueBGuard.MaxCodeLength);
        RuleFor(command => command.NameEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.NameAr).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.Abbreviation).RequiredText(Antibiotic.MaxAbbreviationLength);
    }
}

public sealed class CreateAntibioticHandler(IAntibioticRepository antibiotics, ICommercialNameRepository commercialNames, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreateAntibioticCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateAntibioticCommand command, CancellationToken cancellationToken)
    {
        var code = CatalogueBGuard.Code(command.Code);
        if (await antibiotics.CodeExistsAsync(code, cancellationToken))
        {
            throw CatalogueBRules.CodeExists(code);
        }

        var commercialName = command.CommercialNameId is { } commercialNameId && commercialNameId != Guid.Empty
            ? await commercialNames.GetForUpdateAsync(commercialNameId, cancellationToken) ?? throw CatalogueBRules.Unknown(CatalogueBCodes.CommercialNameUnknown, "The commercial name does not exist.")
            : null;

        var antibiotic = Antibiotic.Create(code, new LocalizedText(command.NameEn, command.NameAr), command.Abbreviation, commercialName, clock.UtcNow);
        antibiotics.Add(antibiotic);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(antibiotic.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record UpdateAntibioticCommand(Guid AntibioticId, string NameEn, string NameAr, string Abbreviation, Guid? CommercialNameId) : ICommand;

public sealed class UpdateAntibioticValidator : AbstractValidator<UpdateAntibioticCommand>
{
    public UpdateAntibioticValidator()
    {
        RuleFor(command => command.AntibioticId).NotEmpty();
        RuleFor(command => command.NameEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.NameAr).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.Abbreviation).RequiredText(Antibiotic.MaxAbbreviationLength);
    }
}

public sealed class UpdateAntibioticHandler(IAntibioticRepository antibiotics, ICommercialNameRepository commercialNames, IUnitOfWork unitOfWork) : ICommandHandler<UpdateAntibioticCommand>
{
    public async Task Handle(UpdateAntibioticCommand command, CancellationToken cancellationToken)
    {
        var antibiotic = await antibiotics.GetForUpdateAsync(command.AntibioticId, cancellationToken)
            ?? throw CatalogueBRules.NotFound(CatalogueBCodes.AntibioticNotFound, "Antibiotic");

        var commercialName = command.CommercialNameId is { } commercialNameId && commercialNameId != Guid.Empty
            ? await commercialNames.GetForUpdateAsync(commercialNameId, cancellationToken) ?? throw CatalogueBRules.Unknown(CatalogueBCodes.CommercialNameUnknown, "The commercial name does not exist.")
            : null;

        antibiotic.Update(new LocalizedText(command.NameEn, command.NameAr), command.Abbreviation, commercialName);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Create)]
public sealed record CreateOrganismCommand(string Code, string NameEn, string NameAr) : ICommand<CreatedResource>;

public sealed class CreateOrganismValidator : AbstractValidator<CreateOrganismCommand>
{
    public CreateOrganismValidator()
    {
        RuleFor(command => command.Code).RequiredText(CatalogueBGuard.MaxCodeLength);
        RuleFor(command => command.NameEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.NameAr).RequiredText(CatalogueBGuard.MaxNameLength);
    }
}

public sealed class CreateOrganismHandler(IOrganismRepository organisms, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreateOrganismCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateOrganismCommand command, CancellationToken cancellationToken)
    {
        var code = CatalogueBGuard.Code(command.Code);
        if (await organisms.CodeExistsAsync(code, cancellationToken))
        {
            throw CatalogueBRules.CodeExists(code);
        }

        var organism = Organism.Create(code, new LocalizedText(command.NameEn, command.NameAr), clock.UtcNow);
        organisms.Add(organism);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(organism.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record UpdateOrganismCommand(Guid OrganismId, string NameEn, string NameAr) : ICommand;

public sealed class UpdateOrganismValidator : AbstractValidator<UpdateOrganismCommand>
{
    public UpdateOrganismValidator()
    {
        RuleFor(command => command.OrganismId).NotEmpty();
        RuleFor(command => command.NameEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.NameAr).RequiredText(CatalogueBGuard.MaxNameLength);
    }
}

public sealed class UpdateOrganismHandler(IOrganismRepository organisms, IUnitOfWork unitOfWork) : ICommandHandler<UpdateOrganismCommand>
{
    public async Task Handle(UpdateOrganismCommand command, CancellationToken cancellationToken)
    {
        var organism = await organisms.GetForUpdateAsync(command.OrganismId, cancellationToken)
            ?? throw CatalogueBRules.NotFound(CatalogueBCodes.OrganismNotFound, "Organism");

        organism.Rename(new LocalizedText(command.NameEn, command.NameAr));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Create)]
public sealed record CreateSensitivityValueCommand(string Code, string NameEn, string NameAr, int DisplayOrder) : ICommand<CreatedResource>;

public sealed class CreateSensitivityValueValidator : AbstractValidator<CreateSensitivityValueCommand>
{
    public CreateSensitivityValueValidator()
    {
        RuleFor(command => command.Code).RequiredText(SensitivityValue.MaxCodeLength);
        RuleFor(command => command.NameEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.NameAr).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.DisplayOrder).InclusiveBetween(0, SensitivityValue.MaxDisplayOrder);
    }
}

public sealed class CreateSensitivityValueHandler(ISensitivityValueRepository values, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreateSensitivityValueCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateSensitivityValueCommand command, CancellationToken cancellationToken)
    {
        var code = CatalogueBGuard.Code(command.Code, SensitivityValue.MaxCodeLength);
        if (await values.CodeExistsAsync(code, cancellationToken))
        {
            throw CatalogueBRules.CodeExists(code);
        }

        var value = SensitivityValue.Create(code, new LocalizedText(command.NameEn, command.NameAr), command.DisplayOrder, clock.UtcNow);
        values.Add(value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(value.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record UpdateSensitivityValueCommand(Guid SensitivityValueId, string NameEn, string NameAr, int DisplayOrder) : ICommand;

public sealed class UpdateSensitivityValueValidator : AbstractValidator<UpdateSensitivityValueCommand>
{
    public UpdateSensitivityValueValidator()
    {
        RuleFor(command => command.SensitivityValueId).NotEmpty();
        RuleFor(command => command.NameEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.NameAr).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.DisplayOrder).InclusiveBetween(0, SensitivityValue.MaxDisplayOrder);
    }
}

public sealed class UpdateSensitivityValueHandler(ISensitivityValueRepository values, IUnitOfWork unitOfWork) : ICommandHandler<UpdateSensitivityValueCommand>
{
    public async Task Handle(UpdateSensitivityValueCommand command, CancellationToken cancellationToken)
    {
        var value = await values.GetForUpdateAsync(command.SensitivityValueId, cancellationToken)
            ?? throw CatalogueBRules.NotFound(CatalogueBCodes.SensitivityValueNotFound, "Sensitivity value");

        value.Update(new LocalizedText(command.NameEn, command.NameAr), command.DisplayOrder);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
