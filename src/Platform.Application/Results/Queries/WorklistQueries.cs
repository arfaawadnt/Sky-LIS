using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Catalogue;
using Platform.Application.Organization;
using Platform.Application.Patients;
using Platform.Application.Samples;
using Platform.Contracts.Common;
using Platform.Contracts.Results;
using Platform.Domain.Authorization;
using Platform.Domain.Patients;
using Platform.Domain.Results;
using Platform.Domain.Samples;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.Results.Queries;

// CP-18 Worklist: one row per test with the BR-13 delay colours; only results of groups the user may see.

/// <summary>Worklist filters (LDM §6); <paramref name="Statuses"/> are status names (the status boxes); <paramref name="From"/>/<paramref name="To"/> bound the collection time.</summary>
[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.View)]
public sealed record GetWorklistQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? ClinicalGroupId,
    Guid? TestId,
    Guid? AnalyserId,
    string? SampleStatus,
    Guid? PayerId,
    IReadOnlyList<string>? Statuses,
    bool DelayedOnly,
    string? Search,
    bool BranchOnly,
    int? Page,
    int? PageSize,
    string? Sort = null,
    string? Direction = null) : IQuery<WorklistPage>;

public sealed class GetWorklistValidator : AbstractValidator<GetWorklistQuery>
{
    public GetWorklistValidator()
    {
        RuleFor(query => query.To).GreaterThan(query => query.From).When(query => query.From is not null && query.To is not null);
        RuleFor(query => query.SampleStatus).IsEnumName(typeof(SampleStatus), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.SampleStatus));
        RuleForEach(query => query.Statuses).IsEnumName(typeof(ResultStatus), caseSensitive: false);
        RuleFor(query => query.Search).MaximumLength(60);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
        RuleFor(query => query.Sort).SortKeyOf(ResultsSortKeys.Worklist);
        RuleFor(query => query.Direction).SortDirection();
    }
}

public sealed class GetWorklistHandler(IResultRepository results, IResultAnalyserDirectory analysers, ITenantSettings settings, IUserPrivileges privileges, ICurrentUser user, IClock clock) : IQueryHandler<GetWorklistQuery, WorklistPage>
{
    public async Task<WorklistPage> Handle(GetWorklistQuery query, CancellationToken cancellationToken)
    {
        var page = PageRequest.Normalized(query.Page, query.PageSize);
        var warn = await settings.GetAsync(SettingKeys.DelayWarnPercent, cancellationToken);
        var late = await settings.GetAsync(SettingKeys.DelayLatePercent, cancellationToken);
        var visibility = ResultRights.VisibilityOf(privileges);
        if (visibility.IsEmpty)
        {
            return new WorklistPage([], page.Page, page.PageSize, 0, new WorklistCounters(0, 0, 0, 0), warn, late);
        }

        var filter = await Worklists.FilterAsync(query, analysers, user, cancellationToken);
        var now = clock.UtcNow;
        var slice = await results.WorklistAsync(filter, visibility, page, now, cancellationToken);
        var items = slice.Rows.Select(row => Worklists.Item(row, now, warn, late)).ToList();
        return new WorklistPage(items, page.Page, page.PageSize, slice.TotalCount, new WorklistCounters(slice.Requests, slice.TotalCount, slice.Samples, slice.Delayed), warn, late);
    }
}

/// <summary>The bench list (worklist document) of the filter: at most 2 000 rows.</summary>
[RequirePermissionPolicy(PermissionCatalog.Results, PermissionAction.View)]
public sealed record GetWorklistDocumentQuery(GetWorklistQuery Filter) : IQuery<WorklistDocument>;

public sealed class GetWorklistDocumentValidator : AbstractValidator<GetWorklistDocumentQuery>
{
    public GetWorklistDocumentValidator() => RuleFor(query => query.Filter).NotNull().SetValidator(new GetWorklistValidator());
}

