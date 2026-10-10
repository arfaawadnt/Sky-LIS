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

/// <summary>T6 Amend: a Verified or Printed result takes a new value with the special right "modify verified result" and a reason; it returns to Complete.</summary>
[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.Edit)]
public sealed record AmendResultCommand(Guid ResultId, decimal? NumericValue, string? TextValue, IReadOnlyList<FormattedParagraphDto>? Paragraphs, CultureEntryDto? Culture, string Reason) : ICommand;

public sealed class AmendResultValidator : AbstractValidator<AmendResultCommand>
{
    public AmendResultValidator()
    {
        RuleFor(command => command.ResultId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(Result.MaxReasonLength);
        RuleFor(command => new ResultValueItem(command.ResultId, command.NumericValue, command.TextValue, command.Paragraphs, command.Culture)).SetValidator(new ResultValueItemValidator());
    }
}

public sealed class AmendResultHandler(
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
    IClock clock) : ICommandHandler<AmendResultCommand>
{
    public async Task Handle(AmendResultCommand command, CancellationToken cancellationToken)
    {
        var actor = ResultsActor.Of(user);
        BranchScope.Require(user);
        var name = await ResultsActor.NameOfAsync(user, directory, cancellationToken);
        var engine = new ResultEntryPorts(results, catalogue, patients, samples, references, evaluator, clock).Engine();
        var value = new ResultValueItem(command.ResultId, command.NumericValue, command.TextValue, command.Paragraphs, command.Culture);
        await engine.AmendAsync(command.ResultId, value, command.Reason, result => ResultRights.Actor(actor, name, privileges, result), actor, name, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
