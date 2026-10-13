using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Treasury;

// Past-tense facts (intake §11); ids and the few values a reader needs. The outbox stamps the tenant.

public sealed record TreasuryCreated(Guid TreasuryId, string Code, Guid BranchId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>T7 Rename changed the name (validation v0.5.0 O-5); the field changes carry before / after.</summary>
public sealed record TreasuryUpdated(Guid TreasuryId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TreasuryDeactivated(Guid TreasuryId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TreasuryReactivated(Guid TreasuryId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TreasuryAccessRequested(Guid RequestId, string Number, Guid TreasuryId, Guid RequestedByUserId, string DeviceId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Finance reacts: payments on this device may use the treasury (policy "when access approved then enable the device").</summary>
public sealed record TreasuryAccessApproved(Guid RequestId, string Number, Guid TreasuryId, Guid RequestedByUserId, Guid ApprovedByUserId, string DeviceId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TreasuryAccessRejected(Guid RequestId, string Number, Guid TreasuryId, Guid RequestedByUserId, Guid RejectedByUserId, string Reason, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TreasuryAccessRevoked(Guid RequestId, string Number, Guid TreasuryId, Guid RequestedByUserId, Guid RevokedByUserId, string DeviceId, string Reason, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TreasuryAccessCancelled(Guid RequestId, string Number, Guid TreasuryId, Guid RequestedByUserId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
