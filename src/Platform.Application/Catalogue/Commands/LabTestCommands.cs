using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Catalogue.Services;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.Catalogue;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Catalogue.Commands;

// Tests (SRS CP-41; intake LIS-INT-CAT-001 T13–T19). Every configuration is a whole-list replace on the aggregate.

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Create)]
public sealed record CreateLabTestCommand(
    string Code,
    string NameEn,
    string NameAr,
    string? ReportName,
    string? Loinc,
    string? Cpt,
    Guid ClinicalGroupId,
    string Type,
    Guid? SampleTypeId,
    Guid? SampleConditionId,
    Guid? UnitId,
    string Gender,
    string? Methodology,
    int Decimals,
    bool IsHiddenHelper,
    bool PrintReference,
    int LabelCount,
    string? Prefix) : ICommand<CreatedResource>;

public sealed class CreateLabTestValidator : AbstractValidator<CreateLabTestCommand>
{
    public CreateLabTestValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(LabTest.MaxCodeLength);
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.ReportName).MaximumLength(LabTest.MaxReportNameLength);
        RuleFor(command => command.Loinc).MaximumLength(LabTest.MaxReferenceCodeLength);
        RuleFor(command => command.Cpt).MaximumLength(LabTest.MaxReferenceCodeLength);
        RuleFor(command => command.ClinicalGroupId).NotEmpty();
        RuleFor(command => command.Type).NotEmpty().IsEnumName(typeof(LabTestType), caseSensitive: false);
        RuleFor(command => command.Gender).NotEmpty().IsEnumName(typeof(GenderRestriction), caseSensitive: false);
        RuleFor(command => command.Methodology).MaximumLength(LabTest.MaxMethodologyLength);
        RuleFor(command => command.Decimals).InclusiveBetween(0, LabTest.MaxDecimals);
        RuleFor(command => command.LabelCount).InclusiveBetween(0, LabTest.MaxLabelCount);
        RuleFor(command => command.Prefix).MaximumLength(LabTest.MaxPrefixLength);
    }
}

