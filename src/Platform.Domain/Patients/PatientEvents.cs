using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Patients;

// Past-tense facts (intake §11): ids, numbers and reasons only, never names or contact details (PII). The outbox stamps the tenant.

public sealed record PatientRegistered(Guid PatientId, string Number, PatientGender Gender, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PatientUpdated(Guid PatientId, string Number, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Registration, Results and Billing re-point the merged file's visits, results and payments to the survivor (idempotent on their row ids).</summary>
public sealed record PatientsMerged(Guid SurvivorId, string SurvivorNumber, Guid MergedId, string MergedNumber, Guid ActorUserId, string Reason, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PatientDocumentAttached(Guid DocumentId, Guid PatientId, string ContentType, long SizeBytes, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PatientDocumentDeactivated(Guid DocumentId, Guid PatientId, Guid ActorUserId, string Reason, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
