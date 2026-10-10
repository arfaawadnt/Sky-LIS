using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Domain.Authorization;
using Platform.Domain.Catalogue;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.Catalogue.Commands;

/// <summary>
/// T3 Deactivate / T4 Reactivate of any Catalogue B record (<c>catalogue.manage</c>, intake LIS-INT-CTB-001 §7). The
/// aggregate's own method keeps the transition idempotent and raises the list's event once.
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Catalogue, PermissionAction.Manage)]
public sealed record SetCatalogueBRecordActiveCommand(CatalogueBList List, Guid RecordId, bool Active) : ICommand;

public sealed class SetCatalogueBRecordActiveValidator : AbstractValidator<SetCatalogueBRecordActiveCommand>
{
    public SetCatalogueBRecordActiveValidator()
    {
        RuleFor(command => command.List).IsInEnum();
        RuleFor(command => command.RecordId).NotEmpty();
    }
}

public sealed class SetCatalogueBRecordActiveHandler(ICatalogueBRecords records, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<SetCatalogueBRecordActiveCommand>
{
    public async Task Handle(SetCatalogueBRecordActiveCommand command, CancellationToken cancellationToken)
    {
        var record = await records.GetForUpdateAsync(command.List, command.RecordId, cancellationToken)
            ?? throw CatalogueBRules.NotFound(NotFoundCode(command.List), "Record");

        if (command.Active)
        {
            record.Reactivate(clock.UtcNow);
        }
        else
        {
            record.Deactivate(clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public static string NotFoundCode(CatalogueBList list) => list switch
    {
        CatalogueBList.Preparations => CatalogueBCodes.PreparationNotFound,
        CatalogueBList.CommentTemplates => CatalogueBCodes.CommentTemplateNotFound,
        CatalogueBList.Doctors => CatalogueBCodes.DoctorNotFound,
        CatalogueBList.Diagnoses => CatalogueBCodes.DiagnosisNotFound,
        CatalogueBList.SendingLabs => CatalogueBCodes.SendingLabNotFound,
        CatalogueBList.ShortcutSets => CatalogueBCodes.ShortcutSetNotFound,
        CatalogueBList.TestRules => CatalogueBCodes.TestRuleNotFound,
        CatalogueBList.Reasons => CatalogueBCodes.ReasonNotFound,
        CatalogueBList.ReportTemplates => CatalogueBCodes.ReportTemplateNotFound,
        CatalogueBList.DrugTypes => CatalogueBCodes.DrugTypeNotFound,
        CatalogueBList.CommercialNames => CatalogueBCodes.CommercialNameNotFound,
        CatalogueBList.Antibiotics => CatalogueBCodes.AntibioticNotFound,
        CatalogueBList.Organisms => CatalogueBCodes.OrganismNotFound,
        _ => CatalogueBCodes.SensitivityValueNotFound,
    };
}
