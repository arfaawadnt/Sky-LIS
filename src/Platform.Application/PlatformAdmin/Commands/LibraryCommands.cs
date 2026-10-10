using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin.Commands;

// Integration library (SA-15 analyser models, SA-16 PCR kits, SA-17 code systems, SA-18 rule templates).

/// <summary>The library rows that only toggle between active and inactive.</summary>
public enum LibraryKind
{
    AnalyserModel = 0,
    PcrKit = 1,
    CodeSystem = 2,
    RuleTemplate = 3,
}

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Manage)]
public sealed record SaveAnalyserModelCommand(Guid? AnalyserModelId, string Vendor, string Model, string Discipline, string Protocol, string PhysicalLink, IReadOnlyList<string>? Modes, string? DriverVersion)
    : ICommand<CreatedResource>, IPlatformAdministrationRequest;

public sealed class SaveAnalyserModelValidator : AbstractValidator<SaveAnalyserModelCommand>
{
    public SaveAnalyserModelValidator()
    {
        RuleFor(command => command.Vendor).NotEmpty().MaximumLength(AnalyserModel.MaxTextLength);
        RuleFor(command => command.Model).NotEmpty().MaximumLength(AnalyserModel.MaxTextLength);
        RuleFor(command => command.Discipline).NotEmpty().MaximumLength(AnalyserModel.MaxTextLength);
        RuleFor(command => command.Protocol).NotEmpty().IsEnumName(typeof(AnalyserProtocol), caseSensitive: false);
        RuleFor(command => command.PhysicalLink).NotEmpty().IsEnumName(typeof(PhysicalLink), caseSensitive: false);
        RuleFor(command => command.Modes).Must(modes => modes is null || modes.Count <= AnalyserModel.MaxModes).WithMessage($"At most {AnalyserModel.MaxModes} modes.");
        RuleForEach(command => command.Modes).MaximumLength(AnalyserModel.MaxModeLength);
        RuleFor(command => command.DriverVersion).MaximumLength(40);
    }
}

