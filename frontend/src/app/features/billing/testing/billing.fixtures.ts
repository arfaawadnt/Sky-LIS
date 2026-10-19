/** Fixtures of the Billing specs (not part of the application bundle: only specs import this file). */

import type { HttpTestingController } from '@angular/common/http/testing';

import { BILLING_API, type CashClosingPreview, type CollectionsReportDocument, type CollectionsReportRow, type PaymentMethodOption, type VisitAccountView } from '../../../core/api/billing.contracts';
import { ORGANIZATION_API } from '../../../core/api/organization.contracts';

export function account(overrides: Partial<VisitAccountView> = {}): VisitAccountView {
  return {
    visitId: 'v-1',
    accession: 'ACC-2026-00001',
    patientId: 'p-1',
    patientNumber: 'PAT-2026-00001',
    patientName: 'Mona Adel',
    branchId: 'b-1',
    payerId: null,
    payerNameEn: null,
    payerNameAr: null,
    batchId: null,
    batchNumber: null,
    batchKind: null,
    visitStatus: 'Open',
    registeredAtUtc: '2026-10-09T08:00:00Z',
    totalListPrice: 320,
    discountTotal: 20,
    patientShareTotal: 300,
    payerShareTotal: 0,
    paid: 100,
    refunded: 0,
    remaining: 200,
    refundable: 0,
    balanceStatus: 'Partial',
    billedThroughClaims: false,
    currency: 'EGP',
    payments: [],
    refunds: [],
    ...overrides,
  };
}

export function preview(overrides: Partial<CashClosingPreview> = {}): CashClosingPreview {
  return {
    treasuryId: 't-1',
    treasuryCode: 'MAIN',
    treasuryNameEn: 'Main drawer',
    treasuryNameAr: 'الخزينة الرئيسية',
    cashierUserId: null,
    cashierName: null,
    periodFromUtc: '2026-10-09T00:00:00.000Z',
    periodToUtc: '2026-10-09T16:00:00.000Z',
    totals: {
      cashPayments: 500,
      cardPayments: 200,
      posPayments: 30,
      transferPayments: 20,
      totalPayments: 750,
      paymentCount: 7,
      cashRefunds: 50,
      cardRefunds: 0,
      posRefunds: 0,
      transferRefunds: 0,
      totalRefunds: 50,
      refundCount: 1,
      expectedCash: 450,
      outstandingBalance: 120,
      discountTotal: 35,
      visitCount: 6,
    },
    movements: [],
    movementsTruncated: false,
    overlapsClosedPeriod: false,
    currency: 'EGP',
    ...overrides,
  };
}

/** A row of the collections report: cash 300 in / 50 back and card 100 in, four posted payments and one refund. */
export function collectionsRow(overrides: Partial<CollectionsReportRow> = {}): CollectionsReportRow {
  return {
    key: 'Cash',
    labelEn: null,
    labelAr: null,
    paymentCount: 4,
    refundCount: 1,
    cash: { payments: 300, refunds: 50, net: 250 },
    card: { payments: 100, refunds: 0, net: 100 },
    pos: { payments: 0, refunds: 0, net: 0 },
    transfer: { payments: 0, refunds: 0, net: 0 },
    totalPayments: 400,
    totalRefunds: 50,
    net: 350,
    ...overrides,
  };
}

/** Today's own collections by method (change request 2026-10-13, item 2). */
export function collectionsReport(overrides: Partial<CollectionsReportDocument> = {}): CollectionsReportDocument {
  return {
    letterhead: { labNameEn: 'Sky Lab', labNameAr: 'مختبر سكاي', branchNameEn: 'Main branch', branchNameAr: 'الفرع الرئيسي' },
    scope: 'User',
    groupBy: 'Method',
    fromDay: '2026-10-13',
    toDay: '2026-10-13',
    fromUtc: '2026-10-12T21:00:00Z',
    toUtc: '2026-10-13T21:00:00Z',
    timeZone: 'Africa/Cairo',
    cashierName: 'Cashier One',
    rows: [
      collectionsRow({ key: 'Cash', card: { payments: 0, refunds: 0, net: 0 }, paymentCount: 3, totalPayments: 300, net: 250 }),
      collectionsRow({ key: 'Card', cash: { payments: 0, refunds: 0, net: 0 }, paymentCount: 1, refundCount: 0, totalPayments: 100, totalRefunds: 0, net: 100 }),
    ],
    totals: collectionsRow({ key: 'Total' }),
    currency: 'EGP',
    printedBy: 'Cashier One',
    printedAtUtc: '2026-10-13T12:00:00Z',
    ...overrides,
  };
}

/** Answers the dialog's read of the member's privileges (Organization's endpoint) with the two report rights. */
export function flushReportPrivileges(backend: HttpTestingController, canViewBranchCollections: boolean, reportDaysLimit: number | null): void {
  backend.expectOne(ORGANIZATION_API.myPrivileges).flush({
    userId: 'u-1',
    tenantId: 't-1',
    branchId: 'b-1',
    isPrimaryAdministrator: false,
    mustChangePassword: false,
    passwordExpiresAtUtc: null,
    privileges: {
      permissionKeys: ['billing.view'],
      specialRights: {
        discountCapPercent: null,
        canPay: true,
        canRefund: false,
        canTransfer: false,
        canCancel: false,
        canModifyVerifiedResult: false,
        canPreviewWithoutVerify: false,
        canPrintInvoice: true,
        canSeeVip: false,
        demographicsEditUpTo: 'Complete',
        canViewBranchCollections,
        reportDaysLimit,
      },
      resultRights: [],
    },
  });
}

/** Answers the two loads of the cash desk (approved treasuries of this device, the billing print parameters) when they are pending. */
export function flushCashDesk(backend: HttpTestingController, autoPrintReceipt = true): void {
  for (const request of backend.match((candidate) => candidate.url === BILLING_API.myTreasuries)) {
    request.flush([{ id: 't-1', code: 'MAIN', nameEn: 'Main drawer', nameAr: 'الخزينة الرئيسية', branchId: 'b-1', isActive: true }]);
  }

  for (const request of backend.match((candidate) => candidate.url === BILLING_API.printSettings)) {
    request.flush({ autoPrintReceipt });
  }

  for (const request of backend.match((candidate) => candidate.url === BILLING_API.paymentMethodOptions)) {
    request.flush(methodOptions);
  }
}

/** The four default methods of CP-30b as the options endpoint answers them (CARD, POS and TRANSFER need a reference). */
export const methodOptions: readonly PaymentMethodOption[] = [
  { id: 'pm-cash', code: 'CASH', nameEn: 'Cash', nameAr: 'نقدي', kind: 'Cash', requiresReference: false },
  { id: 'pm-card', code: 'CARD', nameEn: 'Card', nameAr: 'بطاقة', kind: 'Card', requiresReference: true },
  { id: 'pm-pos', code: 'POS', nameEn: 'POS', nameAr: 'نقطة بيع', kind: 'Pos', requiresReference: true },
  { id: 'pm-transfer', code: 'TRANSFER', nameEn: 'Bank transfer', nameAr: 'تحويل بنكي', kind: 'Transfer', requiresReference: true },
];