public sealed class GetWorklistDocumentHandler(
    IResultRepository results,
    IResultAnalyserDirectory analysers,
    IResultReferenceLookup references,
    ICatalogueLookup catalogue,
    ITenantSettings settings,
    IUserPrivileges privileges,
    IUserDirectory directory,
    ICurrentUser user,
    IClock clock) : IQueryHandler<GetWorklistDocumentQuery, WorklistDocument>
{
    public const int MaxRows = 2000;

    public async Task<WorklistDocument> Handle(GetWorklistDocumentQuery query, CancellationToken cancellationToken)
    {
        var warn = await settings.GetAsync(SettingKeys.DelayWarnPercent, cancellationToken);
        var late = await settings.GetAsync(SettingKeys.DelayLatePercent, cancellationToken);
        var visibility = ResultRights.VisibilityOf(privileges);
        var filter = await Worklists.FilterAsync(query.Filter, analysers, user, cancellationToken);
        var now = clock.UtcNow;
        var rows = new List<WorklistItem>();
        long total = 0;
        for (var pageNumber = 1; !visibility.IsEmpty && rows.Count < MaxRows; pageNumber++)
        {
            var slice = await results.WorklistAsync(filter, visibility, PageRequest.Normalized(pageNumber, PageRequest.MaxPageSize), now, cancellationToken);
            total = slice.TotalCount;
            rows.AddRange(slice.Rows.Select(row => Worklists.Item(row, now, warn, late)));
            if (slice.Rows.Count < PageRequest.MaxPageSize)
            {
                break;
            }
        }

        var group = query.Filter.ClinicalGroupId is { } groupId ? await catalogue.GetClinicalGroupAsync(groupId, cancellationToken) : null;
        var letterhead = await references.LetterheadAsync(user.BranchId ?? Guid.Empty, cancellationToken);
        var printedBy = await ResultsActor.NameOfAsync(user, directory, cancellationToken);
        return new WorklistDocument(letterhead, now, printedBy, query.Filter.From, query.Filter.To, group?.NameEn, group?.NameAr, [.. rows.Take(MaxRows)], total > MaxRows);
    }
}

/// <summary>Worklist filter and row mapping (BR-13 delay and colour).</summary>
internal static class Worklists
{
    public static async Task<WorklistFilter> FilterAsync(GetWorklistQuery query, IResultAnalyserDirectory analysers, ICurrentUser user, CancellationToken cancellationToken)
    {
        var analyserTests = query.AnalyserId is { } analyserId ? await analysers.MappedTestIdsAsync(analyserId, cancellationToken) : null;
        return new WorklistFilter(
            query.From,
            query.To,
            query.ClinicalGroupId,
            query.TestId,
            analyserTests,
            string.IsNullOrEmpty(query.SampleStatus) ? null : Enum.Parse<SampleStatus>(query.SampleStatus, ignoreCase: true).ToString(),
            query.PayerId,
            query.Statuses is { Count: > 0 } statuses ? [.. statuses.Select(status => Enum.Parse<ResultStatus>(status, ignoreCase: true))] : null,
            query.DelayedOnly,
            string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            query.BranchOnly ? user.BranchId : null,
            ListSort.Of(query.Sort, query.Direction));
    }

    public static WorklistItem Item(WorklistRow row, DateTimeOffset nowUtc, int warnPercent, int latePercent)
    {
        var done = ResultStatuses.IsSigned(row.Status);
        var end = done ? row.VerifiedAtUtc ?? nowUtc : nowUtc;
        var consumed = TurnaroundClock.ConsumedPercent(row.ClockStartUtc, row.ExpectedAtUtc, end);
        var delay = Math.Max(0L, (long)Math.Floor((end - row.ExpectedAtUtc).TotalSeconds));
        var today = DateOnly.FromDateTime(nowUtc.UtcDateTime);
        int? age = row.DateOfBirth is { } birth && birth <= today ? PatientAge.Between(birth, today).Years : null;
        return new WorklistItem(
            row.ResultId,
            row.VisitId,
            row.Accession,
            row.PatientId,
            row.PatientNumber,
            row.PatientName,
            row.PatientNameAr,
            row.Gender?.ToString(),
            age,
            row.TestId,
            row.TestCode,
            row.TestNameEn,
            row.TestNameAr,
            row.ClinicalGroupId,
            row.GroupNameEn,
            row.GroupNameAr,
            row.Status.ToString(),
            row.TubeId,
            row.SampleNumber,
            row.SampleStatus,
            row.CollectedAtUtc,
            row.ExpectedAtUtc,
            delay,
            consumed,
            TurnaroundClock.State(consumed, done, warnPercent, latePercent).ToString(),
            row.Urgent,
            row.TextValue,
            row.Flag?.ToString(),
            row.PrintCount > 0,
            row.Source.ToString());
    }
}
