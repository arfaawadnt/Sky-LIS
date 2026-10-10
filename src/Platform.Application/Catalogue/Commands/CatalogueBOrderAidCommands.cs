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

// Order aids of Catalogue B (CP-46 preparations, CP-49 diagnoses, CP-51 shortcut sets, CP-52 test rules), T1/T2 of
// intake LIS-INT-CTB-001. Each handler: check, load or create, call the aggregate, commit once (01 §3.3).

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Create)]
public sealed record CreatePreparationCommand(string Code, string QuestionEn, string QuestionAr, string AnswerType, IReadOnlyList<string>? Options, string Gender, IReadOnlyList<Guid>? ServiceIds) : ICommand<CreatedResource>;

public sealed class CreatePreparationValidator : AbstractValidator<CreatePreparationCommand>
{
    public CreatePreparationValidator()
    {
        RuleFor(command => command.Code).RequiredText(CatalogueBGuard.MaxCodeLength);
        RuleFor(command => command.QuestionEn).RequiredText(CatalogueBGuard.MaxLongTextLength);
        RuleFor(command => command.QuestionAr).RequiredText(CatalogueBGuard.MaxLongTextLength);
        RuleFor(command => command.AnswerType).EnumName<CreatePreparationCommand, PreparationAnswerType>();
        RuleFor(command => command.Gender).EnumName<CreatePreparationCommand, PreparationGender>();
        RuleFor(command => command.Options).Must(options => options is null || options.Count <= Preparation.MaxOptions).WithMessage($"At most {Preparation.MaxOptions} options.");
        RuleForEach(command => command.Options).MaximumLength(Preparation.MaxOptionLength);
        RuleFor(command => command.ServiceIds).IdSet();
    }
}

