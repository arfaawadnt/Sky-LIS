import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { BILLING_API, type BatchAccountView, type BatchMemberView, type Paged, type RefundListItem, type VisitAccountView } from '../../../core/api/billing.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { BillingPrinter } from '../state/billing-printer';
import { BatchPaymentFacade } from '../state/batch-payment.facade';
import { CashDeskFacade } from '../state/cash-desk.facade';
import { RefundsFacade } from '../state/refunds.facade';
import { account, flushCashDesk } from '../testing/billing.fixtures';
import type { MovementInput } from '../ui/movement-form.component';
import { BatchPaymentPage } from './batch-payment.page';
import { RefundsPage } from './refunds.page';

interface RefundsInternals {
  refund(account: VisitAccountView, input: MovementInput): Promise<void>;
}

interface BatchInternals {
  readonly payable: () => number;
  payAll(batch: BatchAccountView, input: MovementInput): Promise<void>;
  canPay(batch: BatchAccountView, member: BatchMemberView): boolean;
}

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function member(visitId: string, remaining: number): BatchMemberView {
  return { visitId, accession: `ACC-${visitId}`, patientId: `p-${visitId}`, patientNumber: null, patientName: null, totalListPrice: 100, discountTotal: 0, patientShareTotal: 100, payerShareTotal: 0, paid: 100 - remaining, refunded: 0, remaining, balanceStatus: remaining > 0 ? 'Unpaid' : 'Paid' };
}

const batch: BatchAccountView = {
  batchId: 'bt-1',
  number: 'BTH-2026-00001',
  kind: 'Group',
  payerId: null,
  payerNameEn: 'ACME',
  payerNameAr: 'أكمي',
  billedThroughClaims: false,
  members: [member('a', 100), member('b', 0), member('c', -20)],
  totalListPrice: 300,
  discountTotal: 0,
  patientShareTotal: 300,
  payerShareTotal: 0,
  paid: 220,
  refunded: 0,
  remaining: 80,
  currency: 'EGP',
};

describe('RefundsPage and BatchPaymentPage (CP-24, CP-27)', () => {
  let backend: HttpTestingController;
  const printed: string[] = [];

  beforeEach(() => {
    printed.length = 0;
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
        {
          provide: BillingPrinter,
          useValue: {
            refundReceipt: (id: string) => Promise.resolve(void printed.push(`refund:${id}`)),
            receipts: (ids: readonly string[]) => Promise.resolve(void printed.push(`receipts:${ids.join(',')}`)),
          } satisfies Pick<BillingPrinter, 'refundReceipt' | 'receipts'>,
        },
        provideRouter(
          [
            {
              path: 'billing',
              providers: [CashDeskFacade],
              children: [
                { path: 'refunds', providers: [RefundsFacade], component: RefundsPage },
                { path: 'batch-payment', providers: [BatchPaymentFacade], component: BatchPaymentPage },
              ],
            },
          ],
          withComponentInputBinding(),
        ),
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  async function tick(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  it('refunds a negative balance and always prints the refund receipt for the signature', async () => {
    const harness = await RouterTestingHarness.create();
    const page = (await harness.navigateByUrl('/billing/refunds?visitId=v-1', RefundsPage)) as unknown as RefundsInternals;
    await tick();
    flushCashDesk(backend, false);
    backend.expectOne((request) => request.url === BILLING_API.refundReasons).flush({ items: [], page: 1, pageSize: 200, totalCount: 0, totalPages: 0 });
    backend.expectOne((request) => request.url === BILLING_API.refunds).flush({ items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 } satisfies Paged<RefundListItem>);
    await tick();
    backend.expectOne(`${BILLING_API.visits}/v-1/account`).flush(account({ paid: 300, patientShareTotal: 260, remaining: -40, refundable: 40, balanceStatus: 'RefundDue' }));
    await tick();

    // UAT A-06: the note typed for this refund must not stay for the next one
    harness.detectChanges();
    const note = harness.routeNativeElement?.querySelector<HTMLTextAreaElement>('app-billing-movement-form textarea');
    expect(note).toBeTruthy();
    if (note) {
      note.value = 'first refund note';
      note.dispatchEvent(new Event('input'));
    }

    await Promise.all([
      page.refund(account(), { amount: 40, method: 'Cash', reference: null, reasonId: 'r-1', note: null }),
      (async () => {
        await settle();
        backend.expectOne((request) => request.url === BILLING_API.refunds && request.method === 'POST').flush({ id: 'rf-1', number: 'RFD-2026-00001' }, { status: 201, statusText: 'Created' });
      })(),
    ]);

    expect(printed).toEqual(['refund:rf-1']);   // printed although autoPrintReceipt is off
    await tick();
    harness.detectChanges();
    expect(harness.routeNativeElement?.querySelector<HTMLTextAreaElement>('app-billing-movement-form textarea')?.value).toBe('');
    backend.expectOne(`${BILLING_API.visits}/v-1/account`).flush(account({ remaining: 0, refundable: 0, balanceStatus: 'Paid' }));
    backend.expectOne((request) => request.url === BILLING_API.refunds && request.method === 'GET').flush({ items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 });
  });

  it('opens a batch by number, offers pay-all for the positive balances only and prints all receipts', async () => {
    const harness = await RouterTestingHarness.create();
    const page = (await harness.navigateByUrl('/billing/batch-payment?number=BTH-2026-00001', BatchPaymentPage)) as unknown as BatchInternals;
    await tick();
    flushCashDesk(backend);
    await tick();
    backend.expectOne(`${BILLING_API.batches}/by-number/BTH-2026-00001/account`).flush(batch);
    await tick();

    expect(page.payable()).toBe(100);
    expect(page.canPay(batch, member('a', 100))).toBe(true);
    expect(page.canPay(batch, member('b', 0))).toBe(false);
    expect(page.canPay({ ...batch, billedThroughClaims: true }, member('a', 100))).toBe(false);

    await Promise.all([
      page.payAll(batch, { amount: 100, method: 'Cash', reference: null, reasonId: null, note: null }),
      (async () => {
        await settle();
        backend.expectOne(`${BILLING_API.batches}/bt-1/pay-all`).flush({ batchId: 'bt-1', amount: 100, payments: [{ visitId: 'a', accession: 'ACC-a', paymentId: 'pay-a', receiptNumber: 'RCT-2026-00009', amount: 100 }] });
      })(),
    ]);

    expect(printed).toEqual(['receipts:pay-a']);
    await tick();
    backend.expectOne(`${BILLING_API.batches}/by-number/BTH-2026-00001/account`).flush({ ...batch, members: [member('a', 0), member('b', 0), member('c', -20)], remaining: -20 });
  });
});
