/** Billing module contracts mirrored from `Platform.Contracts.Billing` (bootstrap until the OpenAPI generator lands, 01 §6.2). */

export type { Paged } from './paging';

export type PaymentMethod = 'Cash' | 'Card' | 'Pos' | 'Transfer';

export const PAYMENT_METHODS: readonly PaymentMethod[] = ['Cash', 'Card', 'Pos', 'Transfer'];

export type PaymentStatus = 'Posted' | 'Voided';

export const PAYMENT_STATUSES: readonly PaymentStatus[] = ['Posted', 'Voided'];

/** Derived from the balance (BR-14): remaining = patient share − posted payments + refunds. */
export type BalanceStatus = 'Paid' | 'Unpaid' | 'Partial' | 'RefundDue';

export type BatchKind = 'Group' | 'LabToLab';

// ---- requests ----------------------------------------------------------------------------------------------------

export interface RecordPaymentRequest {
  readonly visitId: string;
  readonly amount: number;
  readonly method: PaymentMethod;
  readonly reference: string | null;
  readonly treasuryId: string;
  readonly deviceId: string;
}

export interface VoidPaymentRequest {
  readonly reason: string;
}

export interface RecordRefundRequest {
  readonly visitId: string;
  readonly amount: number;
  readonly method: PaymentMethod;
  readonly reference: string | null;
  readonly reasonId: string;
  readonly note: string | null;
  readonly treasuryId: string;
  readonly deviceId: string;
}

export interface PayBatchRequest {
  readonly amount: number;
  readonly method: PaymentMethod;
  readonly reference: string | null;
  readonly treasuryId: string;
  readonly deviceId: string;
}

export interface CloseCashDrawerRequest {
  readonly treasuryId: string;
  readonly cashierUserId: string | null;
  readonly periodFromUtc: string;
  readonly periodToUtc: string;
  readonly countedCash: number | null;
  readonly note: string | null;
}

// ---- command responses ----------------------------------------------------------------------------------------------

export interface PaymentCreated {
  readonly id: string;
  readonly receiptNumber: string;
}

export interface RefundCreated {
  readonly id: string;
  readonly number: string;
}

export interface CashClosingCreated {
  readonly id: string;
  readonly number: string;
}

export interface BatchMemberPayment {
  readonly visitId: string;
  readonly accession: string;
  readonly paymentId: string;
  readonly receiptNumber: string;
  readonly amount: number;
}

export interface BatchPaymentResult {
  readonly batchId: string;
  readonly amount: number;
  readonly payments: readonly BatchMemberPayment[];
}

// ---- registers --------------------------------------------------------------------------------------------------------

export interface PaymentListItem {
  readonly id: string;
  readonly receiptNumber: string;
  readonly visitId: string;
  readonly accession: string | null;
  readonly amount: number;
  readonly method: PaymentMethod;
  readonly reference: string | null;
  readonly treasuryId: string;
  readonly treasuryCode: string;
  readonly treasuryNameEn: string;
  readonly treasuryNameAr: string;
  readonly cashierUserId: string;
  readonly cashierName: string;
  readonly branchId: string;
  readonly deviceId: string;
  readonly batchId: string | null;
  readonly paidAtUtc: string;
  readonly status: PaymentStatus;
  readonly isFrozen: boolean;
  readonly voidReason: string | null;
  readonly voidedByName: string | null;
  readonly voidedAtUtc: string | null;
}

export interface RefundListItem {
  readonly id: string;
  readonly number: string;
  readonly visitId: string;
  readonly accession: string | null;
  readonly amount: number;
  readonly method: PaymentMethod;
  readonly reference: string | null;
  readonly reasonId: string;
  readonly reasonNameEn: string | null;
  readonly reasonNameAr: string | null;
  readonly note: string | null;
  readonly treasuryId: string;
  readonly treasuryCode: string;
  readonly treasuryNameEn: string;
  readonly treasuryNameAr: string;
  readonly cashierUserId: string;
  readonly cashierName: string;
  readonly branchId: string;
  readonly deviceId: string;
  readonly refundedAtUtc: string;
}

export interface CashClosingTotals {
  readonly cashPayments: number;
  readonly cardPayments: number;
  readonly posPayments: number;
  readonly transferPayments: number;
  readonly totalPayments: number;
  readonly paymentCount: number;
  readonly cashRefunds: number;
  readonly cardRefunds: number;
  readonly posRefunds: number;
  readonly transferRefunds: number;
  readonly totalRefunds: number;
  readonly refundCount: number;
  readonly expectedCash: number;
  readonly outstandingBalance: number;
  readonly discountTotal: number;
  readonly visitCount: number;
}

