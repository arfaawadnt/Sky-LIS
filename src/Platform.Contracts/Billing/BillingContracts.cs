namespace Platform.Contracts.Billing;

// Request and response shapes of the Billing module (intake LIS-INT-BIL-001). Validation here is shape only; the
// aggregates decide. Enum values travel as their names: methods Cash | Card | Pos | Transfer, payment status
// Posted | Voided, balance status Paid | Unpaid | Partial | RefundDue.

// ---- requests -------------------------------------------------------------------------------------------------

/// <summary>
/// T1 (CP-23; BR-26 from the registration page). The device id is the browser's identity (BR-27). <paramref name="MethodCode"/>
/// (change request 2026-10-19) names the laboratory's payment method definition: when present its kind and reference rule
/// apply; without it <paramref name="Method"/> is the kind and every kind but cash needs a reference.
/// </summary>
public sealed record RecordPaymentRequest(Guid VisitId, decimal Amount, string Method, string? Reference, Guid TreasuryId, string DeviceId, string? MethodCode = null);

public sealed record VoidPaymentRequest(string Reason);

/// <summary>T3 (CP-24): <paramref name="ReasonId"/> is a catalogue reason of kind Refund.</summary>
public sealed record RecordRefundRequest(Guid VisitId, decimal Amount, string Method, string? Reference, Guid ReasonId, string? Note, Guid TreasuryId, string DeviceId, string? MethodCode = null);

/// <summary>T4 (CP-27): the amount is distributed across the members in accession order.</summary>
public sealed record PayBatchRequest(decimal Amount, string Method, string? Reference, Guid TreasuryId, string DeviceId, string? MethodCode = null);

// ---- payment method definitions (set-up list CP-30b, change request 2026-10-19) -----------------------------------

/// <summary><paramref name="Kind"/> is Cash | Card | Pos | Transfer; <paramref name="Sequence"/> orders the pick lists (default: after the last).</summary>
public sealed record PaymentMethodRequest(string Code, string NameEn, string NameAr, string Kind, bool RequiresReference, int? Sequence);

/// <summary>Everything but the code, which never changes.</summary>
public sealed record UpdatePaymentMethodRequest(string NameEn, string NameAr, string Kind, bool RequiresReference, int Sequence);

public sealed record PaymentMethodListItem(Guid Id, string Code, string NameEn, string NameAr, string Kind, bool RequiresReference, bool IsActive, int Sequence);

/// <summary>An active method as the cash desk and the registration page offer it, in sequence order.</summary>
public sealed record PaymentMethodOption(Guid Id, string Code, string NameEn, string NameAr, string Kind, bool RequiresReference);

/// <summary>The defaults endpoint: how many of the four default methods were added (0 when all existed).</summary>
public sealed record PaymentMethodDefaultsResult(int Created);

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
    DateTimeOffset? VoidedAtUtc,
    string? MethodCode = null,
    string? MethodNameEn = null,
    string? MethodNameAr = null);

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
    DateTimeOffset RefundedAtUtc,
    string? MethodCode = null,
    string? MethodNameEn = null,
    string? MethodNameAr = null);

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

/// <summary>The print parameters the cash desk needs (CP-57 <c>autoPrintReceipt</c>), readable with <c>billing.view</c> (UAT A-05: a cashier holds no <c>settings.view</c>).</summary>
public sealed record BillingPrintSettings(bool AutoPrintReceipt);

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
    DateTimeOffset PrintedAtUtc,
    string? MethodCode = null,
    string? MethodNameEn = null,
    string? MethodNameAr = null);

public sealed record InvoiceLine(Guid LineId, Guid ServiceId, string? ServiceCode, string? ServiceNameEn, string? ServiceNameAr, string Status, decimal ListPrice, decimal Discount, decimal PatientShare, decimal PayerShare);

public sealed record InvoiceMovement(string Number, DateTimeOffset AtUtc, string Method, decimal Amount, string? MethodCode = null, string? MethodNameEn = null, string? MethodNameAr = null);

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
    DateTimeOffset PrintedAtUtc,
    string? MethodCode = null,
    string? MethodNameEn = null,
    string? MethodNameAr = null);

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

// ---- collections (treasury) report ---------------------------------------------------------------------------------

/// <summary>Payments in, refunds out and the net of one method on a report row.</summary>
public sealed record CollectionsMethodView(decimal Payments, decimal Refunds, decimal Net);

/// <summary>
/// One row of the collections report: <paramref name="Key"/> is the branch id, the cashier's user id or the method name
/// (<c>Cash | Card | Pos | Transfer</c>) by the grouping; labels are the branch's bilingual names, the cashier's display
/// name in both, or <see langword="null"/> for a method (the SPA translates the method). Counts exclude voided payments.
/// </summary>
public sealed record CollectionsReportRow(
    string Key,
    string? LabelEn,
    string? LabelAr,
    int PaymentCount,
    int RefundCount,
    CollectionsMethodView Cash,
    CollectionsMethodView Card,
    CollectionsMethodView Pos,
    CollectionsMethodView Transfer,
    decimal TotalPayments,
    decimal TotalRefunds,
    decimal Net);

/// <summary>
/// The treasury (collections) report of the Payments register (A4): <paramref name="Scope"/> User | Branch, <paramref name="GroupBy"/>
/// Branch | User | Method, the period as whole laboratory days (<paramref name="TimeZone"/>) and its UTC bounds (half-open),
/// the cashier in user scope, the rows and the grand total.
/// </summary>
public sealed record CollectionsReportDocument(
    DocumentLetterhead Letterhead,
    string Scope,
    string GroupBy,
    DateOnly FromDay,
    DateOnly ToDay,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string TimeZone,
    string? CashierName,
    IReadOnlyList<CollectionsReportRow> Rows,
    CollectionsReportRow Totals,
    string Currency,
    string PrintedBy,
    DateTimeOffset PrintedAtUtc);

/// <summary>A generated file (CSV) the controller streams with its name and type.</summary>
public sealed record BillingExportFile(string FileName, string ContentType, byte[] Content);
