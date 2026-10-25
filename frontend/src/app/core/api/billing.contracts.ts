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
  /** The laboratory's payment method definition (CP-30b); its kind and reference rule apply when present. */
  readonly methodCode?: string | null;
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
  readonly methodCode?: string | null;
}

export interface PayBatchRequest {
  readonly amount: number;
  readonly method: PaymentMethod;
  readonly reference: string | null;
  readonly methodCode?: string | null;
}

/**
 * Closes a cashier's drawer on the session branch (LIS-CR-004): `cashierUserId` null = every cashier of the branch
 * (`billing.manage`); the server takes the branch from the session.
 */
export interface CloseCashDrawerRequest {
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
  readonly cashierUserId: string;
  readonly cashierName: string;
  readonly branchId: string;
  readonly batchId: string | null;
  readonly paidAtUtc: string;
  readonly status: PaymentStatus;
  readonly isFrozen: boolean;
  readonly voidReason: string | null;
  readonly voidedByName: string | null;
  readonly voidedAtUtc: string | null;
  /** The definition's code and names at posting time (CP-30b); null on legacy rows, shown as the kind. */
  readonly methodCode?: string | null;
  readonly methodNameEn?: string | null;
  readonly methodNameAr?: string | null;
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
  readonly cashierUserId: string;
  readonly cashierName: string;
  readonly branchId: string;
  readonly refundedAtUtc: string;
  readonly methodCode?: string | null;
  readonly methodNameEn?: string | null;
  readonly methodNameAr?: string | null;
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
  /** Cash ledger (LIS-CR-011): the cash in the drawer when the period starts and the cash entries in / out of it; 0 on closings made before. */
  readonly openingBalance?: number;
  readonly cashIn?: number;
  readonly cashOut?: number;
  readonly entryCount?: number;
}

export interface CashClosingListItem {
  readonly id: string;
  readonly number: string;
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

/** A cash entry of the cash ledger (LIS-CR-011) is a movement too: CashIn / CashOut, without a visit. */
export type DrawerMovementKind = 'Payment' | 'Refund' | 'CashIn' | 'CashOut';

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
  /** A cash entry's reason (LIS-CR-011). */
  readonly text?: string | null;
}

export interface DrawerCashier {
  readonly userId: string;
  readonly name: string;
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
  readonly methodCode?: string | null;
  readonly methodNameEn?: string | null;
  readonly methodNameAr?: string | null;
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
  readonly methodCode?: string | null;
  readonly methodNameEn?: string | null;
  readonly methodNameAr?: string | null;
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
  readonly cashierName: string;
  readonly currency: string;
  readonly printedBy: string;
  readonly printedAtUtc: string;
  readonly methodCode?: string | null;
  readonly methodNameEn?: string | null;
  readonly methodNameAr?: string | null;
}

