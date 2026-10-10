using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Registration;

// Events of the Registration module beyond the shared contracts of RegistrationEvents.cs (intake §11). Past-tense facts
// with ids and the few values a reader needs; a Reason property feeds the audit ledger. The outbox stamps the tenant.

/// <summary>V7: the contract transfer as logged (reason for the audit trail); <see cref="VisitTransferred"/> is the shared contract.</summary>
public sealed record VisitTransferRecorded(
    Guid VisitId,
    string Accession,
    Guid TransferId,
    Guid FromPayerId,
    Guid FromContractTierId,
    Guid ToPayerId,
    Guid ToContractTierId,
    decimal PatientShareBefore,
    decimal PatientShareAfter,
    Guid ActorUserId,
    string Reason,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>V8: value moved between the patient and the payer share of one line (LDM "Normal Transfer").</summary>
public sealed record VisitLineSharesAdjusted(
    Guid VisitId,
    string Accession,
    Guid LineId,
    Guid ServiceId,
    decimal PatientShareBefore,
    decimal PatientShareAfter,
    decimal PayerShareBefore,
    decimal PayerShareAfter,
    Guid ActorUserId,
    string Reason,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>V1 / V6: the visit discount was applied, changed or cleared (<paramref name="Kind"/> null = cleared).</summary>
public sealed record VisitDiscountChanged(
    Guid VisitId,
    string Accession,
    string? Kind,
    decimal Value,
    decimal Amount,
    Guid ActorUserId,
    string Reason,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>V9: the visit is completed (results reported and settled; raised by later policies).</summary>
public sealed record VisitCompleted(Guid VisitId, string Accession, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>V10: the visit now belongs to the surviving file of a patient merge (BR-15: the original id is kept on the visit).</summary>
public sealed record VisitPatientRepointed(Guid VisitId, string Accession, Guid FromPatientId, Guid ToPatientId, Guid ActorUserId, string Reason, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>B1: a home visit was booked.</summary>
public sealed record BookingCreated(Guid BookingId, string Number, Guid BranchId, DateTimeOffset VisitAtUtc, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>B3: a booking was withdrawn.</summary>
public sealed record BookingCancelled(Guid BookingId, string Number, Guid ActorUserId, string Reason, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>B4: a booking became a visit (WF-03).</summary>
public sealed record BookingConverted(Guid BookingId, string Number, Guid VisitId, string Accession, Guid ActorUserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>B5: the patient of a booking now is the surviving file of a merge.</summary>
public sealed record BookingPatientRepointed(Guid BookingId, string Number, Guid FromPatientId, Guid ToPatientId, string Reason, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>T1: a batch (group or lab-to-lab) was registered with its member visits.</summary>
public sealed record BatchRegistered(Guid BatchId, string Number, string Kind, int MemberCount, Guid ActorUserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>T2: members (visits) were added to a batch.</summary>
public sealed record BatchMembersAdded(Guid BatchId, string Number, int Added, Guid ActorUserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