/// <summary>Creates (no id) or edits (id) an analyser model; vendor + model stays unique (LIB-100).</summary>
public sealed class SaveAnalyserModelHandler(IAnalyserModelRepository models, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<SaveAnalyserModelCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(SaveAnalyserModelCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var data = new AnalyserModelData(command.Vendor, command.Model, command.Discipline, Enum.Parse<AnalyserProtocol>(command.Protocol, ignoreCase: true), Enum.Parse<PhysicalLink>(command.PhysicalLink, ignoreCase: true), command.Modes ?? [], command.DriverVersion);
        AnalyserModel model;
        if (command.AnalyserModelId is { } id)
        {
            model = await models.GetForUpdateAsync(id, cancellationToken)
                ?? throw new DomainException(PlatformAdminCodes.AnalyserModelNotFound, "Analyser model not found.", DomainFault.NotFound);
            model.Update(data);
        }
        else
        {
            model = AnalyserModel.Create(data);
            models.Add(model);
        }

        if (await models.ExistsAsync(model.Vendor, model.Model, model.Id, cancellationToken))
        {
            throw new DomainException(PlatformAdminCodes.AnalyserModelExists, $"{model.Vendor} {model.Model} already exists.", DomainFault.Conflict);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(model.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Manage)]
public sealed record SavePcrKitCommand(Guid? PcrKitId, string Name, Guid? AnalyserModelId, string LayoutJson, string ThresholdsJson, string VerdictRulesJson)
    : ICommand<CreatedResource>, IPlatformAdministrationRequest;

public sealed class SavePcrKitValidator : AbstractValidator<SavePcrKitCommand>
{
    public SavePcrKitValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(PcrKitTemplate.MaxNameLength);
        RuleFor(command => command.LayoutJson).NotEmpty().MaximumLength(PcrKitTemplate.MaxJsonLength);
        RuleFor(command => command.ThresholdsJson).NotEmpty().MaximumLength(PcrKitTemplate.MaxJsonLength);
        RuleFor(command => command.VerdictRulesJson).NotEmpty().MaximumLength(PcrKitTemplate.MaxJsonLength);
    }
}

public sealed class SavePcrKitHandler(IPcrKitTemplateRepository kits, IAnalyserModelRepository models, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<SavePcrKitCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(SavePcrKitCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var model = command.AnalyserModelId is { } modelId
            ? await models.GetForUpdateAsync(modelId, cancellationToken) ?? throw new DomainException(PlatformAdminCodes.AnalyserModelNotFound, "Analyser model not found.", DomainFault.NotFound)
            : null;

        PcrKitTemplate kit;
        if (command.PcrKitId is { } id)
        {
            kit = await kits.GetForUpdateAsync(id, cancellationToken)
                ?? throw new DomainException(PlatformAdminCodes.PcrKitNotFound, "PCR kit not found.", DomainFault.NotFound);
            kit.Update(command.Name, model, command.LayoutJson, command.ThresholdsJson, command.VerdictRulesJson);
        }
        else
        {
            kit = PcrKitTemplate.Create(command.Name, model, command.LayoutJson, command.ThresholdsJson, command.VerdictRulesJson);
            kits.Add(kit);
        }

        if (await kits.NameExistsAsync(kit.Name, kit.Id, cancellationToken))
        {
            throw new DomainException(PlatformAdminCodes.PcrKitExists, $"A PCR kit named '{kit.Name}' already exists.", DomainFault.Conflict);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(kit.Id);
    }
}

/// <summary>Creates (no id) a code system or edits its version and use (id); the name is fixed once created.</summary>
[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Manage)]
public sealed record SaveCodeSystemCommand(Guid? CodeSystemId, string Name, string Version, string? Use) : ICommand<CreatedResource>, IPlatformAdministrationRequest;

public sealed class SaveCodeSystemValidator : AbstractValidator<SaveCodeSystemCommand>
{
    public SaveCodeSystemValidator()
    {
        RuleFor(command => command.Name).NotEmpty().IsEnumName(typeof(CodeSystemName), caseSensitive: false);
        RuleFor(command => command.Version).NotEmpty().MaximumLength(CodeSystem.MaxVersionLength);
        RuleFor(command => command.Use).MaximumLength(CodeSystem.MaxUseLength);
    }
}

public sealed class SaveCodeSystemHandler(ICodeSystemRepository codeSystems, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<SaveCodeSystemCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(SaveCodeSystemCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        CodeSystem codeSystem;
        if (command.CodeSystemId is { } id)
        {
            codeSystem = await codeSystems.GetForUpdateAsync(id, cancellationToken)
                ?? throw new DomainException(PlatformAdminCodes.CodeSystemNotFound, "Code system not found.", DomainFault.NotFound);
            codeSystem.Update(command.Version, command.Use);
        }
        else
        {
            codeSystem = CodeSystem.Create(Enum.Parse<CodeSystemName>(command.Name, ignoreCase: true), command.Version, command.Use);
            codeSystems.Add(codeSystem);
        }

        if (await codeSystems.ExistsAsync(codeSystem.Name, codeSystem.Version, codeSystem.Id, cancellationToken))
        {
            throw new DomainException(PlatformAdminCodes.CodeSystemExists, $"{codeSystem.Name} {codeSystem.Version} already exists.", DomainFault.Conflict);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(codeSystem.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Manage)]
public sealed record SaveRuleTemplateCommand(Guid? RuleTemplateId, string Name, string? ClinicalGroupName, string Formula, string? Description) : ICommand<CreatedResource>, IPlatformAdministrationRequest;

public sealed class SaveRuleTemplateValidator : AbstractValidator<SaveRuleTemplateCommand>
{
    public SaveRuleTemplateValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(RuleTemplate.MaxNameLength);
        RuleFor(command => command.ClinicalGroupName).MaximumLength(RuleTemplate.MaxGroupLength);
        RuleFor(command => command.Formula).NotEmpty().MaximumLength(RuleTemplate.MaxFormulaLength);
        RuleFor(command => command.Description).MaximumLength(RuleTemplate.MaxDescriptionLength);
    }
}

public sealed class SaveRuleTemplateHandler(IRuleTemplateRepository rules, ICurrentTenant currentTenant, IUnitOfWork unitOfWork) : ICommandHandler<SaveRuleTemplateCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(SaveRuleTemplateCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        RuleTemplate rule;
        if (command.RuleTemplateId is { } id)
        {
            rule = await rules.GetForUpdateAsync(id, cancellationToken)
                ?? throw new DomainException(PlatformAdminCodes.RuleTemplateNotFound, "Rule template not found.", DomainFault.NotFound);
            rule.Update(command.Name, command.ClinicalGroupName, command.Formula, command.Description);
        }
        else
        {
            rule = RuleTemplate.Create(command.Name, command.ClinicalGroupName, command.Formula, command.Description);
            rules.Add(rule);
        }

        if (await rules.NameExistsAsync(rule.Name, rule.Id, cancellationToken))
        {
            throw new DomainException(PlatformAdminCodes.RuleTemplateExists, $"A rule template named '{rule.Name}' already exists.", DomainFault.Conflict);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(rule.Id);
    }
}

/// <summary>Deactivates (<paramref name="Active"/> false) or reactivates one library row.</summary>
[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Manage)]
public sealed record SetLibraryItemActiveCommand(LibraryKind Kind, Guid Id, bool Active) : ICommand, IPlatformAdministrationRequest;

public sealed class SetLibraryItemActiveValidator : AbstractValidator<SetLibraryItemActiveCommand>
{
    public SetLibraryItemActiveValidator()
    {
        RuleFor(command => command.Kind).IsInEnum();
        RuleFor(command => command.Id).NotEmpty();
    }
}

public sealed class SetLibraryItemActiveHandler(
    IAnalyserModelRepository models,
    IPcrKitTemplateRepository kits,
    ICodeSystemRepository codeSystems,
    IRuleTemplateRepository rules,
    ICurrentTenant currentTenant,
    IUnitOfWork unitOfWork) : ICommandHandler<SetLibraryItemActiveCommand>
{
    public async Task Handle(SetLibraryItemActiveCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        (Action Deactivate, Action Reactivate) toggle = command.Kind switch
        {
            LibraryKind.AnalyserModel => Toggle(await models.GetForUpdateAsync(command.Id, cancellationToken), PlatformAdminCodes.AnalyserModelNotFound, item => item.Deactivate, item => item.Reactivate),
            LibraryKind.PcrKit => Toggle(await kits.GetForUpdateAsync(command.Id, cancellationToken), PlatformAdminCodes.PcrKitNotFound, item => item.Deactivate, item => item.Reactivate),
            LibraryKind.CodeSystem => Toggle(await codeSystems.GetForUpdateAsync(command.Id, cancellationToken), PlatformAdminCodes.CodeSystemNotFound, item => item.Deactivate, item => item.Reactivate),
            LibraryKind.RuleTemplate => Toggle(await rules.GetForUpdateAsync(command.Id, cancellationToken), PlatformAdminCodes.RuleTemplateNotFound, item => item.Deactivate, item => item.Reactivate),
            _ => throw new DomainException(PlatformAdminCodes.LibraryTextInvalid, "Unknown library kind.", DomainFault.Validation),
        };

        if (command.Active)
        {
            toggle.Reactivate();
        }
        else
        {
            toggle.Deactivate();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static (Action, Action) Toggle<T>(T? item, string notFoundCode, Func<T, Action> deactivate, Func<T, Action> reactivate)
        where T : class =>
        item is null
            ? throw new DomainException(notFoundCode, "Library item not found.", DomainFault.NotFound)
            : (deactivate(item), reactivate(item));
}

/// <summary>I1 Import codes from CSV (header <c>code,display</c>; upsert by code). The file stream is read once, up to 20 MB.</summary>
[RequirePermissionPolicy(PermissionCatalog.Integration, PermissionAction.Manage)]
public sealed record ImportCodeSystemItemsCommand(Guid CodeSystemId, Stream Content, long Length) : ICommand<Contracts.PlatformAdmin.CodeSystemImportResult>, IPlatformAdministrationRequest;

public sealed class ImportCodeSystemItemsValidator : AbstractValidator<ImportCodeSystemItemsCommand>
{
    public ImportCodeSystemItemsValidator()
    {
        RuleFor(command => command.CodeSystemId).NotEmpty();
        RuleFor(command => command.Content).NotNull();
        RuleFor(command => command.Length).GreaterThan(0);
    }
}

public sealed class ImportCodeSystemItemsHandler(ICodeSystemRepository codeSystems, ICurrentTenant currentTenant, IUnitOfWork unitOfWork, SharedKernel.Abstractions.IClock clock)
    : ICommandHandler<ImportCodeSystemItemsCommand, Contracts.PlatformAdmin.CodeSystemImportResult>
{
    public async Task<Contracts.PlatformAdmin.CodeSystemImportResult> Handle(ImportCodeSystemItemsCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        if (command.Length > CodeSystemCsv.MaxBytes)
        {
            throw new DomainException(PlatformAdminCodes.ImportTooLarge, $"The file exceeds {CodeSystemCsv.MaxBytes / (1024 * 1024)} MB.", DomainFault.Validation);
        }

        var codeSystem = await codeSystems.GetForUpdateAsync(command.CodeSystemId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.CodeSystemNotFound, "Code system not found.", DomainFault.NotFound);

        var rows = await CodeSystemCsv.ReadAsync(command.Content, cancellationToken);
        var existing = await codeSystems.LoadItemsAsync(codeSystem.Id, cancellationToken);
        var result = codeSystem.Import(existing, rows, clock.UtcNow);
        codeSystems.AddItems(result.NewItems);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Contracts.PlatformAdmin.CodeSystemImportResult(result.Inserted, result.Updated, result.Unchanged, codeSystem.ItemCount);
    }
}
