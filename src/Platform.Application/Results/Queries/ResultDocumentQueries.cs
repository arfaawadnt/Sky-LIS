using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Catalogue;
using Platform.Application.Organization;
using Platform.Application.Patients;
using Platform.Application.Registration;
using Platform.Application.Samples;
using Platform.Contracts.Common;
using Platform.Contracts.Results;
using Platform.Domain.Authorization;
using Platform.Domain.Results;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Results.Queries;

// Documents of the module (MODULES.md §1): the laboratory report of a report unit (preview and print). The data only;
// the SPA renders it with the print kit. The worklist document lives with the worklist query.

/// <summary>Everything a report query needs, grouped so handlers stay readable.</summary>
public sealed record ResultReportPorts(
    ICatalogueLookup Catalogue,
    ISampleStatusLookup Samples,
    IUserDirectory Users,
    IPatientLookup Patients,
    IVisitLookup Visits,
    IResultReferenceLookup References,
    IDoctorLookup Doctors,
    IReportTemplateLookup Templates,
    ITenantSettings Settings)
{
    internal ResultReportBuilder Builder() =>
        new(new ResultReadContextLoader(Catalogue, Samples, Users), new ResultHeaderLoader(Patients, Visits, References, Doctors), Catalogue, Templates, References, Settings);
}

/// <summary>
/// The report of one unit. <paramref name="Preview"/>: refused while a value is missing (RES-022, LDM §5.7 "There are
/// other results have no value") unless the user holds "preview without verify". Print copy: only when the unit is
/// printable (RES-021, BR-09).
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.View)]
public sealed record GetReportDocumentQuery(string ReportUnitKey, bool Preview) : IQuery<LaboratoryReportDocument>;

public sealed class GetReportDocumentValidator : AbstractValidator<GetReportDocumentQuery>
{
    public GetReportDocumentValidator() => RuleFor(query => query.ReportUnitKey).Must(ReportUnits.IsWellFormed).WithMessage("A report unit key is required.");
}

public sealed class GetReportDocumentHandler(
    IResultRepository results,
    ICatalogueLookup catalogue,
    ISampleStatusLookup samples,
    IUserDirectory users,
    IPatientLookup patients,
    IVisitLookup visits,
    IResultReferenceLookup references,
    IDoctorLookup doctors,
    IReportTemplateLookup templates,
    ITenantSettings settings,
    IUserPrivileges privileges,
    ICurrentUser user,
    IClock clock) : IQueryHandler<GetReportDocumentQuery, LaboratoryReportDocument>
{
    public async Task<LaboratoryReportDocument> Handle(GetReportDocumentQuery query, CancellationToken cancellationToken)
    {
        var unit = await results.ReadByUnitsAsync([query.ReportUnitKey], cancellationToken);
        var live = ReportGuards.Visible(unit, privileges);
        var rights = ResultRights.For(privileges, live[0].ClinicalGroupId, live[0].BranchId);
        if (query.Preview)
        {
            if (live.Any(result => !result.HasValue) && !rights.CanPreviewWithoutVerify)
            {
                throw new DomainException(ResultsCodes.PreviewIncomplete, "There are other results that have no value: complete them before the preview.", DomainFault.Conflict);
            }
        }
        else
        {
            var printUnverified = await settings.GetAsync(SettingKeys.PrintUnverified, cancellationToken);
            var actor = new ResultActor(user.UserId ?? Guid.Empty, user.DisplayName, rights);
            foreach (var result in live)
            {
                result.EnsurePrintable(actor, printUnverified);
            }
        }

        var printedBy = await ResultsActor.NameOfAsync(user, users, cancellationToken);
        var ports = new ResultReportPorts(catalogue, samples, users, patients, visits, references, doctors, templates, settings);
        return await ports.Builder().BuildAsync(query.ReportUnitKey, unit, query.Preview, clock.UtcNow, printedBy, cancellationToken);
    }
}

internal static class ReportGuards
{
    /// <summary>The live results of a unit the user may see, or RES-405.</summary>
    public static IReadOnlyList<Result> Visible(IReadOnlyList<Result> unit, IUserPrivileges privileges)
    {
        var visibility = ResultRights.VisibilityOf(privileges);
        var live = unit.Where(result => result.IsLive).ToList();
        if (live.Count == 0 || !live.Any(result => visibility.CanSee(result.ClinicalGroupId, result.BranchId)))
        {
            throw ResultsNotFound.Unit();
        }

        return live;
    }
}
