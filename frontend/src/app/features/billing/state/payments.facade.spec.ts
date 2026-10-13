import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { BILLING_API, type Paged, type PaymentListItem } from '../../../core/api/billing.contracts';
import { DeviceIdentityService } from '../../../core/device/device-identity.service';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { ORGANIZATION_API } from '../../../core/api/organization.contracts';
import { account, collectionsReport, flushCashDesk, flushReportPrivileges } from '../testing/billing.fixtures';
import { CashDeskFacade } from './cash-desk.facade';
import { PaymentsFacade } from './payments.facade';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function page<T>(items: T[]): Paged<T> {
  return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 };
}

describe('PaymentsFacade (CP-23)', () => {
  let facade: PaymentsFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: DeviceIdentityService, useValue: { deviceId: () => 'device-test' } satisfies Pick<DeviceIdentityService, 'deviceId'> },
        CashDeskFacade,
        PaymentsFacade,
      ],
    });
    facade = TestBed.inject(PaymentsFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
    flushCashDesk(backend);
  }

  it('re-reads the visit on screen when the page opens again, and stays idle without one (UAT D-14, A-01)', async () => {
    facade.refresh();
    await runEffects();
    backend.expectNone((request) => request.url.startsWith(BILLING_API.visits));

    facade.open('v-1');
    await runEffects();
    backend.expectOne(`${BILLING_API.visits}/v-1/account`).flush(account());
    await runEffects();

    facade.refresh();
    await runEffects();
    backend.expectOne(`${BILLING_API.visits}/v-1/account`).flush(account({ remaining: 0 }));
    await runEffects();
    expect(facade.current()?.remaining).toBe(0);
    backend.verify();
  });

  it('stays idle until a visit is looked up, then reads the account by accession (encoded)', async () => {
    await runEffects();
    backend.expectNone((request) => request.url.startsWith(BILLING_API.visits));

    facade.find('  ACC 2026/1 ');
    await runEffects();
    backend.expectOne(`${BILLING_API.visits}/by-accession/${encodeURIComponent('ACC 2026/1')}/account`).flush(account());
    await runEffects();

    expect(facade.current()?.remaining).toBe(200);
    backend.verify();
  });

  it('posts a payment with this device and reloads the account', async () => {
    facade.open('v-1');
    await runEffects();
    backend.expectOne(`${BILLING_API.visits}/v-1/account`).flush(account());
    await runEffects();

    const pending = facade.pay.execute({ visitId: 'v-1', treasuryId: 't-1', amount: 200, method: 'Card', reference: 'SLIP-1', reasonId: null, note: null });
    await settle();
    const post = backend.expectOne(BILLING_API.payments);
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ visitId: 'v-1', amount: 200, method: 'Card', reference: 'SLIP-1', treasuryId: 't-1', deviceId: 'device-test' });
    post.flush({ id: 'pay-1', receiptNumber: 'RCT-2026-00002' }, { status: 201, statusText: 'Created' });
    expect(await pending).toEqual({ id: 'pay-1', receiptNumber: 'RCT-2026-00002' });

    await runEffects();
    backend.expectOne(`${BILLING_API.visits}/v-1/account`).flush(account({ paid: 300, remaining: 0, balanceStatus: 'Paid' }));
    await runEffects();
    expect(facade.current()?.balanceStatus).toBe('Paid');
    backend.verify();
  });

  it('keeps the refusal code of a payment as its problem', async () => {
    await runEffects();
    const pending = facade.pay.execute({ visitId: 'v-1', treasuryId: 't-1', amount: 900, method: 'Cash', reference: null, reasonId: null, note: null });
    await settle();
    backend.expectOne(BILLING_API.payments).flush({ type: 'about:blank', title: 'Over', status: 409, code: 'BIL-010' }, { status: 409, statusText: 'Conflict' });

    expect(await pending).toBeNull();
    expect(facade.pay.problem()?.code).toBe('BIL-010');
  });

  it('loads the register only while its tab is open, with the filter as query parameters', async () => {
    await runEffects();
    backend.expectNone((request) => request.url === BILLING_API.payments);

    facade.registerOpen.set(true);
    facade.setFilter({ method: 'Cash', status: 'Posted', accession: 'ACC-1' });
    await runEffects();

    const list = backend.expectOne((request) => request.url === BILLING_API.payments);
    expect(list.request.params.get('method')).toBe('Cash');
    expect(list.request.params.get('status')).toBe('Posted');
    expect(list.request.params.get('accession')).toBe('ACC-1');
    expect(list.request.params.get('page')).toBe('1');
    expect(list.request.params.has('treasuryId')).toBe(false);
    list.flush(page<PaymentListItem>([]));
    backend.expectOne(BILLING_API.treasuries).flush([]);
    await runEffects();
    backend.verify();
  });

  it('reads the report rights when the collections dialog opens and forgets the loaded report when it closes', async () => {
    await runEffects();
    backend.expectNone(ORGANIZATION_API.myPrivileges);

    facade.openCollections();
    await runEffects();
    flushReportPrivileges(backend, true, 7);
    await runEffects();
    expect(facade.reportPrivileges.value()).toEqual({ canViewBranchCollections: true, reportDaysLimit: 7 });

    const loading = facade.collectionsReport.execute({ fromDay: null, toDay: null, scope: 'User', groupBy: 'Method' });
    await settle();
    const get = backend.expectOne((request) => request.url === BILLING_API.collectionsReport);
    expect([get.request.params.has('from'), get.request.params.get('scope'), get.request.params.get('groupBy')]).toEqual([false, 'User', 'Method']);
    get.flush(collectionsReport());
    expect((await loading)?.totals.net).toBe(350);
    expect(facade.collectionsDocument()?.rows.length).toBe(2);

    facade.closeCollections();
    expect(facade.collectionsDocument()).toBeNull();
    expect(facade.collectionsOpen()).toBe(false);
    backend.verify();
  });

  it('voids with the reason in the body', async () => {
    await runEffects();
    const pending = facade.voidPayment.execute({ id: 'pay/1', reason: 'wrong visit' });
    await settle();
    const post = backend.expectOne(`${BILLING_API.payments}/${encodeURIComponent('pay/1')}/void`);
    expect(post.request.body).toEqual({ reason: 'wrong visit' });
    post.flush(null, { status: 204, statusText: 'No Content' });
    expect(await pending).toBeUndefined();
  });
});
