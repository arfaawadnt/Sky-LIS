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

/// <summary>The sortable columns of the tracker (allow-list; the handler orders the whole filtered day before paging).</summary>
public static class TrackerSortKeys
{
    public const string TimeLeft = "timeLeft";
    public const string RegisteredAt = "registeredAt";
    public const string Accession = "accession";
    public const string Patient = "patient";
    public const string Stage = "stage";

    public static readonly SortKeys Visits = new(TimeLeft, RegisteredAt, Accession, Patient, Stage);
}
