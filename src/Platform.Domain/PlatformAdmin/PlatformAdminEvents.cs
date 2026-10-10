using Platform.SharedKernel.Primitives;

namespace Platform.Domain.PlatformAdmin;

// Past-tense facts of platform administration (intake §11). Platform rows carry no tenant; the outbox records them
// with the tenant of the scope that raised them (or none for the anonymous wizard).

/// <summary>A laboratory became a tenant (SA-04 manual create or CP-02 wizard); its master data was copied in the same transaction (BR-25).</summary>
public sealed record TenantProvisioned(Guid TenantId, string PortalSlug, string Source, Guid PlanId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TenantActivated(Guid TenantId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Every user of the client is blocked at sign-in from now on and their live sessions were revoked.</summary>
public sealed record TenantDeactivated(Guid TenantId, string Reason, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TenantPlanChanged(Guid TenantId, Guid FromPlanId, Guid ToPlanId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record WalletToppedUp(Guid WalletId, Guid TenantId, Guid TransactionId, decimal Amount, string Reference, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>A subscription charge or a metered usage debit; <paramref name="Kind"/> is the transaction kind name.</summary>
public sealed record WalletDebited(Guid WalletId, Guid TenantId, Guid TransactionId, string Kind, decimal Amount, decimal BalanceAfter, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record WalletTopUpRequested(Guid WalletId, Guid TenantId, Guid RequestId, decimal Amount, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SubscriptionRequested(Guid SubscriptionId, Guid CountryId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SubscriptionVerified(Guid SubscriptionId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SubscriptionCompleted(Guid SubscriptionId, Guid TenantId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record CodeSystemImported(Guid CodeSystemId, string Name, string Version, int Inserted, int Updated, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
