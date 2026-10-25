import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { BILLING_API } from '../../../core/api/billing.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { flushReportPrivileges, ledgerView, preview } from '../testing/billing.fixtures';
import { CashLedgerFacade } from './cash-ledger.facade';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

describe('CashLedgerFacade (CP-70, LIS-CR-011)', () => {
  let facade: CashLedgerFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(withInterceptors([problemDetailsInterceptor])), provideHttpClientTesting(), CashLedgerFacade],
    });
    facade = TestBed.inject(CashLedgerFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  async function initialLoads(): Promise<void> {
    await runEffects();
    const first = backend.expectOne((request) => request.url === BILLING_API.cashLedger);
    expect(first.request.params.get('scope')).toBe('User');
    expect(first.request.params.has('from')).toBe(false);   // today: the server's laboratory day
    expect(first.request.params.has('cashierUserId')).toBe(false);
    first.flush(ledgerView());
    flushReportPrivileges(backend, true, null);
    await runEffects();
  }

  it('reads today\'s own drawer first, then the period, the branch scope and one cashier asked for', async () => {
    await initialLoads();
    expect(facade.current()?.summary.closingBalance).toBe(360);
    expect(facade.currentPrivileges()?.canViewBranchCollections).toBe(true);

    facade.setRequest({ fromDay: '2026-10-01', toDay: '2026-10-09', scope: 'Branch', cashierUserId: 'u-2' });
    await runEffects();
    const request = backend.expectOne((candidate) => candidate.url === BILLING_API.cashLedger);
    expect(request.request.params.get('from')).toBe('2026-10-01');
    expect(request.request.params.get('to')).toBe('2026-10-09');
    expect(request.request.params.get('scope')).toBe('Branch');
    expect(request.request.params.get('cashierUserId')).toBe('u-2');
    request.flush(ledgerView({ scope: 'Branch', cashierUserId: 'u-2' }));
    await runEffects();

    facade.setRequest({ scope: 'User' });
    expect(facade.request().cashierUserId).toBeNull();   // the own drawer names no other cashier
    await runEffects();
    backend.expectOne((candidate) => candidate.url === BILLING_API.cashLedger).flush(ledgerView());
    backend.verify();
  });

  it('posts a cash entry and voids one, re-reading the ledger after each', async () => {
    await initialLoads();

    const posting = facade.postEntry.execute({ kind: 'CashOut', category: 'Supplier', amount: 40, reason: 'Courier', method: 'Cash' });
    const post = backend.expectOne((request) => request.method === 'POST' && request.url === BILLING_API.cashEntries);
    expect(post.request.body).toEqual({ kind: 'CashOut', category: 'Supplier', amount: 40, reason: 'Courier', method: 'Cash' });
    post.flush({ id: 'e-1', number: 'CSH-2026-00001' }, { status: 201, statusText: 'Created' });
    expect(await posting).toEqual({ id: 'e-1', number: 'CSH-2026-00001' });
    await runEffects();
    backend.expectOne((request) => request.url === BILLING_API.cashLedger).flush(ledgerView());
    await runEffects();

    const voiding = facade.voidEntry.execute({ id: 'e-1', reason: 'typed twice' });
    const voidCall = backend.expectOne(`${BILLING_API.cashEntries}/e-1/void`);
    expect(voidCall.request.body).toEqual({ reason: 'typed twice' });
    voidCall.flush(null, { status: 204, statusText: 'No Content' });
    expect(await voiding).toBeUndefined();
    await runEffects();
    backend.expectOne((request) => request.url === BILLING_API.cashLedger).flush(ledgerView());

    const refused = facade.voidEntry.execute({ id: 'e-2', reason: 'late' });
    backend.expectOne(`${BILLING_API.cashEntries}/e-2/void`).flush({ type: 'about:blank', title: 'Closed', status: 409, code: 'BIL-061', traceId: null, correlationId: null, errors: null }, { status: 409, statusText: 'Conflict' });
    expect(await refused).toBeNull();
    expect(facade.voidEntry.problem()?.code).toBe('BIL-061');
    backend.verify();
  });

  it('closes the day over the ledger\'s close window: preview of the scope\'s drawer, then the close, then a fresh ledger', async () => {
    await initialLoads();
    expect(facade.openClose(ledgerView({ closeWindow: null }))).toBe(false);
    expect(facade.openClose(ledgerView())).toBe(true);
    await runEffects();

    const previewCall = backend.expectOne((request) => request.url === `${BILLING_API.cashClosings}/preview`);
    expect(previewCall.request.params.get('cashierUserId')).toBe('u-1');
    expect(previewCall.request.params.get('from')).toBe('2026-10-08T21:00:00Z');
    expect(previewCall.request.params.get('to')).toBe('2026-10-09T10:00:00Z');
    previewCall.flush(preview({ cashierUserId: 'u-1', periodFromUtc: '2026-10-08T21:00:00Z', periodToUtc: '2026-10-09T10:00:00Z' }));
    await runEffects();
    expect(facade.currentClosePreview()?.periodToUtc).toBe('2026-10-09T10:00:00Z');

    const closing = facade.close.execute({ cashierUserId: 'u-1', periodFromUtc: '2026-10-08T21:00:00Z', periodToUtc: '2026-10-09T10:00:00Z', countedCash: 360, note: null });
    backend.expectOne((request) => request.method === 'POST' && request.url === BILLING_API.cashClosings).flush({ id: 'c-1', number: 'CLS-2026-00004' }, { status: 201, statusText: 'Created' });
    expect((await closing)?.number).toBe('CLS-2026-00004');
    expect(facade.closeScope()).toBeNull();
    await runEffects();
    backend.expectOne((request) => request.url === BILLING_API.cashLedger).flush(ledgerView({ closeWindow: null }));
    backend.verify();
  });
});
