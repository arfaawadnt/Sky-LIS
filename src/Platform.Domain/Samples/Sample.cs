using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Samples;

/// <summary>
/// A tube of a visit and its physical track (SRS CP-14…CP-17, WF-05; LDM §2.1–§2.3, §5.9). The transition table of
/// intake LIS-INT-SMP-001 §7 lives here, one method per row: Create (T1, from the registration tube), Collect (T2),
/// Receive (T3, records the collection first when the tube was not scanned at phlebotomy), Reject (T4, catalogue
/// reason), Unreject (T5, BR-18), Replace (T6, new sample on the same tube with a new number), Cancel (T7). Every
/// step appends one <see cref="SampleEvent"/> to the timeline. Only Received opens the result gate (BR-03).
/// </summary>
public sealed class Sample : AggregateRoot, ITenantScoped
{
    public const int MaxSampleNumberLength = 32;
    public const int MaxAccessionLength = 40;
    public const int MaxNoteLength = 500;
    public const int MaxReasonLength = 1000;

    /// <summary>Reference-number kind and prefix of sample numbers (MODULES.md §1); Registration issues the first, Replace the next.</summary>
    public const string NumberKind = "sample";
    public const string NumberPrefix = "SMP";

    private readonly List<SampleEvent> _events = [];

    private Sample()
    {
        SampleNumber = string.Empty;
        Accession = string.Empty;
        ServiceIds = [];
    }

    private Sample(
        Guid id,
        Guid tubeId,
        int generation,
        string sampleNumber,
        Guid visitId,
        string accession,
        Guid patientId,
        Guid branchId,
        Guid clinicalGroupId,
        Guid sampleTypeId,
        Guid? sampleConditionId,
        Guid[] serviceIds,
        DateTimeOffset nowUtc)
        : base(id)
    {
        TubeId = tubeId;
        Generation = generation;
        SampleNumber = sampleNumber;
        VisitId = visitId;
        Accession = accession;
        PatientId = patientId;
        BranchId = branchId;
        ClinicalGroupId = clinicalGroupId;
        SampleTypeId = sampleTypeId;
        SampleConditionId = sampleConditionId;
        ServiceIds = serviceIds;
        Status = SampleStatus.Ordered;
        OrderedAtUtc = nowUtc;
        StatusChangedAtUtc = nowUtc;
    }

    public Guid TenantId { get; private set; }

    /// <summary>The tube Registration generated (BR-02); every generation of the tube's sample keeps it (the policy's idempotency key).</summary>
    public Guid TubeId { get; private set; }

    /// <summary>0 for the sample created at registration, +1 for each replacement (INV-SMP-001).</summary>
    public int Generation { get; private set; }

    /// <summary>The barcode (BR-01): unique per tenant, never reused.</summary>
    public string SampleNumber { get; private set; }

    public Guid VisitId { get; private set; }

    public string Accession { get; private set; }

    public Guid PatientId { get; private set; }

    /// <summary>The branch that registered the visit.</summary>
    public Guid BranchId { get; private set; }

    public Guid ClinicalGroupId { get; private set; }

    public Guid SampleTypeId { get; private set; }

    public Guid? SampleConditionId { get; private set; }

    /// <summary>The services drawn into this tube.</summary>
    public Guid[] ServiceIds { get; private set; }

    public SampleStatus Status { get; private set; }

    public DateTimeOffset OrderedAtUtc { get; private set; }

    /// <summary>The "status date" of CP-15.</summary>
    public DateTimeOffset StatusChangedAtUtc { get; private set; }

    public DateTimeOffset? CollectedAtUtc { get; private set; }

    public Guid? CollectedByUserId { get; private set; }

    public Guid? CollectedBranchId { get; private set; }

    public DateTimeOffset? ReceivedAtUtc { get; private set; }

    public Guid? ReceivedByUserId { get; private set; }

    public Guid? ReceivedBranchId { get; private set; }

    /// <summary>The last rejection (kept after a reinstatement or replacement, BR-15).</summary>
    public SampleRejection? Rejection { get; private set; }

    public Guid? ReplacedBySampleId { get; private set; }

