using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Results;

// Past-tense facts of the result track (intake LIS-INT-RES-001 §11); ids and the few values a reader needs. The outbox
// stamps the tenant. Fields may be added, never removed or renamed. Statuses, flags and sources travel as their names.

/// <summary>A value was recorded (manual entry, analyser, formula or amendment); Automation evaluates it (CP-63/64).</summary>
public sealed record ResultEntered(
    Guid ResultId,
    Guid VisitId,
    string Accession,
    Guid TestId,
    Guid ClinicalGroupId,
    string ReportUnitKey,
    string Source,
    string? Flag,
    bool IsPanic,
    bool DeltaFlag,
    Guid? ActorUserId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>A panic (critical) value appeared on a result: the unit must call the doctor (LDM §3.4); Notifications may subscribe.</summary>
public sealed record ResultPanicDetected(
    Guid ResultId,
    Guid VisitId,
    string Accession,
    Guid PatientId,
    Guid TestId,
    Guid BranchId,
    string Value,
    string? Flag,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ResultReviewed(Guid ResultId, Guid VisitId, string Accession, Guid TestId, Guid ClinicalGroupId, string ReportUnitKey, Guid ActorUserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>The electronic signature (BR-09): the result may print.</summary>
public sealed record ResultVerified(Guid ResultId, Guid VisitId, string Accession, Guid TestId, Guid ClinicalGroupId, string ReportUnitKey, Guid ActorUserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Undo review / undo verify (LDM 9.3.0), audited with its reason.</summary>
public sealed record ResultUndone(Guid ResultId, Guid VisitId, string Accession, string FromStatus, string ToStatus, Guid ActorUserId, string Reason, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>A signed result was modified with the special right "modify verified result"; it is Complete again.</summary>
public sealed record ResultAmended(Guid ResultId, Guid VisitId, string Accession, Guid TestId, Guid ActorUserId, string Reason, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ResultDeltaAcknowledged(Guid ResultId, Guid VisitId, string Accession, Guid TestId, Guid ActorUserId, string? Comment, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>The result's report unit was printed (first print or a copy); Registration completes the visit when every unit is printed.</summary>
public sealed record ResultPrinted(Guid ResultId, Guid VisitId, string Accession, string ReportUnitKey, int PrintCount, string? PrintListNumber, Guid ActorUserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>The visit line was cancelled (BR-15: nothing is erased).</summary>
public sealed record ResultCancelled(Guid ResultId, Guid VisitId, string Accession, Guid LineId, Guid TestId, Guid ActorUserId, string Reason, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

