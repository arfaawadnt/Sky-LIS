import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { BILLING_API, type BatchAccountView, type Paged, type RefundListItem } from '../../../core/api/billing.contracts';
import { DeviceIdentityService } from '../../../core/device/device-identity.service';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { flushCashDesk } from '../testing/billing.fixtures';
import { BatchPaymentFacade } from './batch-payment.facade';
import { CashDeskFacade } from './cash-desk.facade';
import { RefundsFacade } from './refunds.facade';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function page<T>(items: T[]): Paged<T> {
  return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 };
}

const batch: BatchAccountView = {
  batchId: 'bt-1',
  number: 'BTH-2026-00001',
  kind: 'Group',
  payerId: null,
  payerNameEn: null,
  payerNameAr: null,
  billedThroughClaims: false,
  members: [],
  totalListPrice: 0,
  discountTotal: 0,
  patientShareTotal: 250,
  payerShareTotal: 0,
  paid: 0,
  refunded: 0,
  remaining: 250,
  currency: 'EGP',
};

describe('RefundsFacade and BatchPaymentFacade (CP-24, CP-27)', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: DeviceIdentityService, useValue: { deviceId: () => 'device-test' } satisfies Pick<DeviceIdentityService, 'deviceId'> },
        CashDeskFacade,
        RefundsFacade,
        BatchPaymentFacade,
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  async function runEffects(flushDesk = true): Promise<void> {
    TestBed.tick();
    await settle();
    if (flushDesk) {
      flushCashDesk(backend);
    }
  }

  it('offers the active catalogue reasons of kind Refund and posts a refund with the device', async () => {
    const facade = TestBed.inject(RefundsFacade);
    await runEffects();
    const reasons = backend.expectOne((request) => request.url === BILLING_API.refundReasons);
    expect(reasons.request.params.get('kind')).toBe('Refund');
    reasons.flush(page([
      { id: 'r-1', kind: 'Refund', nameEn: 'Overpayment', nameAr: 'مبلغ زائد', isActive: true },
      { id: 'r-2', kind: 'Refund', nameEn: 'Old', nameAr: 'قديم', isActive: false },
    ]));
    backend.expectOne((request) => request.url === BILLING_API.refunds).flush(page<RefundListItem>([]));
    await runEffects();
    expect(facade.reasons.value()?.map((reason) => reason.id)).toEqual(['r-1']);

    const pending = facade.refund.execute({ visitId: 'v-1', treasuryId: 't-1', amount: 40, method: 'Cash', reference: null, reasonId: 'r-1', note: 'transfer' });
    await settle();
    const post = backend.expectOne((request) => request.url === BILLING_API.refunds && request.method === 'POST');
    expect(post.request.body).toEqual({ visitId: 'v-1', amount: 40, method: 'Cash', reference: null, reasonId: 'r-1', note: 'transfer', treasuryId: 't-1', deviceId: 'device-test' });
    post.flush({ id: 'rf-1', number: 'RFD-2026-00001' }, { status: 201, statusText: 'Created' });
    expect(await pending).toEqual({ id: 'rf-1', number: 'RFD-2026-00001' });
    await runEffects();
    backend.expectOne((request) => request.url === BILLING_API.refunds && request.method === 'GET').flush(page<RefundListItem>([]));
  });

  it('looks a batch up by number and pays all with the device', async () => {
    const facade = TestBed.inject(BatchPaymentFacade);
    facade.find(' BTH-2026-00001 ');
    await runEffects();
    backend.expectOne(`${BILLING_API.batches}/by-number/BTH-2026-00001/account`).flush(batch);
    await runEffects();
    expect(facade.current()?.remaining).toBe(250);

    const pending = facade.payAll.execute({ batchId: 'bt-1', treasuryId: 't-1', amount: 250, method: 'Pos', reference: 'POS-9', reasonId: null, note: null });
    await settle();
    const post = backend.expectOne(`${BILLING_API.batches}/bt-1/pay-all`);
    expect(post.request.body).toEqual({ amount: 250, method: 'Pos', reference: 'POS-9', treasuryId: 't-1', deviceId: 'device-test' });
    post.flush({ batchId: 'bt-1', amount: 250, payments: [] });
    await pending;
    await runEffects();
    backend.expectOne(`${BILLING_API.batches}/by-number/BTH-2026-00001/account`).flush({ ...batch, paid: 250, remaining: 0 });
    await runEffects();
    expect(facade.current()?.remaining).toBe(0);
    backend.verify();
  });

  it('re-read the refunds register and the batch on screen when their pages open again (UAT D-14, A-01)', async () => {
    const refunds = TestBed.inject(RefundsFacade);
    const batches = TestBed.inject(BatchPaymentFacade);
    await runEffects();
    backend.expectOne((request) => request.url === BILLING_API.refunds).flush({ items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 });
    for (const request of backend.match((candidate) => candidate.url === BILLING_API.refundReasons)) {
      request.flush({ items: [], page: 1, pageSize: 200, totalCount: 0, totalPages: 0 });
    }

    batches.find('BTH-2026-00001');
    await runEffects();
    backend.expectOne(`${BILLING_API.batches}/by-number/BTH-2026-00001/account`).flush(batch);
    await runEffects();

    refunds.refresh();
    batches.refresh();
    await runEffects();
    backend.expectOne((request) => request.url === BILLING_API.refunds).flush({ items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 });
    backend.expectOne(`${BILLING_API.batches}/by-number/BTH-2026-00001/account`).flush(batch);
    backend.expectNone((request) => request.url.startsWith(BILLING_API.visits));   // no visit looked up: stays idle
  });

  it('reads the treasuries approved for this device and keeps the chosen one while it stays approved', async () => {
    const desk = TestBed.inject(CashDeskFacade);
    await runEffects(false);
    const mine = backend.expectOne((request) => request.url === BILLING_API.myTreasuries);
    expect(mine.request.params.get('deviceId')).toBe('device-test');
    mine.flush([
      { id: 't-1', code: 'A', nameEn: 'A', nameAr: 'أ', branchId: 'b-1', isActive: true },
      { id: 't-2', code: 'B', nameEn: 'B', nameAr: 'ب', branchId: 'b-1', isActive: true },
    ]);
    backend.expectOne(BILLING_API.printSettings).flush({ type: 'about:blank', title: 'Forbidden', status: 403, code: 'AUTHZ-001' }, { status: 403, statusText: 'Forbidden' });
    await runEffects();

    expect(desk.treasuryId()).toBe('t-1');
    desk.treasuryId.set('t-2');
    expect(desk.hasStation()).toBe(true);
    expect(desk.shouldAutoPrint()).toBe(false);   // UAT A-05: an unknown parameter does not print (it used to print by default)
  });
});
