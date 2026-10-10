using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Results;
using Platform.Contracts.Common;
using Platform.Domain.Catalogue;
using Platform.Domain.Patients;
using Platform.Domain.Results;
using Platform.Domain.Samples;

namespace Platform.Infrastructure.Persistence.Results;

/// <summary>
/// The write side of results and the projections of the worklist and the visit search. Patients, tests, clinical groups
/// (wave 1) and samples (wave 2) are joined through their entity types (earlier waves, MODULE-PATTERN §3); every read is
/// bounded to the current tenant by the query filter and RLS.
/// </summary>
internal sealed class ResultRepository(AppDbContext context) : Repository<Result>(context), IResultRepository
{
    private IQueryable<Result> Tracked => Set.Include(result => result.Log);

    private IQueryable<Result> Live => Set.AsNoTracking().Where(result => result.Status != ResultStatus.Cancelled);

    public override Task<Result?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        Tracked.FirstOrDefaultAsync(result => result.Id == id, cancellationToken);

    public async Task<IReadOnlySet<(Guid LineId, Guid TestId)>> ExistingLineTestsAsync(IReadOnlyCollection<Guid> lineIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lineIds);
        if (lineIds.Count == 0)
        {
            return new HashSet<(Guid, Guid)>();
        }

        var ids = lineIds.Distinct().ToArray();
        var rows = await Set.AsNoTracking().Where(result => ids.Contains(result.LineId)).Select(result => new { result.LineId, result.TestId }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(row => (row.LineId, row.TestId)).ToHashSet();
    }

