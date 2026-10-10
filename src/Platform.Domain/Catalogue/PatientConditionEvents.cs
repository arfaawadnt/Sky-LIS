using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

// Past-tense facts of the patient condition (intake LIS-INT-CAT-001 §11).

public sealed record PatientConditionCreated(Guid PatientConditionId, string NameEn, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PatientConditionChanged(Guid PatientConditionId, string NameEn, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PatientConditionDeactivated(Guid PatientConditionId, string NameEn, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PatientConditionReactivated(Guid PatientConditionId, string NameEn, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
