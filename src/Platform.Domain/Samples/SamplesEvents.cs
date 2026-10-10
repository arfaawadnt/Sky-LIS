using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Samples;

// Past-tense facts of the sample track (intake LIS-INT-SMP-001 §11); ids and the few values a reader needs. The
// outbox stamps the tenant. Results (wave 3) keys its rows by TubeId, so every event carries it.

public sealed record SampleCollected(Guid SampleId, string SampleNumber, Guid TubeId, Guid VisitId, string Accession, Guid BranchId, Guid ActorUserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>The Received gate opens (BR-03).</summary>
public sealed record SampleReceived(Guid SampleId, string SampleNumber, Guid TubeId, Guid VisitId, string Accession, Guid BranchId, Guid ActorUserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>
/// Reception is notified to recollect (CP-16). <paramref name="RegisteredBranchId"/> is the branch that registered the
/// visit (the reception to tell); <paramref name="BranchId"/> is where the tube was rejected. <paramref name="Reason"/>
/// is the reason name with the note, copied into the audit history.
/// </summary>
public sealed record SampleRejected(
    Guid SampleId,
    string SampleNumber,
    Guid TubeId,
    Guid VisitId,
    string Accession,
    Guid PatientId,
    Guid RegisteredBranchId,
    Guid BranchId,
    Guid ReasonId,
    string Reason,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Reinstated with the same barcode (BR-18 with reinstatement allowed).</summary>
public sealed record SampleUnrejected(Guid SampleId, string SampleNumber, Guid TubeId, Guid VisitId, string Accession, Guid ActorUserId, string? Comment, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>A new sample with a new barcode took over the tube (BR-18); results stay on the tube.</summary>
public sealed record SampleReplaced(
    Guid OldSampleId,
    Guid NewSampleId,
    string NewSampleNumber,
    Guid TubeId,
    string OldSampleNumber,
    Guid VisitId,
    string Accession,
    Guid ActorUserId,
    string? Comment,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>No live line remains on the tube (CP-26 "related samples cancelled").</summary>
public sealed record SampleCancelled(Guid SampleId, string SampleNumber, Guid TubeId, Guid VisitId, string Accession, Guid ActorUserId, string Reason, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
