using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Account;

// Past-tense facts of the support desk (intake LIS-INT-ACC-001 §11). They carry the laboratory explicitly because a
// ticket lives in a platform-level table: the outbox stamps the tenant of the scope that raised the event, which is the
// platform tenant for a desk action.

public sealed record SupportTicketOpened(Guid TicketId, Guid TenantId, string Number, SupportTicketPriority Priority, Guid OpenedByUserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SupportTicketReplied(Guid TicketId, Guid TenantId, string Number, Guid ReplyId, bool FromPlatform, Guid AuthorUserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SupportTicketStarted(Guid TicketId, Guid TenantId, string Number, Guid ActorUserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SupportTicketResolved(Guid TicketId, Guid TenantId, string Number, Guid ActorUserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SupportTicketReopened(Guid TicketId, Guid TenantId, string Number, Guid ActorUserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SupportTicketClosed(Guid TicketId, Guid TenantId, string Number, Guid ActorUserId, bool ByPlatform, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

// The laboratory logo (top bar): an upload or replacement, and a removal. The tenant is the scope that raised them.

public sealed record LaboratoryLogoChanged(Guid LogoId, int Version, string ContentType, long SizeBytes, Guid ActorUserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record LaboratoryLogoRemoved(Guid LogoId, int Version, Guid ActorUserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
