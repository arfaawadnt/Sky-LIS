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
using Platform.Domain.Samples;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Results.Queries;

// CP-19 Results by accession, CP-20 Results by sample, and the query panel of CP-19 (intake §14). Queries carry a
// policy like commands (D-04) and read projections only; results of groups the user cannot see are left out.

/// <summary>Exactly one of <paramref name="Accession"/>, <paramref name="VisitId"/> or <paramref name="SampleNumber"/>.</summary>
[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.View)]
public sealed record GetResultCaseQuery(string? Accession, Guid? VisitId, string? SampleNumber) : IQuery<ResultCaseView>;

public sealed class GetResultCaseValidator : AbstractValidator<GetResultCaseQuery>
{
    public GetResultCaseValidator()
    {
        RuleFor(query => query)
            .Must(query => (string.IsNullOrWhiteSpace(query.Accession) ? 0 : 1) + (query.VisitId is null ? 0 : 1) + (string.IsNullOrWhiteSpace(query.SampleNumber) ? 0 : 1) == 1)
            .WithName("Accession")
            .WithMessage("Give an accession, a visit id or a sample number.");
        RuleFor(query => query.Accession).MaximumLength(Result.MaxAccessionLength);
        RuleFor(query => query.SampleNumber).MaximumLength(Sample.MaxSampleNumberLength);
        RuleFor(query => query.VisitId).NotEqual(Guid.Empty).When(query => query.VisitId is not null);
    }
}

public sealed class GetResultCaseHandler(
    IResultRepository results,
    ISampleStatusLookup samples,
    ICatalogueLookup catalogue,
    IPatientLookup patients,
    IUserDirectory users,
    IUserPrivileges privileges,
    IVisitLookup visits,
    IResultReferenceLookup references,
    IDoctorLookup doctors,
    ITenantSettings settings) : IQueryHandler<GetResultCaseQuery, ResultCaseView>
{
    public async Task<ResultCaseView> Handle(GetResultCaseQuery query, CancellationToken cancellationToken)
    {
        Guid visitId;
        Guid? tubeId = null;
        string? scanned = null;
        if (query.VisitId is { } id)
        {
            visitId = id;
        }
        else if (!string.IsNullOrWhiteSpace(query.Accession))
        {
            visitId = await results.FindVisitByAccessionAsync(query.Accession.Trim(), cancellationToken)
                ?? (await visits.GetByAccessionAsync(query.Accession.Trim(), cancellationToken))?.Id
                ?? throw ResultsNotFound.Visit();
        }
        else
        {
            var sample = await samples.BySampleNumberAsync(query.SampleNumber!, cancellationToken) ?? throw ResultsNotFound.Sample();
            tubeId = sample.TubeId;
            scanned = sample.SampleNumber;
            visitId = await results.FindVisitByTubeAsync(sample.TubeId, cancellationToken) ?? sample.VisitId;
        }

        var all = await results.ReadByVisitAsync(visitId, cancellationToken);
        var scoped = tubeId is { } tube ? all.Where(result => result.TubeId == tube).ToList() : [.. all];
        if (scoped.Count == 0)
        {
            throw ResultsNotFound.Visit();
        }

        var visibility = ResultRights.VisibilityOf(privileges);
        var visible = scoped.Where(result => visibility.CanSee(result.ClinicalGroupId, result.BranchId)).ToList();
        var hiddenUnits = scoped.Select(result => result.ReportUnitKey).Distinct().Count() - visible.Select(result => result.ReportUnitKey).Distinct().Count();
        var context = await new ResultReadContextLoader(catalogue, samples, users).LoadAsync(visible, cancellationToken);
        var header = await new ResultHeaderLoader(patients, visits, references, doctors).LoadAsync(scoped[0], cancellationToken);
        return new ResultCaseView(
            header.VisitView,
            header.PatientView,
            ResultViews.Units(visible, context, privileges),
            hiddenUnits,
            scanned,
            await ResultSettings.ReadAsync(settings, cancellationToken));
    }
}

/// <summary>The query panel of CP-19 (LDM §5.2): <paramref name="Status"/> and <paramref name="SampleStatus"/> are status names; <paramref name="PatientName"/> accepts <c>%</c>.</summary>
[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.View)]
public sealed record SearchResultVisitsQuery(
    string? Accession,
    string? PatientNumber,
    string? PatientName,
    string? Status,
    string? SampleStatus,
    Guid? AnalyserId,
    bool NotPrinted,
    bool NotVerified,
    bool Delta,
    DateTimeOffset? From,
    DateTimeOffset? To,
    bool BranchOnly,
    int? Page,
    int? PageSize) : IQuery<Paged<ResultVisitListItem>>;

public sealed class SearchResultVisitsValidator : AbstractValidator<SearchResultVisitsQuery>
{
    public SearchResultVisitsValidator()
    {
        RuleFor(query => query.Accession).MaximumLength(Result.MaxAccessionLength);
        RuleFor(query => query.PatientNumber).MaximumLength(40);
        RuleFor(query => query.PatientName).MaximumLength(200);
        RuleFor(query => query.Status).IsEnumName(typeof(ResultStatus), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Status));
        RuleFor(query => query.SampleStatus).IsEnumName(typeof(SampleStatus), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.SampleStatus));
        RuleFor(query => query.To).GreaterThan(query => query.From).When(query => query.From is not null && query.To is not null);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
    }
}

