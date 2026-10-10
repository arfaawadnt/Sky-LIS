namespace Platform.Contracts.Billing;

// Request and response shapes of the Billing module (intake LIS-INT-BIL-001). Validation here is shape only; the
// aggregates decide. Enum values travel as their names: methods Cash | Card | Pos | Transfer, payment status
// Posted | Voided, balance status Paid | Unpaid | Partial | RefundDue.

// ---- requests -------------------------------------------------------------------------------------------------

/// <summary>T1 (CP-23; BR-26 from the registration page). The device id is the browser's identity (BR-27).</summary>
public sealed record RecordPaymentRequest(Guid VisitId, decimal Amount, string Method, string? Reference, Guid TreasuryId, string DeviceId);

public sealed record VoidPaymentRequest(string Reason);

/// <summary>T3 (CP-24): <paramref name="ReasonId"/> is a catalogue reason of kind Refund.</summary>
public sealed record RecordRefundRequest(Guid VisitId, decimal Amount, string Method, string? Reference, Guid ReasonId, string? Note, Guid TreasuryId, string DeviceId);

/// <summary>T4 (CP-27): the amount is distributed across the members in accession order.</summary>
public sealed record PayBatchRequest(decimal Amount, string Method, string? Reference, Guid TreasuryId, string DeviceId);

/// <summary>T5 (CP-30): <paramref name="CashierUserId"/> null = every cashier; the period is half-open.</summary>
public sealed record CloseCashDrawerRequest(Guid TreasuryId, Guid? CashierUserId, DateTimeOffset PeriodFromUtc, DateTimeOffset PeriodToUtc, decimal? CountedCash, string? Note);

// ---- responses of commands ------------------------------------------------------------------------------------

public sealed record PaymentCreated(Guid Id, string ReceiptNumber);

public sealed record RefundCreated(Guid Id, string Number);

public sealed record CashClosingCreated(Guid Id, string Number);

public sealed record BatchMemberPayment(Guid VisitId, string Accession, Guid PaymentId, string ReceiptNumber, decimal Amount);

public sealed record BatchPaymentResult(Guid BatchId, decimal Amount, IReadOnlyList<BatchMemberPayment> Payments);

// ---- registers --------------------------------------------------------------------------------------------------

public sealed record PaymentListItem(
    Guid Id,
    string ReceiptNumber,
    Guid VisitId,
    string? Accession,
    decimal Amount,
    string Method,
    string? Reference,
    Guid TreasuryId,
    string TreasuryCode,
    string TreasuryNameEn,
    string TreasuryNameAr,
    Guid CashierUserId,
    string CashierName,
    Guid BranchId,
    string DeviceId,
    Guid? BatchId,
    DateTimeOffset PaidAtUtc,
    string Status,
    bool IsFrozen,
    string? VoidReason,
    string? VoidedByName,
    DateTimeOffset? VoidedAtUtc);

public sealed record RefundListItem(
    Guid Id,
    string Number,
    Guid VisitId,
    string? Accession,
    decimal Amount,
    string Method,
    string? Reference,
    Guid ReasonId,
    string? ReasonNameEn,
    string? ReasonNameAr,
    string? Note,
    Guid TreasuryId,
    string TreasuryCode,
    string TreasuryNameEn,
    string TreasuryNameAr,
    Guid CashierUserId,
    string CashierName,
    Guid BranchId,
    string DeviceId,
    DateTimeOffset RefundedAtUtc);

public sealed record CashClosingTotalsView(
    decimal CashPayments,
    decimal CardPayments,
    decimal PosPayments,
    decimal TransferPayments,
    decimal TotalPayments,
    int PaymentCount,
    decimal CashRefunds,
    decimal CardRefunds,
    decimal PosRefunds,
    decimal TransferRefunds,
    decimal TotalRefunds,
    int RefundCount,
    decimal ExpectedCash,
    decimal OutstandingBalance,
    decimal DiscountTotal,
    int VisitCount);

public sealed record CashClosingListItem(
    Guid Id,
    string Number,
    Guid TreasuryId,
    string TreasuryCode,
    string TreasuryNameEn,
    string TreasuryNameAr,
    Guid? CashierUserId,
    string? CashierName,
    DateTimeOffset PeriodFromUtc,
    DateTimeOffset PeriodToUtc,
    decimal TotalPayments,
    decimal TotalRefunds,
    decimal ExpectedCash,
    decimal? CountedCash,
    decimal? Difference,
    Guid ClosedByUserId,
    string ClosedByName,
    DateTimeOffset ClosedAtUtc);

/// <summary>One movement of a drawer: <paramref name="Kind"/> is Payment or Refund; <paramref name="Status"/> Posted/Voided (refunds: Posted).</summary>
public sealed record DrawerMovement(string Kind, Guid Id, string Number, Guid VisitId, string? Accession, string Method, decimal Amount, DateTimeOffset AtUtc, Guid CashierUserId, string CashierName, string Status);

/// <summary>A cashier who moved money on a treasury in a period (the closing page's cashier filter).</summary>
public sealed record DrawerCashier(Guid UserId, string Name);

/// <summary>A treasury of the session branch as the billing pages offer it.</summary>
public sealed record BillingTreasuryItem(Guid Id, string Code, string NameEn, string NameAr, Guid BranchId, bool IsActive);

// ---- visit and batch accounts -------------------------------------------------------------------------------------

