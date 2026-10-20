using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Billing;

// Past-tense facts of the Billing module (intake LIS-INT-BIL-001 §11): ids and the few values a reader needs.
// The outbox stamps the tenant; the audit ledger copies a Reason/Comment property as the change reason.

/// <summary>Money received on a visit (T1, or T4 per batch member with <paramref name="BatchId"/>).</summary>
public sealed record PaymentRecorded(
    Guid PaymentId,
    Guid VisitId,
    string ReceiptNumber,
    decimal Amount,
    string Method,
    Guid CashierUserId,
    Guid BranchId,
    Guid? BatchId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>A posted payment cancelled with a reason (T2); it no longer counts in the balance or the drawer.</summary>
public sealed record PaymentVoided(Guid PaymentId, Guid VisitId, string ReceiptNumber, decimal Amount, Guid ActorUserId, string Reason, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Money returned on a negative balance (T3, BR-14); <paramref name="Comment"/> is the cashier's note.</summary>
public sealed record RefundRecorded(
    Guid RefundId,
    Guid VisitId,
    string Number,
    decimal Amount,
    string Method,
    Guid ReasonId,
    Guid CashierUserId,
    Guid BranchId,
    string? Comment,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>The drawer of a branch reconciled for a cashier scope and period (T5, BR-16); the period is frozen afterwards.</summary>
public sealed record CashClosingClosed(
    Guid ClosingId,
    string Number,
    Guid BranchId,
    Guid? CashierUserId,
    DateTimeOffset PeriodFromUtc,
    DateTimeOffset PeriodToUtc,
    decimal ExpectedCash,
    decimal? CountedCash,
    decimal? Difference,
    Guid ClosedByUserId,
    string? Comment,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

// Payment method definitions (change request 2026-10-19, P-C10): the set-up list of the cash desks.

public sealed record PaymentMethodDefined(Guid MethodId, string Code, string Kind, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Names, kind, reference rule or order changed; the field changes carry before / after.</summary>
public sealed record PaymentMethodUpdated(Guid MethodId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PaymentMethodDeactivated(Guid MethodId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PaymentMethodReactivated(Guid MethodId, string Code, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