export interface CashClosingListItem {
  readonly id: string;
  readonly number: string;
  readonly treasuryId: string;
  readonly treasuryCode: string;
  readonly treasuryNameEn: string;
  readonly treasuryNameAr: string;
  readonly cashierUserId: string | null;
  readonly cashierName: string | null;
  readonly periodFromUtc: string;
  readonly periodToUtc: string;
  readonly totalPayments: number;
  readonly totalRefunds: number;
  readonly expectedCash: number;
  readonly countedCash: number | null;
  readonly difference: number | null;
  readonly closedByUserId: string;
  readonly closedByName: string;
  readonly closedAtUtc: string;
}

export type DrawerMovementKind = 'Payment' | 'Refund';

export interface DrawerMovement {
  readonly kind: DrawerMovementKind;
  readonly id: string;
  readonly number: string;
  readonly visitId: string;
  readonly accession: string | null;
  readonly method: PaymentMethod;
  readonly amount: number;
  readonly atUtc: string;
  readonly cashierUserId: string;
  readonly cashierName: string;
  readonly status: PaymentStatus;
}

export interface DrawerCashier {
  readonly userId: string;
  readonly name: string;
}

export interface BillingTreasuryItem {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly branchId: string;
  readonly isActive: boolean;
}

// ---- accounts ------------------------------------------------------------------------------------------------------------

export interface VisitAccountView {
  readonly visitId: string;
  readonly accession: string;
  readonly patientId: string;
  readonly patientNumber: string | null;
  readonly patientName: string | null;
  readonly branchId: string;
  readonly payerId: string | null;
  readonly payerNameEn: string | null;
  readonly payerNameAr: string | null;
  readonly batchId: string | null;
  readonly batchNumber: string | null;
  readonly batchKind: BatchKind | null;
  readonly visitStatus: string;
  readonly registeredAtUtc: string;
  readonly totalListPrice: number;
  readonly discountTotal: number;
  readonly patientShareTotal: number;
  readonly payerShareTotal: number;
  readonly paid: number;
  readonly refunded: number;
  readonly remaining: number;
  readonly refundable: number;
  readonly balanceStatus: BalanceStatus;
  readonly billedThroughClaims: boolean;
  readonly currency: string;
  readonly payments: readonly PaymentListItem[];
  readonly refunds: readonly RefundListItem[];
}

export interface BatchMemberView {
  readonly visitId: string;
  readonly accession: string;
  readonly patientId: string;
  readonly patientNumber: string | null;
  readonly patientName: string | null;
  readonly totalListPrice: number;
  readonly discountTotal: number;
  readonly patientShareTotal: number;
  readonly payerShareTotal: number;
  readonly paid: number;
  readonly refunded: number;
  readonly remaining: number;
  readonly balanceStatus: BalanceStatus;
}

export interface BatchAccountView {
  readonly batchId: string;
  readonly number: string;
  readonly kind: BatchKind;
  readonly payerId: string | null;
  readonly payerNameEn: string | null;
  readonly payerNameAr: string | null;
  readonly billedThroughClaims: boolean;
  readonly members: readonly BatchMemberView[];
  readonly totalListPrice: number;
  readonly discountTotal: number;
  readonly patientShareTotal: number;
  readonly payerShareTotal: number;
  readonly paid: number;
  readonly refunded: number;
  readonly remaining: number;
  readonly currency: string;
}

export interface CashClosingPreview {
  readonly treasuryId: string;
  readonly treasuryCode: string;
  readonly treasuryNameEn: string;
  readonly treasuryNameAr: string;
  readonly cashierUserId: string | null;
  readonly cashierName: string | null;
  readonly periodFromUtc: string;
  readonly periodToUtc: string;
  readonly totals: CashClosingTotals;
  readonly movements: readonly DrawerMovement[];
  readonly movementsTruncated: boolean;
  readonly overlapsClosedPeriod: boolean;
  readonly currency: string;
}

// ---- documents --------------------------------------------------------------------------------------------------------------

export interface DocumentLetterhead {
  readonly labNameEn: string;
  readonly labNameAr: string;
  readonly branchNameEn: string | null;
  readonly branchNameAr: string | null;
}

export interface PaymentReceiptDocument {
  readonly letterhead: DocumentLetterhead;
  readonly paymentId: string;
  readonly receiptNumber: string;
  readonly status: PaymentStatus;
  readonly paidAtUtc: string;
  readonly visitId: string;
  readonly accession: string;
  readonly patientNumber: string | null;
  readonly patientName: string | null;
  readonly amount: number;
  readonly method: PaymentMethod;
  readonly reference: string | null;
  readonly treasuryCode: string;
  readonly treasuryNameEn: string;
  readonly treasuryNameAr: string;
  readonly cashierName: string;
  readonly patientShareTotal: number;
  readonly paid: number;
  readonly refunded: number;
  readonly remaining: number;
  readonly voidReason: string | null;
  readonly voidedAtUtc: string | null;
  readonly currency: string;
  readonly printedBy: string;
  readonly printedAtUtc: string;
}

