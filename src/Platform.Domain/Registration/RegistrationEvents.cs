using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Registration;

// Shared event contracts of the Registration module (docs/build/MODULES.md §3). Samples, Results and Billing build
// their policies on these shapes in parallel with the Visit aggregate: fields may be added, never removed or renamed.
// The outbox stamps the tenant; the actor and branch travel in the event because readers need them.

/// <summary>
/// A tube generated at registration (BR-02: one per clinical group × sample type × condition). <paramref name="CollectedOutside"/>
/// (LIS-CR-008 C-02): the sample was collected outside the laboratory, at <paramref name="CollectedAtUtc"/> when the clerk typed
/// it; Samples records it as Collected whatever the auto-collect parameter says. Defaults keep older outbox payloads readable.
/// </summary>
public sealed record RegisteredTube(Guid TubeId, string SampleNumber, Guid ClinicalGroupId, Guid SampleTypeId, Guid? SampleConditionId, IReadOnlyList<Guid> ServiceIds, bool CollectedOutside = false, DateTimeOffset? CollectedAtUtc = null);

/// <summary>An ordered line of the visit; <paramref name="Status"/> is "Ordered" or "Reserved" (BR-05).</summary>
public sealed record RegisteredLine(Guid LineId, Guid ServiceId, Guid? TubeId, string Status, IReadOnlyList<Guid> TestIds);

/// <summary>Samples creates one Sample per tube (idempotent on TubeId); Results creates one Result per test of every Ordered line (idempotent on LineId + TestId).</summary>
public sealed record VisitRegistered(
    Guid VisitId,
    string Accession,
    Guid BranchId,
    Guid PatientId,
    Guid? PayerId,
    Guid? ContractTierId,
    bool Urgent,
    Guid RegisteredByUserId,
    IReadOnlyList<RegisteredTube> Tubes,
    IReadOnlyList<RegisteredLine> Lines,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Samples cancels the tube when no live line remains on it; Results cancels the line's results (BR-15: nothing is erased).</summary>
public sealed record VisitLineCancelled(Guid VisitId, string Accession, Guid LineId, Guid ServiceId, Guid? TubeId, Guid ActorUserId, string Reason, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Billing re-reads the visit balance after a contract transfer (BR-06); an overpayment becomes refundable (BR-14).</summary>
public sealed record VisitTransferred(Guid VisitId, string Accession, Guid? FromPayerId, Guid? FromContractTierId, Guid? ToPayerId, Guid? ToContractTierId, Guid ActorUserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>
/// Lines added to a saved visit, or Reserved lines released to Ordered (BR-05). Carries the new tubes and every
/// line whose state changed; Samples and Results apply the same idempotent rules as for <see cref="VisitRegistered"/>.
/// </summary>
public sealed record VisitLinesAdded(
    Guid VisitId,
    string Accession,
    Guid BranchId,
    Guid PatientId,
    Guid ActorUserId,
    IReadOnlyList<RegisteredTube> Tubes,
    IReadOnlyList<RegisteredLine> Lines,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
