using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Catalogue;
using Platform.Application.Catalogue.Services;
using Platform.Application.Organization;
using Platform.Application.Patients;
using Platform.Application.Samples;
using Platform.Contracts.Results;
using Platform.Domain.Authorization;
using Platform.Domain.Results;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.Results.Commands;

// T2 Enter (intake LIS-INT-RES-001 §7): one result (PUT /{id}) or the changed rows of a grid (PUT /values, Ctrl+S).
// Validators check shape; the aggregate decides (pitfall P-10). Formulas of the visits touched are recomputed (T2b).

/// <summary>The ports an entry needs, grouped so the handlers stay readable.</summary>
public sealed record ResultEntryPorts(
    IResultRepository Results,
    ICatalogueLookup Catalogue,
    IPatientLookup Patients,
    ISampleStatusLookup Samples,
    IResultReferenceLookup References,
    IFormulaEvaluator Evaluator,
    IClock Clock)
{
    internal ResultEntryEngine Engine() => new(Results, Catalogue, Patients, Samples, References, Evaluator, Clock);
}

[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.Create)]
public sealed record EnterResultValueCommand(Guid ResultId, decimal? NumericValue, string? TextValue, IReadOnlyList<FormattedParagraphDto>? Paragraphs, CultureEntryDto? Culture) : ICommand;

public sealed class EnterResultValueValidator : AbstractValidator<EnterResultValueCommand>
{
    public EnterResultValueValidator()
    {
        RuleFor(command => command.ResultId).NotEmpty();
        RuleFor(command => new ResultValueItem(command.ResultId, command.NumericValue, command.TextValue, command.Paragraphs, command.Culture)).SetValidator(new ResultValueItemValidator());
    }
}

public sealed class EnterResultValueHandler(
    IResultRepository results,
    ICatalogueLookup catalogue,
    IPatientLookup patients,
    ISampleStatusLookup samples,
    IResultReferenceLookup references,
    IFormulaEvaluator evaluator,
    IUserPrivileges privileges,
    IUserDirectory directory,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    IClock clock) : ICommandHandler<EnterResultValueCommand>
{
    public async Task Handle(EnterResultValueCommand command, CancellationToken cancellationToken)
    {
        var actor = ResultsActor.Of(user);
        BranchScope.Require(user);
        var name = await ResultsActor.NameOfAsync(user, directory, cancellationToken);
        var engine = new ResultEntryPorts(results, catalogue, patients, samples, references, evaluator, clock).Engine();
        var item = new ResultValueItem(command.ResultId, command.NumericValue, command.TextValue, command.Paragraphs, command.Culture);
        var outcome = await engine.EnterAsync([item], result => ResultRights.Actor(actor, name, privileges, result), actor, name, cancellationToken);
        if (outcome.FirstRefusal is { } refusal)
        {
            throw refusal;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.Create)]
public sealed record SaveResultValuesCommand(IReadOnlyList<ResultValueItem> Values) : ICommand<BulkResultOutcome>;

public sealed class SaveResultValuesValidator : AbstractValidator<SaveResultValuesCommand>
{
    public const int MaxValues = 500;

    public SaveResultValuesValidator()
    {
        RuleFor(command => command.Values).NotNull().Must(values => values is { Count: >= 1 and <= MaxValues }).WithMessage($"Send 1 to {MaxValues} values.");
        RuleForEach(command => command.Values).SetValidator(new ResultValueItemValidator());
    }
}

/// <summary>Partial saves: every valid row is staged and committed together; each refused row comes back with its code.</summary>
public sealed class SaveResultValuesHandler(
    IResultRepository results,
    ICatalogueLookup catalogue,
    IPatientLookup patients,
    ISampleStatusLookup samples,
    IResultReferenceLookup references,
    IFormulaEvaluator evaluator,
    IUserPrivileges privileges,
    IUserDirectory directory,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    IClock clock) : ICommandHandler<SaveResultValuesCommand, BulkResultOutcome>
{
    public async Task<BulkResultOutcome> Handle(SaveResultValuesCommand command, CancellationToken cancellationToken)
    {
        var actor = ResultsActor.Of(user);
        BranchScope.Require(user);
        var name = await ResultsActor.NameOfAsync(user, directory, cancellationToken);
        var engine = new ResultEntryPorts(results, catalogue, patients, samples, references, evaluator, clock).Engine();
        var outcome = await engine.EnterAsync(command.Values, result => ResultRights.Actor(actor, name, privileges, result), actor, name, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new BulkResultOutcome(outcome.Changed, outcome.Refused);
    }
}

/// <summary>Shape of one value: lengths and counts only (the type rules are the aggregate's).</summary>
public sealed class ResultValueItemValidator : AbstractValidator<ResultValueItem>
{
    public ResultValueItemValidator()
    {
        RuleFor(item => item.ResultId).NotEmpty();
        RuleFor(item => item.TextValue).MaximumLength(Result.MaxTextLength);
        RuleFor(item => item.Paragraphs).Must(paragraphs => paragraphs is null || paragraphs.Count <= FormattedReport.MaxParagraphs).WithMessage($"At most {FormattedReport.MaxParagraphs} paragraphs.");
        RuleForEach(item => item.Paragraphs).ChildRules(paragraph =>
        {
            paragraph.RuleFor(value => value.Title).MaximumLength(FormattedReport.MaxTitleLength);
            paragraph.RuleFor(value => value.Text).MaximumLength(FormattedReport.MaxTextLength);
        });
        When(item => item.Culture is not null, () =>
        {
            RuleFor(item => item.Culture!.Isolates).Must(isolates => isolates is null || isolates.Count <= CultureReport.MaxIsolates).WithMessage($"At most {CultureReport.MaxIsolates} organisms.");
            RuleForEach(item => item.Culture!.Isolates).ChildRules(isolate =>
            {
                isolate.RuleFor(value => value.OrganismId).NotEmpty();
                isolate.RuleFor(value => value.Quantity).MaximumLength(CultureReport.MaxQuantityLength);
                isolate.RuleFor(value => value.Comment).MaximumLength(CultureReport.MaxCommentLength);
                isolate.RuleFor(value => value.Sensitivities).Must(items => items is null || items.Count <= CultureReport.MaxSensitivities).WithMessage($"At most {CultureReport.MaxSensitivities} antibiotics per organism.");
                isolate.RuleForEach(value => value.Sensitivities).ChildRules(sensitivity =>
                {
                    sensitivity.RuleFor(value => value.AntibioticId).NotEmpty();
                    sensitivity.RuleFor(value => value.Interpretation).NotEmpty().MaximumLength(CultureReport.MaxInterpretationLength);
                    sensitivity.RuleFor(value => value.Mic).MaximumLength(CultureReport.MaxMicLength);
                });
            });
        });
    }
}
