using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

// Past-tense facts of the sample type (intake LIS-INT-CAT-001 §11). The outbox stamps the tenant.

public sealed record SampleTypeCreated(Guid SampleTypeId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SampleTypeChanged(Guid SampleTypeId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SampleConditionsChanged(Guid SampleTypeId, string Code, int ActiveConditionCount, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SampleTypeDeactivated(Guid SampleTypeId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SampleTypeReactivated(Guid SampleTypeId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>The compatible conditions of the type changed (LIS-CR-015 M4); <paramref name="MergingConditionCount"/> = conditions that now merge into another.</summary>
public sealed record SampleConditionCompatibilityChanged(Guid SampleTypeId, string Code, int MergingConditionCount, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
