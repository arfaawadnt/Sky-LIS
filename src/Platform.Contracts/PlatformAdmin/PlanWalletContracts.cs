namespace Platform.Contracts.PlatformAdmin;

// Plans and wallets (SA-05) and the laboratory's own wallet (CP-66). Kinds: TopUp, Subscription, Usage.
// Top-up request statuses: Pending, Completed, Declined. Amounts: positive credit, negative debit.

public sealed record PlanListItem(
    Guid Id,
    string Code,
    string NameEn,
    string NameAr,
    decimal MonthlyFee,
    string CurrencyCode,
    int MaxBranches,
    int MaxUsers,
    int SmsQuota,
    int MaxAnalysers,
    bool IsActive,
    int ClientCount);

/// <summary>Create and edit a plan; the code is ignored on edit (natural key).</summary>
public sealed record PlanRequest(
    string Code,
    string NameEn,
    string NameAr,
    decimal MonthlyFee,
    string CurrencyCode,
    int MaxBranches,
    int MaxUsers,
    int SmsQuota,
    int MaxAnalysers);

public sealed record TopUpRequestItem(
    Guid Id,
    decimal Amount,
    string? Note,
    string Status,
    DateTimeOffset RequestedAtUtc,
    Guid RequestedByUserId,
    string? RequestedByName,
    DateTimeOffset? DecidedAtUtc);

public sealed record WalletDetail(
    Guid WalletId,
    Guid TenantId,
    string ClientNameEn,
    string ClientNameAr,
    decimal Balance,
    string CurrencyCode,
    DateTimeOffset NextChargeAtUtc,
    Guid PlanId,
    string PlanCode,
    string PlanNameEn,
    string PlanNameAr,
    decimal MonthlyFee,
    string PlanCurrencyCode,
    int SmsQuota,
    int SmsUsedThisMonth,
    IReadOnlyList<TopUpRequestItem> PendingTopUpRequests);

public sealed record WalletTransactionItem(
    Guid Id,
    string Kind,
    decimal Amount,
    decimal BalanceAfter,
    string Reference,
    string? Note,
    DateTimeOffset AtUtc,
    Guid? ByUserId,
    string? ByName);

/// <summary>SA-05 "Top up": amount, payment reference, optional note and the laboratory's pending request it settles.</summary>
public sealed record TopUpWalletRequest(decimal Amount, string Reference, string? Note, Guid? TopUpRequestId);

/// <summary>CP-66 "Request top-up" (client tenant, <c>wallet.create</c>).</summary>
public sealed record CreateTopUpRequest(decimal Amount, string? Note);
