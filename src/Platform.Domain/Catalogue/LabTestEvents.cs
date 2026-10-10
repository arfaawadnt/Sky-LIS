using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

// Past-tense facts of the test (intake LIS-INT-CAT-001 §11). The outbox stamps the tenant.

public sealed record LabTestCreated(Guid LabTestId, string Code, LabTestType Type, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record LabTestChanged(Guid LabTestId, string Code, LabTestType Type, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record LabTestTimingChanged(Guid LabTestId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record LabTestRangesChanged(Guid LabTestId, string Code, int BandCount, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record LabTestListValuesChanged(Guid LabTestId, string Code, int ValueCount, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record LabTestFormulaChanged(Guid LabTestId, string Code, string? Expression, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record LabTestParagraphsChanged(Guid LabTestId, string Code, int ParagraphCount, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record LabTestDeactivated(Guid LabTestId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record LabTestReactivated(Guid LabTestId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
