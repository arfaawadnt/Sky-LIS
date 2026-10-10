using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Pricing;

// Past-tense facts (intake LIS-INT-PRC-001 §11); ids and the few values a reader needs. The outbox stamps the tenant.

public sealed record PayerCreated(Guid PayerId, string SearchKey, PayerType Type, bool IsDefault, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PayerDeactivated(Guid PayerId, string SearchKey, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PayerReactivated(Guid PayerId, string SearchKey, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ContractTierAdded(Guid PayerId, Guid ContractTierId, bool IsDefault, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ContractTierDeactivated(Guid PayerId, Guid ContractTierId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ContractTierReactivated(Guid PayerId, Guid ContractTierId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>A new base list, empty (L1) or copied from another list (L4, <paramref name="CopiedFromListId"/> set).</summary>
public sealed record BasePriceListCreated(Guid ListId, string Code, int Year, Guid? CopiedFromListId, int LineCount, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Prices of a list changed (L3). Contracts are not re-priced: Apply is explicit (BR-07).</summary>
public sealed record BasePriceListPricesChanged(Guid ListId, string Code, int Added, int Changed, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record BasePriceListActivated(Guid ListId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record BasePriceListDeactivated(Guid ListId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ContractFormulaSet(Guid ContractPricingId, Guid PayerId, Guid ContractTierId, int ListCount, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>A tier's derived prices were recomputed (C2, L6); excluded prices were kept (BR-07).</summary>
public sealed record ContractPricingApplied(
    Guid ContractPricingId,
    Guid PayerId,
    Guid ContractTierId,
    int Priced,
    int Added,
    int Updated,
    int Removed,
    int ExcludedKept,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ContractPriceNegotiated(Guid ContractPricingId, Guid ContractTierId, Guid ServiceId, decimal PatientShare, decimal PayerShare, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ContractPriceExclusionCleared(Guid ContractPricingId, Guid ContractTierId, Guid ServiceId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ContractPricingCopied(Guid ContractPricingId, Guid ContractTierId, Guid SourceContractPricingId, Guid SourceTierId, decimal Factor, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
