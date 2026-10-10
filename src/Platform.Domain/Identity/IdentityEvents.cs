using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Identity;

// Past-tense facts; ids and the few values a reader needs, never the record (03 §7).

public sealed record PlatformUserRegistered(Guid UserId, string Username, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PlatformUserLockedOut(Guid UserId, DateTimeOffset LockoutEndUtc, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PlatformUserUnlocked(Guid UserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PlatformUserPasswordChanged(Guid UserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PlatformUserDeactivated(Guid UserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record RefreshSessionOpened(Guid SessionId, Guid FamilyId, Guid UserId, Guid? TenantId, SessionKind Kind, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record RefreshSessionRotated(Guid SessionId, Guid SuccessorId, Guid FamilyId, Guid UserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record RefreshSessionRevoked(Guid SessionId, Guid FamilyId, Guid UserId, RevocationReason Reason, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>CP-04: the session is now bound to one branch; every branch-stamped transaction of the session carries it.</summary>
public sealed record RefreshSessionBranchSelected(Guid SessionId, Guid UserId, Guid? TenantId, Guid BranchId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>CP-03: the password was replaced after a one-time code proved possession of the registered mobile.</summary>
public sealed record PlatformUserPasswordRecovered(Guid UserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
