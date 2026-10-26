using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

// Past-tense facts of the sample type (intake LIS-INT-CAT-001 §11). The outbox stamps the tenant.

public sealed record SampleTypeCreated(Guid SampleTypeId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SampleTypeChanged(Guid SampleTypeId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SampleConditionsChanged(Guid SampleTypeId, string Code, int ActiveConditionCount, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SampleTypeDeactivated(Guid SampleTypeId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SampleTypeReactivated(Guid SampleTypeId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

// Compatibility groups of samples (LIS-CR-015 M4).

public sealed record SampleCompatibilityGroupCreated(Guid GroupId, string Code, int MemberCount, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SampleCompatibilityGroupChanged(Guid GroupId, string Code, int MemberCount, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SampleCompatibilityGroupDeactivated(Guid GroupId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SampleCompatibilityGroupReactivated(Guid GroupId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