    public Guid? ReplacesSampleId { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public Guid? CancelledByUserId { get; private set; }

    public string? CancellationReason { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>The timeline, oldest first as appended.</summary>
    public IReadOnlyCollection<SampleEvent> Events => _events.AsReadOnly();

    /// <summary>A sample is live until a replacement supersedes it; at most one live sample per tube.</summary>
    public bool IsLive => Status != SampleStatus.Replaced;

    /// <summary>BR-03: results may be recorded only on a Received sample.</summary>
    public bool IsResultEntryAllowed => Status == SampleStatus.Received;

    /// <summary>
    /// T1 Create from a registration tube (policy on <c>VisitRegistered</c> / <c>VisitLinesAdded</c>): an Ordered sample
    /// with the number Registration issued. The auto settings are applied afterwards with <see cref="AutoAdvance"/>.
    /// </summary>
    public static Sample Create(
        Guid tubeId,
        string sampleNumber,
        Guid visitId,
        string accession,
        Guid patientId,
        Guid branchId,
        Guid clinicalGroupId,
        Guid sampleTypeId,
        Guid? sampleConditionId,
        IEnumerable<Guid> serviceIds,
        Guid? orderedByUserId,
        DateTimeOffset nowUtc)
    {
        Require(tubeId != Guid.Empty, SamplesCodes.TubeRequired, "A sample needs the tube it was generated for.", DomainFault.Validation);
        Require(
            visitId != Guid.Empty && patientId != Guid.Empty && branchId != Guid.Empty && clinicalGroupId != Guid.Empty && sampleTypeId != Guid.Empty,
            SamplesCodes.ReferenceRequired,
            "A sample needs its visit, patient, branch, clinical group and sample type.",
            DomainFault.Validation);

        var sample = new Sample(
            Guid.CreateVersion7(),
            tubeId,
            0,
            CleanNumber(sampleNumber),
            visitId,
            CleanAccession(accession),
            patientId,
            branchId,
            clinicalGroupId,
            sampleTypeId,
            sampleConditionId is { } condition && condition != Guid.Empty ? condition : null,
            CleanServices(serviceIds),
            nowUtc);
        sample.Append(SampleAction.Ordered, nowUtc, orderedByUserId, branchId);
        return sample;
    }

    /// <summary>
    /// The arrival parameters (CP-57 auto-collect / auto-receive, LDM §2.3): an Ordered sample is collected, and also
    /// received when <paramref name="autoReceive"/> is on, by the given actor at the given branch and time. Anything
    /// already past Ordered is left alone.
    /// </summary>
    public void AutoAdvance(bool autoCollect, bool autoReceive, Guid actorUserId, Guid branchId, DateTimeOffset nowUtc)
    {
        if (Status != SampleStatus.Ordered)
        {
            return;
        }

        if (autoReceive)
        {
            Receive(actorUserId, branchId, nowUtc);
        }
        else if (autoCollect)
        {
            Collect(actorUserId, branchId, nowUtc);
        }
    }

    /// <summary>
    /// Lines added to a saved visit on an existing tube (policy on <c>VisitLinesAdded</c>, BR-02): their services join
    /// the tube so the label and the job order list them. Idempotent; a Cancelled or Replaced sample takes no service.
    /// Returns true when the tube gained a service.
    /// </summary>
    public bool AddServices(IEnumerable<Guid> serviceIds)
    {
        if (Status is SampleStatus.Cancelled or SampleStatus.Replaced)
        {
            return false;
        }

        var merged = CleanServices(ServiceIds.Concat(serviceIds ?? []));
        if (merged.Length == ServiceIds.Length)
        {
            return false;
        }

        ServiceIds = merged;
        return true;
    }

    /// <summary>T2 Collect (<c>samples.create</c>): Ordered only (SMP-010); records time, user and branch.</summary>
    public void Collect(Guid actorUserId, Guid branchId, DateTimeOffset nowUtc)
    {
        if (Status != SampleStatus.Ordered)
        {
            throw new InvalidStateTransitionException(SamplesCodes.NotOrdered, "sample", Status.ToString(), "collect");
        }

        CollectedAtUtc = nowUtc;
        CollectedByUserId = actorUserId;
        CollectedBranchId = branchId;
        MoveTo(SampleStatus.Collected, nowUtc);
        Append(SampleAction.Collected, nowUtc, actorUserId, branchId);
        Raise(new SampleCollected(Id, SampleNumber, TubeId, VisitId, Accession, branchId, actorUserId, nowUtc));
    }

    /// <summary>T3 Receive (<c>samples.create</c>): Collected, or Ordered (the collection is recorded first, INV-SMP-003); else SMP-011.</summary>
    public void Receive(Guid actorUserId, Guid branchId, DateTimeOffset nowUtc)
    {
        if (Status == SampleStatus.Ordered)
        {
            Collect(actorUserId, branchId, nowUtc);
        }

        if (Status != SampleStatus.Collected)
        {
            throw new InvalidStateTransitionException(SamplesCodes.NotReceivable, "sample", Status.ToString(), "receive");
        }

        ReceivedAtUtc = nowUtc;
        ReceivedByUserId = actorUserId;
        ReceivedBranchId = branchId;
        MoveTo(SampleStatus.Received, nowUtc);
        Append(SampleAction.Received, nowUtc, actorUserId, branchId);
        Raise(new SampleReceived(Id, SampleNumber, TubeId, VisitId, Accession, branchId, actorUserId, nowUtc));
    }

    /// <summary>T4 Reject (<c>samples.edit</c>): Collected or Received (SMP-012); a catalogue reason (SMP-020) and an optional note ≤ 500 (SMP-022).</summary>
    public void Reject(RejectionReason reason, string? note, Guid actorUserId, Guid branchId, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(reason);
        if (Status is not (SampleStatus.Collected or SampleStatus.Received))
        {
            throw new InvalidStateTransitionException(SamplesCodes.NotRejectable, "sample", Status.ToString(), "reject");
        }

        Require(reason.ReasonId != Guid.Empty, SamplesCodes.RejectionReasonRequired, "A rejection needs a reason.", DomainFault.Validation);
        var cleanedNote = CleanNote(note);

        Rejection = new SampleRejection(reason, cleanedNote, nowUtc, actorUserId, branchId);
        MoveTo(SampleStatus.Rejected, nowUtc);
        Append(SampleAction.Rejected, nowUtc, actorUserId, branchId, reason.ReasonId, cleanedNote);
        Raise(new SampleRejected(Id, SampleNumber, TubeId, VisitId, Accession, PatientId, BranchId, branchId, reason.ReasonId, DescribeRejection(reason, cleanedNote), actorUserId, nowUtc));
    }

    /// <summary>
    /// T5 Unreject (<c>samples.edit</c>): Rejected only (SMP-013) and only when the tenant allows reinstatement with
    /// the same barcode (<paramref name="reinstatementAllowed"/> = <c>newBarcodeOnReject</c> off, BR-18; else SMP-016).
    /// The sample is back to Received; the reinstatement is recorded as the new receipt.
    /// </summary>
    public void Unreject(bool reinstatementAllowed, string? note, Guid actorUserId, Guid branchId, DateTimeOffset nowUtc)
    {
        RequireRejected("unreject");
        Require(reinstatementAllowed, SamplesCodes.ReinstatementNotAllowed, "The laboratory policy requires a new barcode: replace the sample.", DomainFault.Refused);
        var cleanedNote = CleanNote(note);

        ReceivedAtUtc = nowUtc;
        ReceivedByUserId = actorUserId;
        ReceivedBranchId = branchId;
        MoveTo(SampleStatus.Received, nowUtc);
        Append(SampleAction.Unrejected, nowUtc, actorUserId, branchId, note: cleanedNote);
        Raise(new SampleUnrejected(Id, SampleNumber, TubeId, VisitId, Accession, actorUserId, cleanedNote, nowUtc));
    }

    /// <summary>
    /// T6 Replace (<c>samples.edit</c>): Rejected only (SMP-013). This sample becomes Replaced; a new Ordered sample on
    /// the same tube (generation + 1) with the new number takes over (INV-SMP-001, INV-SMP-006). The handler applies
    /// the arrival parameters to the returned sample with <see cref="AutoAdvance"/>.
    /// </summary>
    public Sample Replace(string newSampleNumber, string? note, Guid actorUserId, Guid branchId, DateTimeOffset nowUtc)
    {
        EnsureReplaceable();
        var cleanedNote = CleanNote(note);
        var number = CleanNumber(newSampleNumber);
        Require(!string.Equals(number, SampleNumber, StringComparison.Ordinal), SamplesCodes.SampleNumberInvalid, "A replacement needs a new sample number (BR-18).", DomainFault.Validation);

        var replacement = new Sample(
            Guid.CreateVersion7(),
            TubeId,
            Generation + 1,
            number,
            VisitId,
            Accession,
            PatientId,
            BranchId,
            ClinicalGroupId,
            SampleTypeId,
            SampleConditionId,
            [.. ServiceIds],
            nowUtc)
        {
            ReplacesSampleId = Id,
        };
        replacement.Append(SampleAction.ReplacementOrdered, nowUtc, actorUserId, branchId, note: SampleNumber);

        ReplacedBySampleId = replacement.Id;
        MoveTo(SampleStatus.Replaced, nowUtc);
        Append(SampleAction.Replaced, nowUtc, actorUserId, branchId, note: cleanedNote);
        Raise(new SampleReplaced(Id, replacement.Id, replacement.SampleNumber, TubeId, SampleNumber, VisitId, Accession, actorUserId, cleanedNote, nowUtc));
        return replacement;
    }

    /// <summary>The precondition of T6 (SMP-013), callable before the handler draws the new number.</summary>
    public void EnsureReplaceable() => RequireRejected("replace");

    /// <summary>
    /// T7 Cancel (policy on <c>VisitLineCancelled</c>): the last live line of the tube is gone. Idempotent (a Cancelled
    /// sample returns false); a Replaced sample is refused (SMP-014): its live replacement is the one to cancel.
    /// </summary>
    public bool Cancel(Guid actorUserId, string? reason, DateTimeOffset nowUtc)
    {
        if (Status == SampleStatus.Cancelled)
        {
            return false;
        }

        if (Status == SampleStatus.Replaced)
        {
            throw new InvalidStateTransitionException(SamplesCodes.NotCancellable, "sample", Status.ToString(), "cancel");
        }

        var cleaned = string.IsNullOrWhiteSpace(reason) ? "The services of the tube were cancelled." : reason.Trim();
        if (cleaned.Length > MaxReasonLength)
        {
            cleaned = cleaned[..MaxReasonLength];
        }

        CancelledAtUtc = nowUtc;
        CancelledByUserId = actorUserId == Guid.Empty ? null : actorUserId;
        CancellationReason = cleaned;
        MoveTo(SampleStatus.Cancelled, nowUtc);
        Append(SampleAction.Cancelled, nowUtc, actorUserId, null, note: cleaned);
        Raise(new SampleCancelled(Id, SampleNumber, TubeId, VisitId, Accession, actorUserId, cleaned, nowUtc));
        return true;
    }

    /// <summary>The scanned or typed barcode in its stored form: trimmed and upper-case; 1–32 printable ASCII characters (Code 128 set B).</summary>
    public static string CleanNumber(string sampleNumber)
    {
        var value = (sampleNumber ?? string.Empty).Trim().ToUpperInvariant();
        Require(
            value.Length is >= 1 and <= MaxSampleNumberLength && value.All(c => c is >= '!' and <= '~'),
            SamplesCodes.SampleNumberInvalid,
            $"A sample number of 1 to {MaxSampleNumberLength} printable characters is required.",
            DomainFault.Validation);
        return value;
    }

    private static string CleanAccession(string accession)
    {
        var value = (accession ?? string.Empty).Trim();
        Require(value.Length is >= 1 and <= MaxAccessionLength, SamplesCodes.ReferenceRequired, $"A sample needs the accession of its visit (1 to {MaxAccessionLength} characters).", DomainFault.Validation);
        return value;
    }

    private static Guid[] CleanServices(IEnumerable<Guid> serviceIds) =>
        [.. (serviceIds ?? []).Where(id => id != Guid.Empty).Distinct()];

    private static string? CleanNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var trimmed = note.Trim();
        Require(trimmed.Length <= MaxNoteLength, SamplesCodes.NoteTooLong, $"The note exceeds {MaxNoteLength} characters.", DomainFault.Validation);
        return trimmed;
    }

    private static string DescribeRejection(RejectionReason reason, string? note)
    {
        var name = string.IsNullOrWhiteSpace(reason.NameEn) ? $"Reason {reason.ReasonId}" : reason.NameEn.Trim();
        return note is null ? name : $"{name} — {note}";
    }

    private void RequireRejected(string action)
    {
        if (Status != SampleStatus.Rejected)
        {
            throw new InvalidStateTransitionException(SamplesCodes.NotRejected, "sample", Status.ToString(), action);
        }
    }

    private void MoveTo(SampleStatus status, DateTimeOffset nowUtc)
    {
        Status = status;
        StatusChangedAtUtc = nowUtc;
    }

    private void Append(SampleAction action, DateTimeOffset atUtc, Guid? byUserId, Guid? branchId, Guid? reasonId = null, string? note = null) =>
        _events.Add(SampleEvent.Record(Id, _events.Count == 0 ? 1 : _events.Max(step => step.Sequence) + 1, action, Status, atUtc, byUserId, branchId, reasonId, note));
}
