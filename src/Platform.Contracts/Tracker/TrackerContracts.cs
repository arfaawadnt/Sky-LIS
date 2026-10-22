namespace Platform.Contracts.Tracker;

// Lab Tracker (change request LIS-CR-006, intake LIS-INT-TRK-001): GET /api/v1/tracker/visits. One row per visit of the
// laboratory day with its stage, next step and time left, all derived on the server. Enum values travel as their names.

/// <summary>Lines of the visit as ordered (top-level services and packages): live and cancelled.</summary>
public sealed record LabTrackerRegistration(int Lines, int CancelledLines);

/// <summary>
/// The live samples of the visit (Cancelled and Replaced excluded) by status; <see cref="FirstRejectionReason"/> is the
/// reason of the earliest rejection still waiting for a new sample (English, <see cref="FirstRejectionReasonAr"/> Arabic).
/// Present only with <c>samples.view</c>.
/// </summary>
public sealed record LabTrackerSamples(int Total, int Ordered, int Collected, int Received, int Rejected, string? FirstRejectionReason, string? FirstRejectionReasonAr);

/// <summary>
/// The live results the user may see (result rights per clinical group and branch, the worklist's rule) by status;
/// <see cref="Hidden"/> counts the live results outside those rights, <see cref="Panic"/> and <see cref="Delta"/> the visible
/// results flagged so, <see cref="FirstPanicTest"/> the test of the first visible panic (English; <see cref="FirstPanicTestAr"/>
/// Arabic). Present only with <c>results.view</c>.
/// </summary>
public sealed record LabTrackerResults(
    int Total,
    int Ordered,
    int Complete,
    int Reviewed,
    int Verified,
    int Printed,
    int Hidden,
    int Panic,
    int Delta,
    string? FirstPanicTest,
    string? FirstPanicTestAr);

/// <summary>The balance of the visit (BR-14): status Paid | Unpaid | Partial | RefundDue. Present only with <c>billing.view</c>.</summary>
public sealed record LabTrackerPayment(string Status, decimal PatientShare, decimal Paid, decimal Remaining);

/// <summary>
/// One visit of the tracker. <see cref="PatientName"/> is masked like the visits register for a VIP file without the
/// See-VIP right (it reads "VIP patient", <see cref="PatientMasked"/> true, no Arabic name). <see cref="Stage"/> is
/// Cancelled | AwaitingSample | InLab | AwaitingVerification | Ready | Delivered; <see cref="NextStep"/> CollectSample |
/// RecollectSample | EnterResults | ReviewResults | VerifyResults | TakePayment | PrintReport | None;
/// <see cref="MinutesLeft"/> the minutes to the earliest expected time of an unsigned result (negative = late);
/// <see cref="DelayState"/> OnTime | Warning | Late | Done. <see cref="AgeText"/> is the age on the laboratory day in its
/// largest whole unit: <c>34y</c>, <c>5m</c>, <c>12d</c>. The sections <see cref="Samples"/>, <see cref="Results"/> and
/// <see cref="Payment"/> are null without the module's view permission; the stage and next step are computed from
/// everything regardless.
/// </summary>
public sealed record LabTrackerItem(
    Guid VisitId,
    string Accession,
    Guid PatientId,
    string? PatientNumber,
    string PatientName,
    string? PatientNameAr,
    bool PatientMasked,
    string? Gender,
    string? AgeText,
    bool Urgent,
    DateTimeOffset RegisteredAtUtc,
    string? RegisteredByName,
    string? PayerName,
    string? PayerNameAr,
    string? TierName,
    string? TierNameAr,
    Guid BranchId,
    string Stage,
    string NextStep,
    int? MinutesLeft,
    string? DelayState,
    LabTrackerRegistration Registration,
    LabTrackerSamples? Samples,
    LabTrackerResults? Results,
    LabTrackerPayment? Payment);

/// <summary>
/// The counters of the whole filtered day (day, branch, search) <b>before</b> the stage, attention and view filters, so the
/// strip and the chips always show the day: <see cref="Total"/> every visit, one count per stage and one per attention flag.
/// </summary>
public sealed record LabTrackerCounters(
    int Total,
    int Cancelled,
    int AwaitingSample,
    int InLab,
    int AwaitingVerification,
    int Ready,
    int Delivered,
    int Delayed,
    int Panic,
    int Rejected,
    int ReadyUnpaid);

/// <summary>
/// A page of the tracker: the rows of the filtered day after the stage / attention / view filters, ordered before paging;
/// <see cref="Day"/> the laboratory day read (echoed after the default applied) and <see cref="BranchId"/> the branch.
/// </summary>
public sealed record LabTrackerPage(
    IReadOnlyList<LabTrackerItem> Items,
    int Page,
    int PageSize,
    long TotalCount,
    LabTrackerCounters Counters,
    DateOnly Day,
    Guid BranchId,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc);
