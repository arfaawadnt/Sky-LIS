using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

// Past-tense facts of the unit of measurement (intake LIS-INT-CAT-001 §11).

public sealed record MeasurementUnitCreated(Guid MeasurementUnitId, string Name, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record MeasurementUnitChanged(Guid MeasurementUnitId, string Name, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record MeasurementUnitDeactivated(Guid MeasurementUnitId, string Name, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record MeasurementUnitReactivated(Guid MeasurementUnitId, string Name, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
