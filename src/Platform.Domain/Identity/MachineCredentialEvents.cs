using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Identity;

// Past-tense facts of machine credentials (intake LIS-INT-INT-001 §11). Never the secret or its hash.

/// <summary>M1: a client id and a secret were issued for <paramref name="TenantId"/>; the service identity holds exactly the scopes.</summary>
public sealed record MachineCredentialIssued(Guid CredentialId, Guid TenantId, string ClientId, Guid ServiceUserId, IReadOnlyList<string> Scopes, DateTimeOffset ExpiresAtUtc, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>M2: the secret was replaced; the previous secret is dead and the credential's live sessions were ended.</summary>
public sealed record MachineCredentialSecretRotated(Guid CredentialId, Guid TenantId, string ClientId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>M3: the credential can no longer exchange or refresh tokens; its service user was deactivated.</summary>
public sealed record MachineCredentialRevoked(Guid CredentialId, Guid TenantId, string ClientId, Guid RevokedByUserId, string? Reason, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
