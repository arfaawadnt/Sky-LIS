using Platform.Application.Abstractions.Persistence;
using Platform.Contracts.Common;
using Platform.Domain.Patients;
using Platform.Domain.Results;

namespace Platform.Application.Results;

/// <summary>
/// The result-right "see" of the current user (CP-37, LDM §8) as a filter: groups visible in every branch and groups
/// visible per branch. <see cref="Unrestricted"/> is for system reads (dashboard figures), never for a user.
/// </summary>
public sealed record ResultVisibility(bool Unrestricted, IReadOnlyList<Guid> AllBranchGroupIds, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> GroupIdsByBranch)
{
    public static ResultVisibility Everything { get; } = new(true, [], new Dictionary<Guid, IReadOnlyList<Guid>>());

    public static ResultVisibility Nothing { get; } = new(false, [], new Dictionary<Guid, IReadOnlyList<Guid>>());

    public bool IsEmpty => !Unrestricted && AllBranchGroupIds.Count == 0 && GroupIdsByBranch.Count == 0;

    public bool CanSee(Guid clinicalGroupId, Guid branchId) =>
        Unrestricted
        || AllBranchGroupIds.Contains(clinicalGroupId)
        || (GroupIdsByBranch.TryGetValue(branchId, out var groups) && groups.Contains(clinicalGroupId));
}

/// <summary>Filters of the worklist (CP-18, intake §14): collection window, group, test, analyser (its mapped tests), sample status, payer, statuses, delayed, search, branch.</summary>
public sealed record WorklistFilter(
    DateTimeOffset? CollectedFromUtc,
    DateTimeOffset? CollectedToUtc,
    Guid? ClinicalGroupId,
    Guid? TestId,
    IReadOnlyCollection<Guid>? AnalyserTestIds,
    string? SampleStatus,
    Guid? PayerId,
    IReadOnlyCollection<ResultStatus>? Statuses,
    bool DelayedOnly,
    string? Search,
    Guid? BranchId);

/// <summary>A worklist row before the delay is computed: the result, its sample, patient, test and group as stored.</summary>
public sealed record WorklistRow(
    Guid ResultId,
    Guid VisitId,
    string Accession,
    Guid PatientId,
    string? PatientNumber,
    string? PatientName,
    string? PatientNameAr,
    PatientGender? Gender,
    DateOnly? DateOfBirth,
    Guid TestId,
    string? TestCode,
    string? TestNameEn,
    string? TestNameAr,
    Guid ClinicalGroupId,
    string? GroupNameEn,
    string? GroupNameAr,
    ResultStatus Status,
    Guid? TubeId,
    string? SampleNumber,
    string? SampleStatus,
    DateTimeOffset? CollectedAtUtc,
    DateTimeOffset ClockStartUtc,
    DateTimeOffset ExpectedAtUtc,
    DateTimeOffset? VerifiedAtUtc,
    bool Urgent,
    string? TextValue,
    ResultFlag? Flag,
    int PrintCount,
    ResultSource Source);

public sealed record WorklistSlice(IReadOnlyList<WorklistRow> Rows, long TotalCount, long Requests, long Samples, long Delayed);

/// <summary>The query panel of CP-19: accession, patient number or name (<c>%</c> wildcard), statuses, analyser, flags, registration window.</summary>
public sealed record ResultVisitFilter(
    string? Accession,
    string? PatientNumber,
    string? PatientName,
    ResultStatus? Status,
    string? SampleStatus,
    Guid? FromAnalyserId,
    bool NotPrinted,
    bool NotVerified,
    bool DeltaOnly,
    DateTimeOffset? RegisteredFromUtc,
    DateTimeOffset? RegisteredToUtc,
    Guid? BranchId);

public sealed record ResultVisitRow(Guid VisitId, string Accession, DateTimeOffset RegisteredAtUtc, Guid PatientId, Guid BranchId, ResultStatus Status, int Results, int Units, bool HasPanic, bool HasDelta, bool AnyPrinted);

/// <summary>Figures of the results for the dashboard (CP-05).</summary>
public sealed record ResultKpis(int PendingResults, int UnverifiedBacklog, int DelayedTests, int VerifiedInWindow);

/// <summary>Write side plus the projections of the module's registers and documents. Every read is bounded to the current tenant.</summary>
public interface IResultRepository : IRepository<Result>
{
    /// <summary>The (line, test) pairs among <paramref name="lineIds"/> that already have a result (the order policy's idempotency, INV-RES-001).</summary>
    Task<IReadOnlySet<(Guid LineId, Guid TestId)>> ExistingLineTestsAsync(IReadOnlyCollection<Guid> lineIds, CancellationToken cancellationToken);

    /// <summary>Results by id, tracked with their logs.</summary>
    Task<IReadOnlyList<Result>> ListForUpdateAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Every result of a visit, tracked with their logs (entry recomputes formulas across the visit).</summary>
    Task<IReadOnlyList<Result>> ListByVisitForUpdateAsync(Guid visitId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Result>> ListByLineForUpdateAsync(Guid lineId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Result>> ListByTubeForUpdateAsync(Guid tubeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Result>> ListByPatientForUpdateAsync(Guid patientId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Result>> ListByUnitsForUpdateAsync(IReadOnlyCollection<string> reportUnitKeys, CancellationToken cancellationToken);

    /// <summary>Untracked results of a visit (case view, documents).</summary>
    Task<IReadOnlyList<Result>> ReadByVisitAsync(Guid visitId, CancellationToken cancellationToken);

    /// <summary>Untracked results of report units (documents, print lists).</summary>
    Task<IReadOnlyList<Result>> ReadByUnitsAsync(IReadOnlyCollection<string> reportUnitKeys, CancellationToken cancellationToken);

    Task<Result?> ReadAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The visit of an accession (from the results, which carry it).</summary>
    Task<Guid?> FindVisitByAccessionAsync(string accession, CancellationToken cancellationToken);

    /// <summary>The visits that have results on a tube.</summary>
    Task<Guid?> FindVisitByTubeAsync(Guid tubeId, CancellationToken cancellationToken);

    /// <summary>BR-11: the patient's latest Verified / Printed numeric value of the test in another visit, verified at or after <paramref name="sinceUtc"/> when given.</summary>
    Task<PreviousResultValue?> PreviousVerifiedAsync(Guid patientId, Guid testId, Guid excludingVisitId, DateTimeOffset? sinceUtc, CancellationToken cancellationToken);

    /// <summary>The patient's other results of the test, newest first.</summary>
    Task<IReadOnlyList<Result>> ReadPreviousAsync(Guid patientId, Guid testId, Guid excludingResultId, int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<ResultLogEntry>> ReadLogAsync(Guid resultId, CancellationToken cancellationToken);

    Task<WorklistSlice> WorklistAsync(WorklistFilter filter, ResultVisibility visibility, PageRequest page, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task<Paged<ResultVisitRow>> SearchVisitsAsync(ResultVisitFilter filter, ResultVisibility visibility, PageRequest page, CancellationToken cancellationToken);

    Task<ResultKpis> KpisAsync(Guid? branchId, DateTimeOffset fromUtc, DateTimeOffset toUtc, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>INV-RES-009 for Registration: true when a line has a Verified or Printed result.</summary>
    Task<bool> AnyVerifiedAsync(IReadOnlyCollection<Guid> lineIds, CancellationToken cancellationToken);
}