public sealed class CreateLabTestHandler(
    ILabTestRepository tests,
    IClinicalGroupRepository groups,
    ISampleTypeRepository sampleTypes,
    IMeasurementUnitRepository units,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<CreateLabTestCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateLabTestCommand command, CancellationToken cancellationToken)
    {
        var code = LabTest.CleanCode(command.Code);
        if (await tests.CodeExistsAsync(code, cancellationToken))
        {
            throw new DomainException(LabTestCodes.CodeExists, $"A test with code '{code}' already exists.", DomainFault.Conflict);
        }

        await CatalogueAReferences.RequireClinicalGroupAsync(groups, command.ClinicalGroupId, null, cancellationToken);
        await CatalogueAReferences.RequireSampleAsync(sampleTypes, command.SampleTypeId, command.SampleConditionId, null, null, cancellationToken);
        await CatalogueAReferences.RequireUnitAsync(units, command.UnitId, null, cancellationToken);

        var definition = LabTestDefinitions.From(
            command.NameEn, command.NameAr, command.ReportName, command.Loinc, command.Cpt, command.ClinicalGroupId, command.Type, command.SampleTypeId,
            command.SampleConditionId, command.UnitId, command.Gender, command.Methodology, command.Decimals, command.IsHiddenHelper, command.PrintReference,
            command.LabelCount, command.Prefix);
        var test = LabTest.Create(code, definition, clock.UtcNow);
        tests.Add(test);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(test.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record UpdateLabTestCommand(
    Guid TestId,
    string NameEn,
    string NameAr,
    string? ReportName,
    string? Loinc,
    string? Cpt,
    Guid ClinicalGroupId,
    string Type,
    Guid? SampleTypeId,
    Guid? SampleConditionId,
    Guid? UnitId,
    string Gender,
    string? Methodology,
    int Decimals,
    bool IsHiddenHelper,
    bool PrintReference,
    int LabelCount,
    string? Prefix) : ICommand;

public sealed class UpdateLabTestValidator : AbstractValidator<UpdateLabTestCommand>
{
    public UpdateLabTestValidator()
    {
        RuleFor(command => command.TestId).NotEmpty();
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.ReportName).MaximumLength(LabTest.MaxReportNameLength);
        RuleFor(command => command.Loinc).MaximumLength(LabTest.MaxReferenceCodeLength);
        RuleFor(command => command.Cpt).MaximumLength(LabTest.MaxReferenceCodeLength);
        RuleFor(command => command.ClinicalGroupId).NotEmpty();
        RuleFor(command => command.Type).NotEmpty().IsEnumName(typeof(LabTestType), caseSensitive: false);
        RuleFor(command => command.Gender).NotEmpty().IsEnumName(typeof(GenderRestriction), caseSensitive: false);
        RuleFor(command => command.Methodology).MaximumLength(LabTest.MaxMethodologyLength);
        RuleFor(command => command.Decimals).InclusiveBetween(0, LabTest.MaxDecimals);
        RuleFor(command => command.LabelCount).InclusiveBetween(0, LabTest.MaxLabelCount);
        RuleFor(command => command.Prefix).MaximumLength(LabTest.MaxPrefixLength);
    }
}

public sealed class UpdateLabTestHandler(
    ILabTestRepository tests,
    IClinicalGroupRepository groups,
    ISampleTypeRepository sampleTypes,
    IMeasurementUnitRepository units,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<UpdateLabTestCommand>
{
    public async Task Handle(UpdateLabTestCommand command, CancellationToken cancellationToken)
    {
        var test = await tests.GetForUpdateAsync(command.TestId, cancellationToken)
            ?? throw new DomainException(LabTestCodes.NotFound, "Test not found.", DomainFault.NotFound);

        await CatalogueAReferences.RequireClinicalGroupAsync(groups, command.ClinicalGroupId, test.ClinicalGroupId, cancellationToken);
        await CatalogueAReferences.RequireSampleAsync(sampleTypes, command.SampleTypeId, command.SampleConditionId, test.SampleTypeId, test.SampleConditionId, cancellationToken);
        await CatalogueAReferences.RequireUnitAsync(units, command.UnitId, test.UnitId, cancellationToken);

        var definition = LabTestDefinitions.From(
            command.NameEn, command.NameAr, command.ReportName, command.Loinc, command.Cpt, command.ClinicalGroupId, command.Type, command.SampleTypeId,
            command.SampleConditionId, command.UnitId, command.Gender, command.Methodology, command.Decimals, command.IsHiddenHelper, command.PrintReference,
            command.LabelCount, command.Prefix);
        test.Change(definition, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record SetLabTestTimingCommand(Guid TestId, int DurationHours, int MinDurationHours, int? FrequencyCheckDays, string FrequencyAction, decimal? DeltaPercent, int? DeltaWindowDays, int TestDays) : ICommand;

public sealed class SetLabTestTimingValidator : AbstractValidator<SetLabTestTimingCommand>
{
    public SetLabTestTimingValidator()
    {
        RuleFor(command => command.TestId).NotEmpty();
        RuleFor(command => command.DurationHours).InclusiveBetween(0, LabTest.MaxDurationHours);
        RuleFor(command => command.MinDurationHours).InclusiveBetween(0, LabTest.MaxDurationHours);
        RuleFor(command => command.FrequencyCheckDays).InclusiveBetween(1, LabTest.MaxIntervalDays).When(command => command.FrequencyCheckDays is not null);
        RuleFor(command => command.FrequencyAction).NotEmpty().IsEnumName(typeof(FrequencyCheckAction), caseSensitive: false);
        RuleFor(command => command.DeltaPercent).GreaterThan(0m).LessThanOrEqualTo(LabTest.MaxDeltaPercent).When(command => command.DeltaPercent is not null);
        RuleFor(command => command.DeltaWindowDays).InclusiveBetween(1, LabTest.MaxIntervalDays).When(command => command.DeltaWindowDays is not null);
        RuleFor(command => command.TestDays).InclusiveBetween(1, LabTestTiming.EveryDay);
    }
}

public sealed class SetLabTestTimingHandler(ILabTestRepository tests, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<SetLabTestTimingCommand>
{
    public async Task Handle(SetLabTestTimingCommand command, CancellationToken cancellationToken)
    {
        var test = await tests.GetForUpdateAsync(command.TestId, cancellationToken)
            ?? throw new DomainException(LabTestCodes.NotFound, "Test not found.", DomainFault.NotFound);

        var timing = new LabTestTiming(
            command.DurationHours,
            command.MinDurationHours,
            command.FrequencyCheckDays,
            Enum.Parse<FrequencyCheckAction>(command.FrequencyAction, ignoreCase: true),
            command.DeltaPercent,
            command.DeltaWindowDays,
            command.TestDays);
        test.SetTiming(timing, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record ReplaceRangeBandsCommand(Guid TestId, IReadOnlyList<RangeBandItem> Bands) : ICommand;

public sealed class ReplaceRangeBandsValidator : AbstractValidator<ReplaceRangeBandsCommand>
{
    public ReplaceRangeBandsValidator()
    {
        RuleFor(command => command.TestId).NotEmpty();
        RuleFor(command => command.Bands).NotNull();
        RuleFor(command => command.Bands.Count).LessThanOrEqualTo(LabTest.MaxRangeBands).When(command => command.Bands is not null);
        RuleForEach(command => command.Bands).ChildRules(band =>
        {
            band.RuleFor(item => item.Sex).NotEmpty().IsEnumName(typeof(GenderRestriction), caseSensitive: false);
            band.RuleFor(item => item.AgeUnit).NotEmpty().IsEnumName(typeof(RangeAgeUnit), caseSensitive: false);
            band.RuleFor(item => item.AgeFrom).GreaterThanOrEqualTo(0);
            band.RuleFor(item => item.AgeTo).GreaterThan(0);
            band.RuleFor(item => item.TextRange).MaximumLength(RangeBand.MaxTextRangeLength);
        });
    }
}

public sealed class ReplaceRangeBandsHandler(ILabTestRepository tests, IPatientConditionRepository conditions, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<ReplaceRangeBandsCommand>
{
    public async Task Handle(ReplaceRangeBandsCommand command, CancellationToken cancellationToken)
    {
        var test = await tests.GetForUpdateAsync(command.TestId, cancellationToken)
            ?? throw new DomainException(LabTestCodes.NotFound, "Test not found.", DomainFault.NotFound);

        var selected = command.Bands.Where(band => band.PatientConditionId is not null).Select(band => band.PatientConditionId!.Value).ToList();
        var current = test.RangeBands.Where(band => band.PatientConditionId is not null).Select(band => band.PatientConditionId!.Value).ToList();
        await CatalogueAReferences.RequirePatientConditionsAsync(conditions, selected, current, cancellationToken);

        var specs = command.Bands.Select(band => new RangeBandSpec(
            Enum.Parse<GenderRestriction>(band.Sex, ignoreCase: true),
            band.AgeFrom,
            band.AgeTo,
            Enum.Parse<RangeAgeUnit>(band.AgeUnit, ignoreCase: true),
            band.PatientConditionId,
            band.Low,
            band.High,
            band.PanicLow,
            band.PanicHigh,
            band.ReportableLow,
            band.ReportableHigh,
            band.TextRange)).ToList();
        test.ReplaceRangeBands(specs, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record ReplaceListValuesCommand(Guid TestId, IReadOnlyList<ListValueItem> Values) : ICommand;

public sealed class ReplaceListValuesValidator : AbstractValidator<ReplaceListValuesCommand>
{
    public ReplaceListValuesValidator()
    {
        RuleFor(command => command.TestId).NotEmpty();
        RuleFor(command => command.Values).NotNull();
        RuleFor(command => command.Values.Count).LessThanOrEqualTo(LabTest.MaxListValues).When(command => command.Values is not null);
        RuleForEach(command => command.Values).ChildRules(value =>
        {
            value.RuleFor(item => item.Value).NotEmpty().MaximumLength(ListValue.MaxValueLength);
            value.RuleFor(item => item.Colour).MaximumLength(7);
        });
    }
}

public sealed class ReplaceListValuesHandler(ILabTestRepository tests, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<ReplaceListValuesCommand>
{
    public async Task Handle(ReplaceListValuesCommand command, CancellationToken cancellationToken)
    {
        var test = await tests.GetForUpdateAsync(command.TestId, cancellationToken)
            ?? throw new DomainException(LabTestCodes.NotFound, "Test not found.", DomainFault.NotFound);

        var specs = command.Values.Select(value => new ListValueSpec(value.Value, value.IsNormal, value.IsCritical, value.IsDefault, value.IsPlaceholder, value.Colour)).ToList();
        test.ReplaceListValues(specs, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>T17: an empty expression clears the formula.</summary>
[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record SetLabTestFormulaCommand(Guid TestId, string? Expression) : ICommand;

public sealed class SetLabTestFormulaValidator : AbstractValidator<SetLabTestFormulaCommand>
{
    public SetLabTestFormulaValidator()
    {
        RuleFor(command => command.TestId).NotEmpty();
        RuleFor(command => command.Expression).MaximumLength(LabTest.MaxFormulaLength);
    }
}

public sealed class SetLabTestFormulaHandler(ILabTestRepository tests, IFormulaEvaluator evaluator, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<SetLabTestFormulaCommand>
{
    public async Task Handle(SetLabTestFormulaCommand command, CancellationToken cancellationToken)
    {
        var test = await tests.GetForUpdateAsync(command.TestId, cancellationToken)
            ?? throw new DomainException(LabTestCodes.NotFound, "Test not found.", DomainFault.NotFound);

        if (string.IsNullOrWhiteSpace(command.Expression))
        {
            test.SetFormula(null, [], clock.UtcNow);
        }
        else
        {
            if (!LabTest.AllowsFormula(test.Type))
            {
                throw new DomainException(LabTestCodes.FormulaNotAllowed, $"A {test.Type} test has no formula.", DomainFault.Validation);
            }

            var verdict = await LabTestFormulaRules.CheckAsync(evaluator, tests, command.Expression, test.Id, cancellationToken);
            if (!verdict.Valid)
            {
                throw new DomainException(verdict.Code!, verdict.Message!, DomainFault.Validation);
            }

            test.SetFormula(verdict.Normalized, verdict.References, clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Edit)]
public sealed record ReplaceParagraphsCommand(Guid TestId, IReadOnlyList<ParagraphItem> Paragraphs) : ICommand;

public sealed class ReplaceParagraphsValidator : AbstractValidator<ReplaceParagraphsCommand>
{
    public ReplaceParagraphsValidator()
    {
        RuleFor(command => command.TestId).NotEmpty();
        RuleFor(command => command.Paragraphs).NotNull();
        RuleFor(command => command.Paragraphs.Count).LessThanOrEqualTo(LabTest.MaxParagraphs).When(command => command.Paragraphs is not null);
        RuleForEach(command => command.Paragraphs).ChildRules(paragraph =>
        {
            paragraph.RuleFor(item => item.Title).NotEmpty().MaximumLength(TestParagraph.MaxTitleLength);
            paragraph.RuleFor(item => item.Text).MaximumLength(TestParagraph.MaxTextLength);
        });
    }
}

public sealed class ReplaceParagraphsHandler(ILabTestRepository tests, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<ReplaceParagraphsCommand>
{
    public async Task Handle(ReplaceParagraphsCommand command, CancellationToken cancellationToken)
    {
        var test = await tests.GetForUpdateAsync(command.TestId, cancellationToken)
            ?? throw new DomainException(LabTestCodes.NotFound, "Test not found.", DomainFault.NotFound);

        test.ReplaceParagraphs(command.Paragraphs.Select(paragraph => new ParagraphSpec(paragraph.Title, paragraph.Text)).ToList(), clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Manage)]
public sealed record SetLabTestActiveCommand(Guid TestId, bool Active) : ICommand;

public sealed class SetLabTestActiveValidator : AbstractValidator<SetLabTestActiveCommand>
{
    public SetLabTestActiveValidator() => RuleFor(command => command.TestId).NotEmpty();
}

public sealed class SetLabTestActiveHandler(ILabTestRepository tests, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<SetLabTestActiveCommand>
{
    public async Task Handle(SetLabTestActiveCommand command, CancellationToken cancellationToken)
    {
        var test = await tests.GetForUpdateAsync(command.TestId, cancellationToken)
            ?? throw new DomainException(LabTestCodes.NotFound, "Test not found.", DomainFault.NotFound);

        if (command.Active)
        {
            test.Reactivate(clock.UtcNow);
        }
        else
        {
            test.Deactivate(clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

internal static class LabTestDefinitions
{
    public static LabTestDefinition From(
        string nameEn,
        string nameAr,
        string? reportName,
        string? loinc,
        string? cpt,
        Guid clinicalGroupId,
        string type,
        Guid? sampleTypeId,
        Guid? sampleConditionId,
        Guid? unitId,
        string gender,
        string? methodology,
        int decimals,
        bool isHiddenHelper,
        bool printReference,
        int labelCount,
        string? prefix) =>
        new(
            new LocalizedText(nameEn, nameAr),
            reportName,
            loinc,
            cpt,
            clinicalGroupId,
            Enum.Parse<LabTestType>(type, ignoreCase: true),
            sampleTypeId,
            sampleConditionId,
            unitId,
            Enum.Parse<GenderRestriction>(gender, ignoreCase: true),
            methodology,
            decimals,
            isHiddenHelper,
            printReference,
            labelCount,
            prefix);
}