public sealed record VisitAccountView(
    Guid VisitId,
    string Accession,
    Guid PatientId,
    string? PatientNumber,
    string? PatientName,
    Guid BranchId,
    Guid? PayerId,
    string? PayerNameEn,
    string? PayerNameAr,
    Guid? BatchId,
    string? BatchNumber,
    string? BatchKind,
    string VisitStatus,
    DateTimeOffset RegisteredAtUtc,
    decimal TotalListPrice,
    decimal DiscountTotal,
    decimal PatientShareTotal,
    decimal PayerShareTotal,
    decimal Paid,
    decimal Refunded,
    decimal Remaining,
    decimal Refundable,
    string BalanceStatus,
    bool BilledThroughClaims,
    string Currency,
    IReadOnlyList<PaymentListItem> Payments,
    IReadOnlyList<RefundListItem> Refunds);

public sealed record BatchMemberView(
    Guid VisitId,
    string Accession,
    Guid PatientId,
    string? PatientNumber,
    string? PatientName,
    decimal TotalListPrice,
    decimal DiscountTotal,
    decimal PatientShareTotal,
    decimal PayerShareTotal,
    decimal Paid,
    decimal Refunded,
    decimal Remaining,
    string BalanceStatus);

public sealed record BatchAccountView(
    Guid BatchId,
    string Number,
    string Kind,
    Guid? PayerId,
    string? PayerNameEn,
    string? PayerNameAr,
    bool BilledThroughClaims,
    IReadOnlyList<BatchMemberView> Members,
    decimal TotalListPrice,
    decimal DiscountTotal,
    decimal PatientShareTotal,
    decimal PayerShareTotal,
    decimal Paid,
    decimal Refunded,
    decimal Remaining,
    string Currency);

// ---- cash closing -------------------------------------------------------------------------------------------------

public sealed record CashClosingPreview(
    Guid TreasuryId,
    string TreasuryCode,
    string TreasuryNameEn,
    string TreasuryNameAr,
    Guid? CashierUserId,
    string? CashierName,
    DateTimeOffset PeriodFromUtc,
    DateTimeOffset PeriodToUtc,
    CashClosingTotalsView Totals,
    IReadOnlyList<DrawerMovement> Movements,
    bool MovementsTruncated,
    bool OverlapsClosedPeriod,
    string Currency);

// ---- documents (rendered by the SPA's print kit) ------------------------------------------------------------------

/// <summary>The bilingual header of every document: laboratory (tenant) and branch names.</summary>
public sealed record DocumentLetterhead(string LabNameEn, string LabNameAr, string? BranchNameEn, string? BranchNameAr);

public sealed record PaymentReceiptDocument(
    DocumentLetterhead Letterhead,
    Guid PaymentId,
    string ReceiptNumber,
    string Status,
    DateTimeOffset PaidAtUtc,
    Guid VisitId,
    string Accession,
    string? PatientNumber,
    string? PatientName,
    decimal Amount,
    string Method,
    string? Reference,
    string TreasuryCode,
    string TreasuryNameEn,
    string TreasuryNameAr,
    string CashierName,
    decimal PatientShareTotal,
    decimal Paid,
    decimal Refunded,
    decimal Remaining,
    string? VoidReason,
    DateTimeOffset? VoidedAtUtc,
    string Currency,
    string PrintedBy,
    DateTimeOffset PrintedAtUtc);

public sealed record InvoiceLine(Guid LineId, Guid ServiceId, string? ServiceCode, string? ServiceNameEn, string? ServiceNameAr, string Status, decimal ListPrice, decimal Discount, decimal PatientShare, decimal PayerShare);

public sealed record InvoiceMovement(string Number, DateTimeOffset AtUtc, string Method, decimal Amount);

public sealed record InvoiceDocument(
    DocumentLetterhead Letterhead,
    Guid VisitId,
    string Accession,
    DateTimeOffset RegisteredAtUtc,
    string? PatientNumber,
    string? PatientName,
    string? PayerNameEn,
    string? PayerNameAr,
    IReadOnlyList<InvoiceLine> Lines,
    decimal TotalListPrice,
    decimal DiscountTotal,
    decimal PatientShareTotal,
    decimal PayerShareTotal,
    IReadOnlyList<InvoiceMovement> Payments,
    IReadOnlyList<InvoiceMovement> Refunds,
    decimal Paid,
    decimal Refunded,
    decimal Remaining,
    string Currency,
    string PrintedBy,
    DateTimeOffset PrintedAtUtc);

public sealed record RefundReceiptDocument(
    DocumentLetterhead Letterhead,
    Guid RefundId,
    string Number,
    DateTimeOffset RefundedAtUtc,
    Guid VisitId,
    string Accession,
    string? PatientNumber,
    string? PatientName,
    decimal Amount,
    string Method,
    string? Reference,
    string? ReasonNameEn,
    string? ReasonNameAr,
    string? Note,
    string TreasuryCode,
    string TreasuryNameEn,
    string TreasuryNameAr,
    string CashierName,
    string Currency,
    string PrintedBy,
    DateTimeOffset PrintedAtUtc);

public sealed record CashClosingSheetDocument(
    DocumentLetterhead Letterhead,
    Guid ClosingId,
    string Number,
    string TreasuryCode,
    string TreasuryNameEn,
    string TreasuryNameAr,
    Guid? CashierUserId,
    string? CashierName,
    DateTimeOffset PeriodFromUtc,
    DateTimeOffset PeriodToUtc,
    CashClosingTotalsView Totals,
    decimal? CountedCash,
    decimal? Difference,
    string? Note,
    string ClosedByName,
    DateTimeOffset ClosedAtUtc,
    IReadOnlyList<DrawerMovement> Movements,
    bool MovementsTruncated,
    string Currency,
    string PrintedBy,
    DateTimeOffset PrintedAtUtc);

/// <summary>A generated file (CSV) the controller streams with its name and type.</summary>
public sealed record BillingExportFile(string FileName, string ContentType, byte[] Content);