export interface CashClosingSheetDocument {
  readonly letterhead: DocumentLetterhead;
  readonly closingId: string;
  readonly number: string;
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

// ---- collections report (change request 2026-10-13, item 2) ------------------------------------------------------

/** Own posted payments and refunds, or every cashier of the session branch (needs the special right `canViewBranchCollections`). */
export type CollectionsReportScope = 'User' | 'Branch';

export type CollectionsReportGrouping = 'Branch' | 'User' | 'Method';

export const COLLECTIONS_REPORT_GROUPINGS: readonly CollectionsReportGrouping[] = ['Branch', 'User', 'Method'];

/** `fromDay`/`toDay` are laboratory-calendar days (`yyyy-MM-dd`, inclusive); null = today. */
export interface CollectionsReportRequest {
  readonly fromDay: string | null;
  readonly toDay: string | null;
  readonly scope: CollectionsReportScope;
  readonly groupBy: CollectionsReportGrouping;
}

export interface CollectionsMethodView {
  readonly payments: number;
  readonly refunds: number;
  readonly net: number;
}

/** `key` is the branch id, the cashier's user id or the method name by the grouping; labels are null for a method (translated here). */
export interface CollectionsReportRow {
  readonly key: string;
  readonly labelEn: string | null;
  readonly labelAr: string | null;
  readonly paymentCount: number;
  readonly refundCount: number;
  readonly cash: CollectionsMethodView;
  readonly card: CollectionsMethodView;
  readonly pos: CollectionsMethodView;
  readonly transfer: CollectionsMethodView;
  readonly totalPayments: number;
  readonly totalRefunds: number;
  readonly net: number;
}

export interface CollectionsReportDocument {
  readonly letterhead: DocumentLetterhead;
  readonly scope: CollectionsReportScope;
  readonly groupBy: CollectionsReportGrouping;
  readonly fromDay: string;
  readonly toDay: string;
  readonly fromUtc: string;
  readonly toUtc: string;
  readonly timeZone: string;
  readonly cashierName: string | null;
  readonly rows: readonly CollectionsReportRow[];
  readonly totals: CollectionsReportRow;
  readonly currency: string;
  readonly printedBy: string;
  readonly printedAtUtc: string;
}

/** The two CP-37 special rights the report dialog reads from `GET /api/v1/organization/me/privileges`. */
export interface CollectionsReportPrivileges {
  readonly canViewBranchCollections: boolean;
  /** Days back the member may report: 0 = today only, null = no limit. */
  readonly reportDaysLimit: number | null;
}

// ---- filters ---------------------------------------------------------------------------------------------------------------

export interface PaymentRegisterFilter {
  readonly accession: string | null;
  readonly fromUtc: string | null;
  readonly toUtc: string | null;
  readonly method: PaymentMethod | null;
  readonly status: PaymentStatus | null;
  readonly page: number;
  readonly pageSize: number;
}

export interface RefundRegisterFilter {
  readonly accession: string | null;
  readonly fromUtc: string | null;
  readonly toUtc: string | null;
  readonly method: PaymentMethod | null;
  readonly page: number;
  readonly pageSize: number;
}

export interface CashClosingRegisterFilter {
  readonly fromUtc: string | null;
  readonly toUtc: string | null;
  readonly page: number;
  readonly pageSize: number;
}

/** The preview scope: the session branch (server side), one cashier or every cashier (null), and the period. */
export interface DrawerScopeFilter {
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

/** The cash desk's print parameters (CP-57 `autoPrintReceipt`), readable with `billing.view` (UAT A-05). */
export interface BillingPrintSettings {
  readonly autoPrintReceipt: boolean;
}

// ---- cash ledger (CP-70, change request LIS-CR-011 point C-15) ------------------------------------------------------

export type CashEntryKind = 'CashOut' | 'CashIn';

export type CashEntryCategory = 'Supplier' | 'PettyCash' | 'BankDeposit' | 'OpeningFloat' | 'OtherIncome' | 'Other';

/** The categories a kind accepts (the server refuses another with BIL-062). */
export const CASH_ENTRY_CATEGORIES: Readonly<Record<CashEntryKind, readonly CashEntryCategory[]>> = {
  CashOut: ['Supplier', 'PettyCash', 'BankDeposit', 'Other'],
  CashIn: ['OpeningFloat', 'OtherIncome', 'Other'],
};

/** Money out of (or into) the drawer without a visit; cashier and branch from the session; the method defaults to cash. */
export interface PostCashEntryRequest {
  readonly kind: CashEntryKind;
  readonly category: CashEntryCategory;
  readonly amount: number;
  readonly reason: string;
  readonly method?: PaymentMethod | null;
  readonly methodCode?: string | null;
  readonly reference?: string | null;
  readonly note?: string | null;
}

export interface CashEntryCreated {
  readonly id: string;
  readonly number: string;
}

/** Own drawer (User) or the branch: one cashier or every cashier (needs `canViewBranchCollections`). */
export type CashLedgerScope = 'User' | 'Branch';

/** `fromDay` / `toDay` are laboratory days (`yyyy-MM-dd`, inclusive); null = today. */
export interface CashLedgerRequest {
  readonly fromDay: string | null;
  readonly toDay: string | null;
  readonly scope: CashLedgerScope;
  readonly cashierUserId: string | null;
}

export interface CashLedgerMethodAmounts {
  readonly cash: number;
  readonly card: number;
  readonly pos: number;
  readonly transfer: number;
  readonly total: number;
}

export interface CashLedgerSummary {
  readonly openingBalance: number;
  readonly openingClosingNumber: string | null;
  readonly openingClosedThroughUtc: string | null;
  readonly receipts: CashLedgerMethodAmounts;
  readonly receiptCount: number;
  readonly refunds: CashLedgerMethodAmounts;
  readonly refundCount: number;
  readonly voidedCount: number;
  readonly voidedAmount: number;
  readonly cashIn: number;
  readonly cashOut: number;
  readonly entryCount: number;
  readonly countDifferences: number;
  readonly closingCount: number;
  readonly closingBalance: number;
  readonly netCash: number;
}

export type CashLedgerRowKind = 'Receipt' | 'Refund' | 'CashIn' | 'CashOut' | 'Closing';

/** One line in time order; `moneyIn` / `moneyOut` are what it did to the drawer (cash only), `balance` the cash after it. */
export interface CashLedgerRow {
  readonly kind: CashLedgerRowKind;
  readonly id: string;
  readonly number: string;
  readonly atUtc: string;
  readonly visitId: string | null;
  readonly accession: string | null;
  readonly method: PaymentMethod | null;
  readonly methodCode: string | null;
  readonly category: CashEntryCategory | null;
  readonly text: string | null;
  readonly amount: number;
  readonly status: PaymentStatus;
  readonly moneyIn: number;
  readonly moneyOut: number;
  readonly balance: number;
  readonly cashierUserId: string;
  readonly cashierName: string | null;
  readonly expected: number | null;
  readonly counted: number | null;
  readonly difference: number | null;
}

/** The period a day close from the ledger covers: after the scope's last closing (or the day's start) up to the read. */
export interface CashLedgerCloseWindow {
  readonly fromUtc: string;
  readonly toUtc: string;
}

/** The ledger and its printed twin, the A4 "Daily cash sheet". */
export interface CashLedgerView {
  readonly letterhead: DocumentLetterhead;
  readonly branchId: string;
  readonly scope: CashLedgerScope;
  readonly cashierUserId: string | null;
  readonly cashierName: string | null;
  readonly fromDay: string;
  readonly toDay: string;
  readonly fromUtc: string;
  readonly toUtc: string;
  readonly timeZone: string;
  readonly summary: CashLedgerSummary;
  readonly rows: readonly CashLedgerRow[];
  readonly closeWindow: CashLedgerCloseWindow | null;
  readonly cashiers: readonly DrawerCashier[];
  readonly currency: string;
  readonly printedBy: string;
  readonly printedAtUtc: string;
}

export const BILLING_API = {
  payments: '/api/v1/billing/payments',
  refunds: '/api/v1/billing/refunds',
  visits: '/api/v1/billing/visits',
  batches: '/api/v1/billing/batches',
  cashClosings: '/api/v1/billing/cash-closings',
  printSettings: '/api/v1/billing/print-settings',
  collectionsReport: '/api/v1/billing/collections-report',
  cashLedger: '/api/v1/billing/cash-ledger',
  cashEntries: '/api/v1/billing/cash-entries',
  refundReasons: '/api/v1/catalogue/reasons',
  paymentMethods: '/api/v1/billing/payment-methods',
  paymentMethodOptions: '/api/v1/billing/payment-methods/options',
} as const;

/** Permission keys of the module (PermissionCatalog.Billing). `approve` records refunds until the CP-37 special right lands. */
export const BILLING_PERMISSIONS = {
  view: 'billing.view',
  pay: 'billing.create',
  refund: 'billing.approve',
  void: 'billing.cancel',
  close: 'billing.manage',
  /** Cash ledger (LIS-CR-011): posting a cash entry is billing.create, voiding it billing.approve. */
  cashEntry: 'billing.create',
  voidCashEntry: 'billing.approve',
  manage: 'billing.manage',
  export: 'billing.export',
} as const;

// ---- payment method definitions (set-up list CP-30b, change request 2026-10-19 P-C10) ------------------------------

/** An active method as the cash desk and the registration page offer it, in sequence order. */
export interface PaymentMethodOption {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly kind: PaymentMethod;
  readonly requiresReference: boolean;
}

export interface PaymentMethodListItem {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly kind: PaymentMethod;
  readonly requiresReference: boolean;
  readonly isActive: boolean;
  readonly sequence: number;
}

/** Create: the code is upper-cased by the server and never changes; `sequence` null = after the last option. */
export interface PaymentMethodRequest {
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly kind: PaymentMethod;
  readonly requiresReference: boolean;
  readonly sequence: number | null;
}

export interface UpdatePaymentMethodRequest {
  readonly nameEn: string;
  readonly nameAr: string;
  readonly kind: PaymentMethod;
  readonly requiresReference: boolean;
  readonly sequence: number;
}

/** How many of the four defaults (CASH, CARD, POS, TRANSFER) the call added; 0 when all existed. */
export interface PaymentMethodDefaultsResult {
  readonly created: number;
}

export interface PaymentMethodFilter {
  readonly search?: string;
  readonly isActive?: boolean | null;
  readonly page?: number;
  readonly pageSize?: number;
  readonly sort?: string | null;
  readonly direction?: 'asc' | 'desc' | null;
}
