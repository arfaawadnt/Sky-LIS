import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { BILLING_API, type CashClosingListItem, type Paged } from '../../../core/api/billing.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { preview } from '../testing/billing.fixtures';
import { CashClosingFacade } from './cash-closing.facade';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function page<T>(items: T[]): Paged<T> {
  return { items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1 };
}

describe('CashClosingFacade (CP-30)', () => {
  let facade: CashClosingFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(withInterceptors([problemDetailsInterceptor])), provideHttpClientTesting(), CashClosingFacade],
    });
    facade = TestBed.inject(CashClosingFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  async function initialLoads(): Promise<void> {
    await runEffects();
    backend.expectOne(BILLING_API.treasuries).flush([{ id: 't-1', code: 'MAIN', nameEn: 'Main drawer', nameAr: 'الخزينة', branchId: 'b-1', isActive: true }]);
    backend.expectOne((request) => request.url === BILLING_API.cashClosings).flush(page<CashClosingListItem>([]));
    await runEffects();
  }

  it('previews a drawer only once a scope is chosen, passing cashier and period', async () => {
    await initialLoads();
    backend.expectNone((request) => request.url.endsWith('/preview'));

    facade.scope.set({ treasuryId: 't-1', cashierUserId: 'u-1', fromUtc: '2026-10-09T00:00:00.000Z', toUtc: '2026-10-09T16:00:00.000Z' });
    await runEffects();
    const request = backend.expectOne((candidate) => candidate.url === `${BILLING_API.cashClosings}/preview`);
    expect(request.request.params.get('treasuryId')).toBe('t-1');
    expect(request.request.params.get('cashierUserId')).toBe('u-1');
    expect(request.request.params.get('from')).toBe('2026-10-09T00:00:00.000Z');
    expect(request.request.params.get('to')).toBe('2026-10-09T16:00:00.000Z');
    request.flush(preview());
    await runEffects();

    expect(facade.currentPreview()?.totals.expectedCash).toBe(450);
    expect(facade.treasuryList().length).toBe(1);
    backend.verify();
  });

  it('closes the drawer, then reloads the preview and the register', async () => {
    await initialLoads();
    facade.scope.set({ treasuryId: 't-1', cashierUserId: null, fromUtc: '2026-10-09T00:00:00.000Z', toUtc: '2026-10-09T16:00:00.000Z' });
    await runEffects();
    backend.expectOne((request) => request.url.endsWith('/preview')).flush(preview());
    await runEffects();

    const pending = facade.close.execute({ treasuryId: 't-1', cashierUserId: null, periodFromUtc: '2026-10-09T00:00:00.000Z', periodToUtc: '2026-10-09T16:00:00.000Z', countedCash: 440, note: '10 short' });
    await settle();
    const post = backend.expectOne((request) => request.url === BILLING_API.cashClosings && request.method === 'POST');
    expect(post.request.body).toEqual({ treasuryId: 't-1', cashierUserId: null, periodFromUtc: '2026-10-09T00:00:00.000Z', periodToUtc: '2026-10-09T16:00:00.000Z', countedCash: 440, note: '10 short' });
    post.flush({ id: 'c-1', number: 'CLS-2026-00001' }, { status: 201, statusText: 'Created' });
    expect(await pending).toEqual({ id: 'c-1', number: 'CLS-2026-00001' });

    await runEffects();
    backend.expectOne((request) => request.url.endsWith('/preview')).flush(preview({ overlapsClosedPeriod: true }));
    backend.expectOne((request) => request.url === BILLING_API.cashClosings && request.method === 'GET').flush(page<CashClosingListItem>([]));
    await runEffects();
    expect(facade.currentPreview()?.overlapsClosedPeriod).toBe(true);
    backend.verify();
  });

  it('re-reads the treasuries and the register when the page opens again (UAT D-14, A-01)', async () => {
    await initialLoads();
    facade.refresh();
    await runEffects();
    backend.expectOne(BILLING_API.treasuries).flush([]);
    backend.expectOne((request) => request.url === BILLING_API.cashClosings).flush(page<CashClosingListItem>([]));
    await runEffects();
    expect(facade.treasuryList()).toEqual([]);
    backend.verify();
  });

  it('asks for the cashiers of a treasury and period', async () => {
    await initialLoads();
    facade.cashierQuery.set({ treasuryId: 't-1', fromUtc: '2026-10-09T00:00:00.000Z', toUtc: '2026-10-09T16:00:00.000Z' });
    await runEffects();
    const request = backend.expectOne((candidate) => candidate.url === `${BILLING_API.cashClosings}/cashiers`);
    expect(request.request.params.get('treasuryId')).toBe('t-1');
    request.flush([{ userId: 'u-1', name: 'Cashier One' }]);
    await runEffects();
    expect(facade.cashiers.value()).toEqual([{ userId: 'u-1', name: 'Cashier One' }]);
  });
});
