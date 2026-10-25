namespace Platform.Contracts.Samples;

// Request and response shapes of the Samples module (intake LIS-INT-SMP-001). Validation here is shape only; the
// Sample aggregate decides. Names of other modules' records (patient, catalogue, branch, reason) are resolved at read
// time and are null when the owning table is not installed; the ids are always present.

/// <summary>CP-14 scan box: <paramref name="Mode"/> is <c>Collect</c> or <c>Receive</c>.</summary>
public sealed record ScanSampleRequest(string SampleNumber, string Mode);

public sealed record RejectSampleRequest(Guid ReasonId, string? Note);

public sealed record UnrejectSampleRequest(string? Note);

public sealed record ReplaceSampleRequest(string? Note);

/// <summary>One row of the sample registers (CP-14 today's list, CP-15 tubes of a visit, CP-16 rejected list). Status names as stored.</summary>
public sealed record SampleListItem(
    Guid Id,
    string SampleNumber,
    Guid TubeId,
    int Generation,
    Guid VisitId,
    string Accession,
    Guid PatientId,
    string? PatientNumber,
    string? PatientName,
    string? PatientNameAr,
    Guid BranchId,
    Guid ClinicalGroupId,
    string? UnitCode,
    string? ClinicalGroupNameEn,
    string? ClinicalGroupNameAr,
    Guid SampleTypeId,
    string? SampleTypeNameEn,
    string? SampleTypeNameAr,
    Guid? SampleConditionId,
    string? SampleConditionNameEn,
    string? SampleConditionNameAr,
    int ServiceCount,
    string Status,
    DateTimeOffset OrderedAtUtc,
    DateTimeOffset StatusChangedAtUtc,
    DateTimeOffset? CollectedAtUtc,
    DateTimeOffset? ReceivedAtUtc,
    Guid? RejectionReasonId,
    string? RejectionReasonNameEn,
    string? RejectionReasonNameAr,
    string? RejectionNote,
    DateTimeOffset? RejectedAtUtc,
    Guid? ReplacedBySampleId,
    Guid? ReplacesSampleId,
    bool CollectedOutside = false);

public sealed record SampleServiceView(Guid Id, string? Code, string? NameEn, string? NameAr);

/// <summary>Who did a step, where and when (names resolved at read time).</summary>
public sealed record SampleStepView(DateTimeOffset AtUtc, Guid? UserId, string? UserName, Guid? BranchId, string? BranchNameEn, string? BranchNameAr);

public sealed record SampleRejectionView(Guid ReasonId, string? ReasonNameEn, string? ReasonNameAr, string? Note, SampleStepView Step);

/// <summary>A link to another generation of the same tube (the replacing or the replaced sample).</summary>
public sealed record SampleLinkView(Guid SampleId, string SampleNumber, string Status);

public sealed record SampleDetail(
    SampleListItem Summary,
    IReadOnlyList<SampleServiceView> Services,
    SampleStepView? Collected,
    SampleStepView? Received,
    SampleRejectionView? Rejection,
    SampleLinkView? ReplacedBy,
    SampleLinkView? Replaces,
    DateTimeOffset? CancelledAtUtc,
    string? CancellationReason,
    bool IsResultEntryAllowed);

/// <summary>The result of collect all / receive all of an accession: the samples that moved and the ones left as they were.</summary>
public sealed record VisitSamplesActionResult(Guid VisitId, int Changed, int Unchanged, IReadOnlyList<string> ChangedSampleNumbers);

/// <summary>One step of the CP-17 timeline, in <paramref name="Sequence"/> order; <paramref name="SincePreviousSeconds"/> is the time since the previous step.</summary>
public sealed record SampleTimelineStep(
    Guid Id,
    int Sequence,
    string Action,
    string Status,
    DateTimeOffset AtUtc,
    Guid? UserId,
    string? UserName,
    Guid? BranchId,
    string? BranchNameEn,
    string? BranchNameAr,
    Guid? ReasonId,
    string? ReasonNameEn,
    string? ReasonNameAr,
    string? Note,
    long? SincePreviousSeconds);

/// <summary>Time between stages (CP-17), in seconds; null while the later stage has not happened.</summary>
public sealed record SampleStageDurations(long? OrderedToCollectedSeconds, long? CollectedToReceivedSeconds, long? OrderedToReceivedSeconds);

public sealed record SampleTimeline(SampleDetail Sample, IReadOnlyList<SampleTimelineStep> Steps, SampleStageDurations Durations);

/// <summary>
/// The label document (CP-15, LDM §2.2 label content): unit letter, date/time, branch, sample type and condition,
/// accession, patient number and name, the barcode (sample number) and the service names; <paramref name="Copies"/>
/// from the label-copies parameter and the services' label counts. Rendering (Code 128) is the SPA's print kit.
/// </summary>
public sealed record SampleLabelDocument(
    Guid SampleId,
    string SampleNumber,
    string Accession,
    string? UnitCode,
    string? ClinicalGroupNameEn,
    string? ClinicalGroupNameAr,
    Guid SampleTypeId,
    string? SampleTypeNameEn,
    string? SampleTypeNameAr,
    Guid? SampleConditionId,
    string? SampleConditionNameEn,
    string? SampleConditionNameAr,
    Guid PatientId,
    string? PatientNumber,
    string? PatientName,
    string? PatientNameAr,
    Guid BranchId,
    string? BranchNameEn,
    string? BranchNameAr,
    DateTimeOffset LabelAtUtc,
    IReadOnlyList<SampleServiceView> Services,
    int Copies,
    bool CollectedOutside = false);

public sealed record VisitLabelsDocument(Guid VisitId, string Accession, IReadOnlyList<SampleLabelDocument> Labels);

/// <summary>The tenant parameters the sample screens follow (CP-57): the arrival switches, BR-18 and label copies.</summary>
public sealed record SampleSettingsView(bool AutoCollect, bool AutoReceive, bool NewBarcodeOnReject, int LabelCopies);