    public async Task<IReadOnlyList<Result>> ListForUpdateAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var keys = ids.Distinct().ToArray();
        return keys.Length == 0 ? [] : await Tracked.Where(result => keys.Contains(result.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Result>> ListByVisitForUpdateAsync(Guid visitId, CancellationToken cancellationToken) =>
        await Tracked.Where(result => result.VisitId == visitId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Result>> ListByLineForUpdateAsync(Guid lineId, CancellationToken cancellationToken) =>
        await Tracked.Where(result => result.LineId == lineId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Result>> ListByTubeForUpdateAsync(Guid tubeId, CancellationToken cancellationToken) =>
        await Tracked.Where(result => result.TubeId == tubeId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Result>> ListByPatientForUpdateAsync(Guid patientId, CancellationToken cancellationToken) =>
        await Tracked.Where(result => result.PatientId == patientId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Result>> ListByUnitsForUpdateAsync(IReadOnlyCollection<string> reportUnitKeys, CancellationToken cancellationToken)
    {
        var keys = reportUnitKeys.Distinct(StringComparer.Ordinal).ToArray();
        return keys.Length == 0 ? [] : await Tracked.Where(result => keys.Contains(result.ReportUnitKey)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Result>> ReadByVisitAsync(Guid visitId, CancellationToken cancellationToken) =>
        await Set.AsNoTracking().Where(result => result.VisitId == visitId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Result>> ReadByUnitsAsync(IReadOnlyCollection<string> reportUnitKeys, CancellationToken cancellationToken)
    {
        var keys = reportUnitKeys.Distinct(StringComparer.Ordinal).ToArray();
        return keys.Length == 0 ? [] : await Set.AsNoTracking().Where(result => keys.Contains(result.ReportUnitKey)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<Result?> ReadAsync(Guid id, CancellationToken cancellationToken) =>
        Set.AsNoTracking().FirstOrDefaultAsync(result => result.Id == id, cancellationToken);

    public Task<Guid?> FindVisitByAccessionAsync(string accession, CancellationToken cancellationToken)
    {
        var typed = (accession ?? string.Empty).Trim();
        var upper = typed.ToUpperInvariant();
        return Set.AsNoTracking().Where(result => result.Accession == typed || result.Accession == upper).Select(result => (Guid?)result.VisitId).FirstOrDefaultAsync(cancellationToken);
    }

    public Task<Guid?> FindVisitByTubeAsync(Guid tubeId, CancellationToken cancellationToken) =>
        Set.AsNoTracking().Where(result => result.TubeId == tubeId).Select(result => (Guid?)result.VisitId).FirstOrDefaultAsync(cancellationToken);

    public async Task<PreviousResultValue?> PreviousVerifiedAsync(Guid patientId, Guid testId, Guid excludingVisitId, DateTimeOffset? sinceUtc, CancellationToken cancellationToken)
    {
        var query = Set.AsNoTracking().Where(result =>
            result.PatientId == patientId
            && result.TestId == testId
            && result.VisitId != excludingVisitId
            && (result.Status == ResultStatus.Verified || result.Status == ResultStatus.Printed)
            && result.NumericValue != null
            && result.VerifiedAtUtc != null);
        if (sinceUtc is { } since)
        {
            query = query.Where(result => result.VerifiedAtUtc >= since);
        }

        var row = await query.OrderByDescending(result => result.VerifiedAtUtc)
            .Select(result => new { result.Id, result.NumericValue, result.VerifiedAtUtc })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : new PreviousResultValue(row.Id, row.NumericValue!.Value, row.VerifiedAtUtc!.Value);
    }

    public async Task<IReadOnlyList<Result>> ReadPreviousAsync(Guid patientId, Guid testId, Guid excludingResultId, int take, CancellationToken cancellationToken) =>
        await Live.Where(result => result.PatientId == patientId && result.TestId == testId && result.Id != excludingResultId)
            .OrderByDescending(result => result.RegisteredAtUtc)
            .ThenByDescending(result => result.Id)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<ResultLogEntry>> ReadLogAsync(Guid resultId, CancellationToken cancellationToken) =>
        await Context.Set<ResultLogEntry>().AsNoTracking()
            .Where(entry => entry.ResultId == resultId)
            .OrderBy(entry => entry.AtUtc)
            .ThenBy(entry => entry.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<WorklistSlice> WorklistAsync(WorklistFilter filter, ResultVisibility visibility, PageRequest page, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var results = ResultVisibilityFilter.Apply(Live, visibility);
        if (filter.ClinicalGroupId is { } groupId)
        {
            results = results.Where(result => result.ClinicalGroupId == groupId);
        }

        if (filter.TestId is { } testId)
        {
            results = results.Where(result => result.TestId == testId);
        }

        if (filter.AnalyserTestIds is { } analyserTests)
        {
            var mapped = analyserTests.Distinct().ToArray();
            results = results.Where(result => mapped.Contains(result.TestId));
        }

        if (filter.PayerId is { } payerId)
        {
            results = results.Where(result => result.PayerId == payerId);
        }

        if (filter.Statuses is { Count: > 0 } statuses)
        {
            var wanted = statuses.Distinct().ToArray();
            results = results.Where(result => wanted.Contains(result.Status));
        }

        if (filter.DelayedOnly)
        {
            results = results.Where(result => result.ExpectedAtUtc < nowUtc && (result.Status == ResultStatus.Ordered || result.Status == ResultStatus.Complete || result.Status == ResultStatus.Reviewed));
        }

        if (filter.BranchId is { } branchId)
        {
            results = results.Where(result => result.BranchId == branchId);
        }

        var samples = Context.Set<Sample>().AsNoTracking().Where(sample => sample.Status != SampleStatus.Replaced);
        var patients = Context.Set<Patient>().AsNoTracking();
        var joined =
            from result in results
            from sample in samples.Where(sample => result.TubeId != null && sample.TubeId == result.TubeId).DefaultIfEmpty()
            select new { Result = result, Sample = sample };

        if (filter.CollectedFromUtc is { } from)
        {
            joined = joined.Where(row => row.Sample != null && row.Sample.CollectedAtUtc >= from);
        }

        if (filter.CollectedToUtc is { } to)
        {
            joined = joined.Where(row => row.Sample != null && row.Sample.CollectedAtUtc < to);
        }

        if (!string.IsNullOrEmpty(filter.SampleStatus) && Enum.TryParse<SampleStatus>(filter.SampleStatus, ignoreCase: true, out var sampleStatus))
        {
            joined = joined.Where(row => row.Sample != null && row.Sample.Status == sampleStatus);
        }

        if (filter.Search is { Length: > 0 } search)
        {
            var term = search.ToUpperInvariant();
            joined = joined.Where(row =>
                row.Result.Accession.StartsWith(term)
                || (row.Sample != null && row.Sample.SampleNumber.StartsWith(term))
                || patients.Any(patient => patient.Id == row.Result.PatientId && patient.Number.StartsWith(term)));
        }

        var total = await joined.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var requests = await joined.Select(row => row.Result.VisitId).Distinct().LongCountAsync(cancellationToken).ConfigureAwait(false);
        var tubes = await joined.Where(row => row.Result.TubeId != null).Select(row => row.Result.TubeId).Distinct().LongCountAsync(cancellationToken).ConfigureAwait(false);
        var delayed = await joined.LongCountAsync(row => row.Result.ExpectedAtUtc < nowUtc && (row.Result.Status == ResultStatus.Ordered || row.Result.Status == ResultStatus.Complete || row.Result.Status == ResultStatus.Reviewed), cancellationToken).ConfigureAwait(false);

        var tests = Context.Set<LabTest>().AsNoTracking();
        var groups = Context.Set<ClinicalGroup>().AsNoTracking();
        var rows = await (
            from row in joined
                .OrderByDescending(row => row.Result.Urgent)
                .ThenBy(row => row.Result.ExpectedAtUtc)
                .ThenBy(row => row.Result.Accession)
                .ThenBy(row => row.Result.Id)
                .Skip(page.Skip)
                .Take(page.PageSize)
            from patient in patients.Where(patient => patient.Id == row.Result.PatientId).DefaultIfEmpty()
            from test in tests.Where(test => test.Id == row.Result.TestId).DefaultIfEmpty()
            from grp in groups.Where(grp => grp.Id == row.Result.ClinicalGroupId).DefaultIfEmpty()
            select new
            {
                row.Result.Id,
                row.Result.VisitId,
                row.Result.Accession,
                row.Result.PatientId,
                PatientNumber = patient == null ? null : patient.Number,
                PatientName = patient == null ? null : patient.FullName,
                PatientNameAr = patient == null ? null : patient.NameAr,
                Gender = patient == null ? (PatientGender?)null : patient.Gender,
                DateOfBirth = patient == null ? null : patient.DateOfBirth,
                row.Result.TestId,
                TestCode = test == null ? null : test.Code,
                TestNameEn = test == null ? null : test.Name.En,
                TestNameAr = test == null ? null : test.Name.Ar,
                row.Result.ClinicalGroupId,
                GroupNameEn = grp == null ? null : grp.Name.En,
                GroupNameAr = grp == null ? null : grp.Name.Ar,
                row.Result.Status,
                row.Result.TubeId,
                SampleNumber = row.Sample == null ? null : row.Sample.SampleNumber,
                SampleStatus = row.Sample == null ? (SampleStatus?)null : row.Sample.Status,
                CollectedAtUtc = row.Sample == null ? null : row.Sample.CollectedAtUtc,
                row.Result.ClockStartUtc,
                row.Result.ExpectedAtUtc,
                row.Result.VerifiedAtUtc,
                row.Result.Urgent,
                row.Result.TextValue,
                row.Result.Flag,
                row.Result.PrintCount,
                row.Result.Source,
            }).ToListAsync(cancellationToken).ConfigureAwait(false);

        var items = rows.Select(row => new WorklistRow(
            row.Id, row.VisitId, row.Accession, row.PatientId, row.PatientNumber, row.PatientName, row.PatientNameAr, row.Gender, row.DateOfBirth,
            row.TestId, row.TestCode, row.TestNameEn, row.TestNameAr, row.ClinicalGroupId, row.GroupNameEn, row.GroupNameAr, row.Status,
            row.TubeId, row.SampleNumber, row.SampleStatus?.ToString(), row.CollectedAtUtc, row.ClockStartUtc, row.ExpectedAtUtc, row.VerifiedAtUtc,
            row.Urgent, row.TextValue, row.Flag, row.PrintCount, row.Source)).ToList();
        return new WorklistSlice(items, total, requests, tubes, delayed);
    }

    public async Task<Paged<ResultVisitRow>> SearchVisitsAsync(ResultVisitFilter filter, ResultVisibility visibility, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var visible = ResultVisibilityFilter.Apply(Live, visibility);
        if (filter.BranchId is { } branchId)
        {
            visible = visible.Where(result => result.BranchId == branchId);
        }

        if (filter.RegisteredFromUtc is { } from)
        {
            visible = visible.Where(result => result.RegisteredAtUtc >= from);
        }

        if (filter.RegisteredToUtc is { } to)
        {
            visible = visible.Where(result => result.RegisteredAtUtc < to);
        }

        if (filter.Accession is { Length: > 0 } accession)
        {
            var term = accession.ToUpperInvariant();
            visible = visible.Where(result => result.Accession.StartsWith(term));
        }

        var patients = Context.Set<Patient>().AsNoTracking();
        if (filter.PatientNumber is { Length: > 0 } number)
        {
            var term = number.ToUpperInvariant();
            var ids = patients.Where(patient => patient.Number.StartsWith(term)).Select(patient => patient.Id);
            visible = visible.Where(result => ids.Contains(result.PatientId));
        }

        if (filter.PatientName is { Length: > 0 } name)
        {
            var pattern = LikePattern(name);
            var ids = patients.Where(patient => EF.Functions.ILike(patient.FullName, pattern) || (patient.NameAr != null && EF.Functions.ILike(patient.NameAr, pattern))).Select(patient => patient.Id);
            visible = visible.Where(result => ids.Contains(result.PatientId));
        }

        // Visit-level criteria: the visit qualifies when one of its visible results matches.
        var matching = visible;
        if (filter.Status is { } status)
        {
            matching = matching.Where(result => result.Status == status);
        }

        if (!string.IsNullOrEmpty(filter.SampleStatus) && Enum.TryParse<SampleStatus>(filter.SampleStatus, ignoreCase: true, out var sampleStatus))
        {
            var tubes = Context.Set<Sample>().AsNoTracking().Where(sample => sample.Status == sampleStatus).Select(sample => (Guid?)sample.TubeId);
            matching = matching.Where(result => tubes.Contains(result.TubeId));
        }

        if (filter.FromAnalyserId is { } analyserId)
        {
            matching = matching.Where(result => result.AnalyserId == analyserId);
        }

        if (filter.NotPrinted)
        {
            matching = matching.Where(result => result.PrintCount == 0);
        }

        if (filter.NotVerified)
        {
            matching = matching.Where(result => result.Status == ResultStatus.Ordered || result.Status == ResultStatus.Complete || result.Status == ResultStatus.Reviewed);
        }

        if (filter.DeltaOnly)
        {
            matching = matching.Where(result => result.DeltaFlag);
        }

        var visitIds = matching.Select(result => result.VisitId).Distinct();
        var grouped = visible.Where(result => visitIds.Contains(result.VisitId))
            .GroupBy(result => new { result.VisitId, result.Accession, result.PatientId, result.BranchId })
            .Select(group => new
            {
                group.Key.VisitId,
                group.Key.Accession,
                group.Key.PatientId,
                group.Key.BranchId,
                RegisteredAtUtc = group.Min(result => result.RegisteredAtUtc),
                Rank = group.Min(result => result.Status == ResultStatus.Ordered ? 0 : result.Status == ResultStatus.Complete ? 1 : result.Status == ResultStatus.Reviewed ? 2 : result.Status == ResultStatus.Verified ? 3 : 4),
                Results = group.Count(),
                Units = group.Select(result => result.ReportUnitKey).Distinct().Count(),
                Panic = group.Max(result => result.IsPanic ? 1 : 0),
                Delta = group.Max(result => result.DeltaFlag ? 1 : 0),
                Printed = group.Max(result => result.PrintCount),
            });

        var total = await grouped.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await grouped.OrderByDescending(row => row.RegisteredAtUtc).ThenBy(row => row.Accession).Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = rows.Select(row => new ResultVisitRow(row.VisitId, row.Accession, row.RegisteredAtUtc, row.PatientId, row.BranchId, (ResultStatus)row.Rank, row.Results, row.Units, row.Panic == 1, row.Delta == 1, row.Printed > 0)).ToList();
        return new Paged<ResultVisitRow>(items, page.Page, page.PageSize, total);
    }

    public async Task<ResultKpis> KpisAsync(Guid? branchId, DateTimeOffset fromUtc, DateTimeOffset toUtc, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var live = Live;
        if (branchId is { } branch)
        {
            live = live.Where(result => result.BranchId == branch);
        }

        var open = live.Where(result => result.Status == ResultStatus.Ordered || result.Status == ResultStatus.Complete || result.Status == ResultStatus.Reviewed);
        var pending = await open.CountAsync(result => result.RegisteredAtUtc >= fromUtc && result.RegisteredAtUtc < toUtc, cancellationToken).ConfigureAwait(false);
        var backlog = await open.CountAsync(result => result.Status != ResultStatus.Ordered, cancellationToken).ConfigureAwait(false);
        var delayed = await open.CountAsync(result => result.ExpectedAtUtc < nowUtc, cancellationToken).ConfigureAwait(false);
        var verified = await live.CountAsync(result => result.VerifiedAtUtc >= fromUtc && result.VerifiedAtUtc < toUtc, cancellationToken).ConfigureAwait(false);
        return new ResultKpis(pending, backlog, delayed, verified);
    }

    public Task<bool> AnyVerifiedAsync(IReadOnlyCollection<Guid> lineIds, CancellationToken cancellationToken)
    {
        var ids = lineIds.Distinct().ToArray();
        return ids.Length == 0
            ? Task.FromResult(false)
            : Set.AsNoTracking().AnyAsync(result => ids.Contains(result.LineId) && (result.Status == ResultStatus.Verified || result.Status == ResultStatus.Printed), cancellationToken);
    }

    /// <summary>A user's <c>%</c> pattern for ILIKE: <c>%</c> kept as the wildcard, <c>_</c> and <c>\</c> escaped; no wildcard = "contains".</summary>
    private static string LikePattern(string text)
    {
        var escaped = text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
        return escaped.Contains('%', StringComparison.Ordinal) ? escaped : $"%{escaped}%";
    }
}

/// <summary>The "see" result rights as a SQL predicate: groups visible everywhere, or a group visible in the result's branch.</summary>
internal static class ResultVisibilityFilter
{
    private static readonly System.Reflection.MethodInfo Contains = typeof(Enumerable).GetMethods()
        .First(method => method.Name == nameof(Enumerable.Contains) && method.GetParameters().Length == 2)
        .MakeGenericMethod(typeof(Guid));

    public static IQueryable<Result> Apply(IQueryable<Result> query, ResultVisibility visibility)
    {
        ArgumentNullException.ThrowIfNull(visibility);
        if (visibility.Unrestricted)
        {
            return query;
        }

        if (visibility.IsEmpty)
        {
            return query.Where(_ => false);
        }

        var result = Expression.Parameter(typeof(Result), "result");
        var group = Expression.Property(result, nameof(Result.ClinicalGroupId));
        var branch = Expression.Property(result, nameof(Result.BranchId));
        Expression? body = null;
        if (visibility.AllBranchGroupIds.Count > 0)
        {
            body = Expression.Call(Contains, Expression.Constant(visibility.AllBranchGroupIds.ToArray()), group);
        }

        foreach (var (branchId, groups) in visibility.GroupIdsByBranch)
        {
            var clause = Expression.AndAlso(
                Expression.Equal(branch, Expression.Constant(branchId)),
                Expression.Call(Contains, Expression.Constant(groups.ToArray()), group));
            body = body is null ? clause : Expression.OrElse(body, clause);
        }

        return query.Where(Expression.Lambda<Func<Result, bool>>(body!, result));
    }
}
