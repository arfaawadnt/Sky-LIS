/** Fixtures of the Billing specs (not part of the application bundle: only specs import this file). */

import type { HttpTestingController } from '@angular/common/http/testing';

import { BILLING_API, type CashClosingPreview, type VisitAccountView } from '../../../core/api/billing.contracts';

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

/** Answers the two loads of the cash desk (approved treasuries of this device, the billing print parameters) when they are pending. */
export function flushCashDesk(backend: HttpTestingController, autoPrintReceipt = true): void {
  for (const request of backend.match((candidate) => candidate.url === BILLING_API.myTreasuries)) {
    request.flush([{ id: 't-1', code: 'MAIN', nameEn: 'Main drawer', nameAr: 'الخزينة الرئيسية', branchId: 'b-1', isActive: true }]);
  }

  for (const request of backend.match((candidate) => candidate.url === BILLING_API.printSettings)) {
    request.flush({ autoPrintReceipt });
  }
}
