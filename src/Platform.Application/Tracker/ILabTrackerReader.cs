using Platform.Application.Abstractions.Persistence;
using Platform.Application.Results;
using Platform.Domain.Tracker;

namespace Platform.Application.Tracker;

/// <summary>
/// The visits of one laboratory day for the Lab Tracker (LIS-CR-006), read in one statement over the Registration, Samples,
/// Results and Billing tables with their per-visit aggregates (adapter <c>Platform.Infrastructure.Persistence.Tracker.LabTrackerReader</c>,
/// tenant-scoped by row-level security, no elevation). The stage and the other derived values are not the reader's: the
/// handler applies <see cref="VisitProgressRules"/> to the counts and clocks returned.
/// </summary>
public interface ILabTrackerReader
{
    Task<IReadOnlyList<LabTrackerRow>> ReadAsync(LabTrackerFilter filter, CancellationToken cancellationToken);

    /// <summary>
    /// The live top-level service lines (services and packages; package members fold into their package) of the same visits
    /// <paramref name="filter"/> selects, with the per-line aggregates of their samples and results (LIS-CR-014 T3).
    /// </summary>
    Task<IReadOnlyList<LabTrackerServiceRow>> ReadServicesAsync(LabTrackerFilter filter, CancellationToken cancellationToken);
}

/// <summary>
/// The visits registered in <c>[FromUtc, ToUtc)</c> at <see cref="BranchId"/>, narrowed by <see cref="Search"/> (accession,
/// sample number and patient number contain it; the Latin or Arabic name contains it unless the file is VIP and
/// <see cref="IncludeVip"/> is false; the mobile contains <see cref="MobileDigits"/> when the search is a number). Results
/// are counted visible by <see cref="Visibility"/>.
/// </summary>
public sealed record LabTrackerFilter(
    Guid BranchId,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string? Search,
    string? MobileDigits,
    bool IncludeVip,
    ResultVisibility Visibility);

/// <summary>
/// One visit with what the tracker needs: identity and names (patient, payer, tier, the user who registered it), top-level
/// lines live and cancelled, live samples by status with the first open rejection, live results (visible ones and all of them) by status,
/// visible panic / delta counts, the clocks of the unsigned results, and the balance figures.
/// </summary>
public sealed record LabTrackerRow(
    Guid VisitId,
    string Accession,
    Guid PatientId,
    string? PatientNumber,
    string? PatientName,
    string? PatientNameAr,
    bool IsVip,
    string? Gender,
    DateOnly? DateOfBirth,
    bool Urgent,
    DateTimeOffset RegisteredAtUtc,
    string? RegisteredByName,
    string? PayerNameEn,
    string? PayerNameAr,
    string? TierNameEn,
    string? TierNameAr,
    Guid BranchId,
    int LiveLines,
    int CancelledLines,
    VisitSampleCounts Samples,
    string? FirstRejectionReasonEn,
    string? FirstRejectionReasonAr,
    VisitResultCounts VisibleResults,
    VisitResultCounts AllResults,
    int VisiblePanics,
    int VisibleOpenPanics,
    int VisibleDeltas,
    string? FirstPanicTestEn,
    string? FirstPanicTestAr,
    IReadOnlyList<ResultClock> VisibleOpenClocks,
    IReadOnlyList<ResultClock> AllOpenClocks,
    decimal PatientShareTotal,
    decimal Paid,
    decimal Refunded,
    bool BilledThroughClaims);

/// <summary>
/// One live top-level line of a visit for the tracker's <i>Services</i> rows (LIS-CR-014 T3): the service as ordered (code,
/// names, sequence), the live samples of the tubes of the line and its package members, its live results (visible by the
/// user's result rights and all) with the clocks of the unsigned ones, the visible panic / delta counts and the first visible
/// panic test. A sample shared by two lines (one tube) counts for both.
/// </summary>
public sealed record LabTrackerServiceRow(
    Guid VisitId,
    Guid LineId,
    Guid ServiceId,
    string ServiceCode,
    string ServiceNameEn,
    string ServiceNameAr,
    string Kind,
    int Sequence,
    VisitSampleCounts Samples,
    string? FirstRejectionReasonEn,
    string? FirstRejectionReasonAr,
    VisitResultCounts VisibleResults,
    VisitResultCounts AllResults,
    int VisiblePanics,
    int VisibleOpenPanics,
    int VisibleDeltas,
    string? FirstPanicTestEn,
    string? FirstPanicTestAr,
    IReadOnlyList<ResultClock> VisibleOpenClocks,
    IReadOnlyList<ResultClock> AllOpenClocks);

/// <summary>The sortable columns of the tracker (allow-list; the handler orders the whole filtered day before paging).</summary>
public static class TrackerSortKeys
{
    public const string TimeLeft = "timeLeft";
    public const string RegisteredAt = "registeredAt";
    public const string Accession = "accession";
    public const string Patient = "patient";
    public const string Stage = "stage";

    /// <summary>The service code (the <i>Services</i> rows only, LIS-CR-014 T3).</summary>
    public const string Service = "service";

    public static readonly SortKeys Visits = new(TimeLeft, RegisteredAt, Accession, Patient, Stage);

    /// <summary>The keys of the <i>Services</i> rows: the visit keys and the service code.</summary>
    public static readonly SortKeys Services = new(TimeLeft, RegisteredAt, Accession, Patient, Stage, Service);
}