export interface InvoiceLine {
  readonly lineId: string;
  readonly serviceId: string;
  readonly serviceCode: string | null;
  readonly serviceNameEn: string | null;
  readonly serviceNameAr: string | null;
  readonly status: string;
  readonly listPrice: number;
  readonly discount: number;
  readonly patientShare: number;
  readonly payerShare: number;
}

export interface InvoiceMovement {
  readonly number: string;
  readonly atUtc: string;
  readonly method: PaymentMethod;
  readonly amount: number;
}

export interface InvoiceDocument {
  readonly letterhead: DocumentLetterhead;
  readonly visitId: string;
  readonly accession: string;
  readonly registeredAtUtc: string;
  readonly patientNumber: string | null;
  readonly patientName: string | null;
  readonly payerNameEn: string | null;
  readonly payerNameAr: string | null;
  readonly lines: readonly InvoiceLine[];
  readonly totalListPrice: number;
  readonly discountTotal: number;
  readonly patientShareTotal: number;
  readonly payerShareTotal: number;
  readonly payments: readonly InvoiceMovement[];
  readonly refunds: readonly InvoiceMovement[];
  readonly paid: number;
  readonly refunded: number;
  readonly remaining: number;
  readonly currency: string;
  readonly printedBy: string;
  readonly printedAtUtc: string;
}

export interface RefundReceiptDocument {
  readonly letterhead: DocumentLetterhead;
  readonly refundId: string;
  readonly number: string;
  readonly refundedAtUtc: string;
  readonly visitId: string;
  readonly accession: string;
  readonly patientNumber: string | null;
  readonly patientName: string | null;
  readonly amount: number;
  readonly method: PaymentMethod;
  readonly reference: string | null;
  readonly reasonNameEn: string | null;
  readonly reasonNameAr: string | null;
  readonly note: string | null;
  readonly treasuryCode: string;
  readonly treasuryNameEn: string;
  readonly treasuryNameAr: string;
  readonly cashierName: string;
  readonly currency: string;
  readonly printedBy: string;
  readonly printedAtUtc: string;
}

export interface CashClosingSheetDocument {
  readonly letterhead: DocumentLetterhead;
  readonly closingId: string;
  readonly number: string;
  readonly treasuryCode: string;
  readonly treasuryNameEn: string;
  readonly treasuryNameAr: string;
  readonly cashierUserId: string | null;
  readonly cashierName: string | null;
  readonly periodFromUtc: string;
  readonly periodToUtc: string;
  readonly totals: CashClosingTotals;
  readonly countedCash: number | null;
  readonly difference: number | null;
  readonly note: string | null;
  readonly closedByName: string;
  readonly closedAtUtc: string;
  readonly movements: readonly DrawerMovement[];
  readonly movementsTruncated: boolean;
  readonly currency: string;
  readonly printedBy: string;
  readonly printedAtUtc: string;
}

// ---- filters ---------------------------------------------------------------------------------------------------------------

export interface PaymentRegisterFilter {
  readonly accession: string | null;
  readonly fromUtc: string | null;
  readonly toUtc: string | null;
  readonly treasuryId: string | null;
  readonly method: PaymentMethod | null;
  readonly status: PaymentStatus | null;
  readonly page: number;
  readonly pageSize: number;
}

export interface RefundRegisterFilter {
  readonly accession: string | null;
  readonly fromUtc: string | null;
  readonly toUtc: string | null;
  readonly treasuryId: string | null;
  readonly method: PaymentMethod | null;
  readonly page: number;
  readonly pageSize: number;
}

export interface CashClosingRegisterFilter {
  readonly treasuryId: string | null;
  readonly fromUtc: string | null;
  readonly toUtc: string | null;
  readonly page: number;
  readonly pageSize: number;
}

export interface DrawerScopeFilter {
  readonly treasuryId: string;
  readonly cashierUserId: string | null;
  readonly fromUtc: string;
  readonly toUtc: string;
}

/**
 * A catalogue reason as `GET /api/v1/catalogue/reasons?kind=Refund` returns it (Catalogue B, CP-53). Declared here so
 * Billing does not depend on another feature's contracts file (01 §6.2); the shape is the agreed one.
 */
export interface BillingReasonOption {
  readonly id: string;
  readonly kind: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly isActive: boolean;
}

export const BILLING_API = {
  payments: '/api/v1/billing/payments',
  refunds: '/api/v1/billing/refunds',
  visits: '/api/v1/billing/visits',
  batches: '/api/v1/billing/batches',
  cashClosings: '/api/v1/billing/cash-closings',
  myTreasuries: '/api/v1/billing/my-treasuries',
  treasuries: '/api/v1/billing/treasuries',
  refundReasons: '/api/v1/catalogue/reasons',
} as const;

/** Permission keys of the module (PermissionCatalog.Billing). `approve` records refunds until the CP-37 special right lands. */
export const BILLING_PERMISSIONS = {
  view: 'billing.view',
  pay: 'billing.create',
  refund: 'billing.approve',
  void: 'billing.cancel',
  close: 'billing.manage',
  export: 'billing.export',
} as const;