public sealed class SearchResultVisitsHandler(IResultRepository results, IPatientLookup patients, IUserPrivileges privileges, ICurrentUser user) : IQueryHandler<SearchResultVisitsQuery, Paged<ResultVisitListItem>>
{
    public async Task<Paged<ResultVisitListItem>> Handle(SearchResultVisitsQuery query, CancellationToken cancellationToken)
    {
        var page = PageRequest.Normalized(query.Page, query.PageSize);
        var visibility = ResultRights.VisibilityOf(privileges);
        if (visibility.IsEmpty)
        {
            return Paged<ResultVisitListItem>.Empty(page);
        }

        var filter = new ResultVisitFilter(
            Clean(query.Accession),
            Clean(query.PatientNumber),
            Clean(query.PatientName),
            string.IsNullOrEmpty(query.Status) ? null : Enum.Parse<ResultStatus>(query.Status, ignoreCase: true),
            string.IsNullOrEmpty(query.SampleStatus) ? null : Enum.Parse<SampleStatus>(query.SampleStatus, ignoreCase: true).ToString(),
            query.AnalyserId,
            query.NotPrinted,
            query.NotVerified,
            query.Delta,
            query.From,
            query.To,
            query.BranchOnly ? user.BranchId : null);
        var rows = await results.SearchVisitsAsync(filter, visibility, page, cancellationToken);
        var names = await patients.GetManyAsync([.. rows.Items.Select(row => row.PatientId).Distinct()], cancellationToken);
        var items = rows.Items.Select(row =>
        {
            var patient = names.GetValueOrDefault(row.PatientId);
            return new ResultVisitListItem(row.VisitId, row.Accession, row.RegisteredAtUtc, row.PatientId, patient?.Number, patient?.FullName, patient?.NameAr, row.BranchId, row.Status.ToString(), row.Results, row.Units, row.HasPanic, row.HasDelta, row.AnyPrinted);
        }).ToList();
        return new Paged<ResultVisitListItem>(items, rows.Page, rows.PageSize, rows.TotalCount);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.View)]
public sealed record GetResultLogQuery(Guid ResultId) : IQuery<IReadOnlyList<ResultLogItem>>;

public sealed class GetResultLogHandler(IResultRepository results, IUserPrivileges privileges) : IQueryHandler<GetResultLogQuery, IReadOnlyList<ResultLogItem>>
{
    public async Task<IReadOnlyList<ResultLogItem>> Handle(GetResultLogQuery query, CancellationToken cancellationToken)
    {
        var result = await VisibleResult.ReadAsync(results, privileges, query.ResultId, cancellationToken);
        var entries = await results.ReadLogAsync(result.Id, cancellationToken);
        return [.. entries.OrderBy(entry => entry.Sequence).Select(ResultViews.Log)];
    }
}

/// <summary>"Last result" (LDM §5.3): the patient's other results of the same test, newest first (at most 20).</summary>
[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.View)]
public sealed record GetPreviousResultsQuery(Guid ResultId) : IQuery<IReadOnlyList<PreviousResultItem>>;

public sealed class GetPreviousResultsHandler(IResultRepository results, IUserPrivileges privileges) : IQueryHandler<GetPreviousResultsQuery, IReadOnlyList<PreviousResultItem>>
{
    public const int MaxRows = 20;

    public async Task<IReadOnlyList<PreviousResultItem>> Handle(GetPreviousResultsQuery query, CancellationToken cancellationToken)
    {
        var result = await VisibleResult.ReadAsync(results, privileges, query.ResultId, cancellationToken);
        var previous = await results.ReadPreviousAsync(result.PatientId, result.TestId, result.Id, MaxRows, cancellationToken);
        var visibility = ResultRights.VisibilityOf(privileges);
        return [.. previous
            .Where(row => visibility.CanSee(row.ClinicalGroupId, row.BranchId))
            .Select(row => new PreviousResultItem(row.Id, row.VisitId, row.Accession, row.RegisteredAtUtc, row.Status.ToString(), row.NumericValue, row.DescribeValue(), row.Unit, row.Flag?.ToString(), row.VerifiedAtUtc))];
    }
}

/// <summary>A result the user may see, or RES-404 (a result of a group the user cannot see does not exist for them).</summary>
internal static class VisibleResult
{
    public static async Task<Result> ReadAsync(IResultRepository results, IUserPrivileges privileges, Guid resultId, CancellationToken cancellationToken)
    {
        var result = await results.ReadAsync(resultId, cancellationToken) ?? throw ResultsNotFound.Result();
        if (!ResultRights.VisibilityOf(privileges).CanSee(result.ClinicalGroupId, result.BranchId))
        {
            throw new DomainException(ResultsCodes.ResultNotFound, "Result not found.", DomainFault.NotFound);
        }

        return result;
    }
}
