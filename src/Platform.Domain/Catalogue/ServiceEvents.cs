using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Catalogue;

// Past-tense facts of the service (intake LIS-INT-CAT-001 §11). The outbox stamps the tenant.

public sealed record ServiceCreated(Guid ServiceId, string Code, ServiceType Type, int Sequence, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ServiceChanged(Guid ServiceId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ServiceMembersChanged(Guid ServiceId, string Code, int MemberCount, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ServiceResequenced(Guid ServiceId, string Code, int Sequence, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ServiceDeactivated(Guid ServiceId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ServiceReactivated(Guid ServiceId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
