using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

// Past-tense facts of the clinical group (intake LIS-INT-CAT-001 §11). The outbox stamps the tenant.

public sealed record ClinicalGroupCreated(Guid ClinicalGroupId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ClinicalGroupChanged(Guid ClinicalGroupId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ClinicalGroupDeactivated(Guid ClinicalGroupId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ClinicalGroupReactivated(Guid ClinicalGroupId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