public sealed class CreatePreparationHandler(IPreparationRepository preparations, ICatalogueBReferences references, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreatePreparationCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreatePreparationCommand command, CancellationToken cancellationToken)
    {
        var code = CatalogueBGuard.Code(command.Code);
        if (await preparations.CodeExistsAsync(code, cancellationToken))
        {
            throw CatalogueBRules.CodeExists(code);
        }

        await CatalogueBRules.RequireServicesAsync(references, command.ServiceIds, cancellationToken);
        var preparation = Preparation.Create(
            code,
            new LocalizedText(command.QuestionEn, command.QuestionAr),
            CatalogueBRules.Parse<PreparationAnswerType>(command.AnswerType),
            command.Options,
            CatalogueBRules.Parse<PreparationGender>(command.Gender),
            command.ServiceIds,
            clock.UtcNow);
        preparations.Add(preparation);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(preparation.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record UpdatePreparationCommand(Guid PreparationId, string QuestionEn, string QuestionAr, string AnswerType, IReadOnlyList<string>? Options, string Gender, IReadOnlyList<Guid>? ServiceIds) : ICommand;

public sealed class UpdatePreparationValidator : AbstractValidator<UpdatePreparationCommand>
{
    public UpdatePreparationValidator()
    {
        RuleFor(command => command.PreparationId).NotEmpty();
        RuleFor(command => command.QuestionEn).RequiredText(CatalogueBGuard.MaxLongTextLength);
        RuleFor(command => command.QuestionAr).RequiredText(CatalogueBGuard.MaxLongTextLength);
        RuleFor(command => command.AnswerType).EnumName<UpdatePreparationCommand, PreparationAnswerType>();
        RuleFor(command => command.Gender).EnumName<UpdatePreparationCommand, PreparationGender>();
        RuleFor(command => command.Options).Must(options => options is null || options.Count <= Preparation.MaxOptions).WithMessage($"At most {Preparation.MaxOptions} options.");
        RuleForEach(command => command.Options).MaximumLength(Preparation.MaxOptionLength);
        RuleFor(command => command.ServiceIds).IdSet();
    }
}

public sealed class UpdatePreparationHandler(IPreparationRepository preparations, ICatalogueBReferences references, IUnitOfWork unitOfWork) : ICommandHandler<UpdatePreparationCommand>
{
    public async Task Handle(UpdatePreparationCommand command, CancellationToken cancellationToken)
    {
        var preparation = await preparations.GetForUpdateAsync(command.PreparationId, cancellationToken)
            ?? throw CatalogueBRules.NotFound(CatalogueBCodes.PreparationNotFound, "Preparation");

        await CatalogueBRules.RequireServicesAsync(references, command.ServiceIds, cancellationToken);
        preparation.Update(
            new LocalizedText(command.QuestionEn, command.QuestionAr),
            CatalogueBRules.Parse<PreparationAnswerType>(command.AnswerType),
            command.Options,
            CatalogueBRules.Parse<PreparationGender>(command.Gender),
            command.ServiceIds);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Create)]
public sealed record CreateDiagnosisCommand(string IcdCode, string NameEn, string NameAr, IReadOnlyList<Guid>? ServiceIds) : ICommand<CreatedResource>;

public sealed class CreateDiagnosisValidator : AbstractValidator<CreateDiagnosisCommand>
{
    public CreateDiagnosisValidator()
    {
        RuleFor(command => command.IcdCode).RequiredText(CatalogueBGuard.MaxCodeLength);
        RuleFor(command => command.NameEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.NameAr).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.ServiceIds).IdSet();
    }
}

public sealed class CreateDiagnosisHandler(IDiagnosisRepository diagnoses, ICatalogueBReferences references, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreateDiagnosisCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateDiagnosisCommand command, CancellationToken cancellationToken)
    {
        var icdCode = CatalogueBGuard.Code(command.IcdCode);
        if (await diagnoses.IcdCodeExistsAsync(icdCode, cancellationToken))
        {
            throw CatalogueBRules.CodeExists(icdCode);
        }

        await CatalogueBRules.RequireServicesAsync(references, command.ServiceIds, cancellationToken);
        var diagnosis = Diagnosis.Create(icdCode, new LocalizedText(command.NameEn, command.NameAr), command.ServiceIds, clock.UtcNow);
        diagnoses.Add(diagnosis);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(diagnosis.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record UpdateDiagnosisCommand(Guid DiagnosisId, string NameEn, string NameAr, IReadOnlyList<Guid>? ServiceIds) : ICommand;

public sealed class UpdateDiagnosisValidator : AbstractValidator<UpdateDiagnosisCommand>
{
    public UpdateDiagnosisValidator()
    {
        RuleFor(command => command.DiagnosisId).NotEmpty();
        RuleFor(command => command.NameEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.NameAr).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.ServiceIds).IdSet();
    }
}

public sealed class UpdateDiagnosisHandler(IDiagnosisRepository diagnoses, ICatalogueBReferences references, IUnitOfWork unitOfWork) : ICommandHandler<UpdateDiagnosisCommand>
{
    public async Task Handle(UpdateDiagnosisCommand command, CancellationToken cancellationToken)
    {
        var diagnosis = await diagnoses.GetForUpdateAsync(command.DiagnosisId, cancellationToken)
            ?? throw CatalogueBRules.NotFound(CatalogueBCodes.DiagnosisNotFound, "Diagnosis");

        await CatalogueBRules.RequireServicesAsync(references, command.ServiceIds, cancellationToken);
        diagnosis.Update(new LocalizedText(command.NameEn, command.NameAr), command.ServiceIds);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Create)]
public sealed record CreateShortcutSetCommand(string Code, string NameEn, string NameAr, IReadOnlyList<Guid> ServiceIds) : ICommand<CreatedResource>;

public sealed class CreateShortcutSetValidator : AbstractValidator<CreateShortcutSetCommand>
{
    public CreateShortcutSetValidator()
    {
        RuleFor(command => command.Code).RequiredText(CatalogueBGuard.MaxCodeLength);
        RuleFor(command => command.NameEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.NameAr).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.ServiceIds).RequiredIdSet();
    }
}

public sealed class CreateShortcutSetHandler(IShortcutSetRepository sets, ICatalogueBReferences references, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreateShortcutSetCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateShortcutSetCommand command, CancellationToken cancellationToken)
    {
        var code = CatalogueBGuard.Code(command.Code);
        if (await sets.CodeExistsAsync(code, cancellationToken))
        {
            throw CatalogueBRules.CodeExists(code);
        }

        await CatalogueBRules.RequireServicesAsync(references, command.ServiceIds, cancellationToken);
        var set = ShortcutSet.Create(code, new LocalizedText(command.NameEn, command.NameAr), command.ServiceIds, clock.UtcNow);
        sets.Add(set);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(set.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record UpdateShortcutSetCommand(Guid ShortcutSetId, string NameEn, string NameAr, IReadOnlyList<Guid> ServiceIds) : ICommand;

public sealed class UpdateShortcutSetValidator : AbstractValidator<UpdateShortcutSetCommand>
{
    public UpdateShortcutSetValidator()
    {
        RuleFor(command => command.ShortcutSetId).NotEmpty();
        RuleFor(command => command.NameEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.NameAr).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.ServiceIds).RequiredIdSet();
    }
}

public sealed class UpdateShortcutSetHandler(IShortcutSetRepository sets, ICatalogueBReferences references, IUnitOfWork unitOfWork) : ICommandHandler<UpdateShortcutSetCommand>
{
    public async Task Handle(UpdateShortcutSetCommand command, CancellationToken cancellationToken)
    {
        var set = await sets.GetForUpdateAsync(command.ShortcutSetId, cancellationToken)
            ?? throw CatalogueBRules.NotFound(CatalogueBCodes.ShortcutSetNotFound, "Shortcut set");

        await CatalogueBRules.RequireServicesAsync(references, command.ServiceIds, cancellationToken);
        set.Update(new LocalizedText(command.NameEn, command.NameAr), command.ServiceIds);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Create)]
public sealed record CreateTestRuleCommand(string NameEn, string NameAr, Guid? ClinicalGroupId, IReadOnlyList<Guid> TriggerServiceIds, IReadOnlyList<Guid> AddedServiceIds) : ICommand<CreatedResource>;

public sealed class CreateTestRuleValidator : AbstractValidator<CreateTestRuleCommand>
{
    public CreateTestRuleValidator()
    {
        RuleFor(command => command.NameEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.NameAr).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.TriggerServiceIds).RequiredIdSet();
        RuleFor(command => command.AddedServiceIds).RequiredIdSet();
    }
}

public sealed class CreateTestRuleHandler(ITestRuleRepository rules, ICatalogueBReferences references, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreateTestRuleCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateTestRuleCommand command, CancellationToken cancellationToken)
    {
        var name = new LocalizedText(command.NameEn, command.NameAr);
        if (await rules.NameExistsAsync(name.En, null, cancellationToken))
        {
            throw CatalogueBRules.NameExists(name.En);
        }

        await CatalogueBRules.RequireClinicalGroupAsync(references, command.ClinicalGroupId, cancellationToken);
        await CatalogueBRules.RequireServicesAsync(references, command.TriggerServiceIds.Concat(command.AddedServiceIds), cancellationToken);
        var rule = TestRule.Create(name, command.ClinicalGroupId, command.TriggerServiceIds, command.AddedServiceIds, clock.UtcNow);
        rules.Add(rule);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(rule.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record UpdateTestRuleCommand(Guid TestRuleId, string NameEn, string NameAr, Guid? ClinicalGroupId, IReadOnlyList<Guid> TriggerServiceIds, IReadOnlyList<Guid> AddedServiceIds) : ICommand;

public sealed class UpdateTestRuleValidator : AbstractValidator<UpdateTestRuleCommand>
{
    public UpdateTestRuleValidator()
    {
        RuleFor(command => command.TestRuleId).NotEmpty();
        RuleFor(command => command.NameEn).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.NameAr).RequiredText(CatalogueBGuard.MaxNameLength);
        RuleFor(command => command.TriggerServiceIds).RequiredIdSet();
        RuleFor(command => command.AddedServiceIds).RequiredIdSet();
    }
}

public sealed class UpdateTestRuleHandler(ITestRuleRepository rules, ICatalogueBReferences references, IUnitOfWork unitOfWork) : ICommandHandler<UpdateTestRuleCommand>
{
    public async Task Handle(UpdateTestRuleCommand command, CancellationToken cancellationToken)
    {
        var rule = await rules.GetForUpdateAsync(command.TestRuleId, cancellationToken)
            ?? throw CatalogueBRules.NotFound(CatalogueBCodes.TestRuleNotFound, "Test rule");

        var name = new LocalizedText(command.NameEn, command.NameAr);
        if (await rules.NameExistsAsync(name.En, rule.Id, cancellationToken))
        {
            throw CatalogueBRules.NameExists(name.En);
        }

        await CatalogueBRules.RequireClinicalGroupAsync(references, command.ClinicalGroupId, cancellationToken);
        await CatalogueBRules.RequireServicesAsync(references, command.TriggerServiceIds.Concat(command.AddedServiceIds), cancellationToken);
        rule.Update(name, command.ClinicalGroupId, command.TriggerServiceIds, command.AddedServiceIds);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
