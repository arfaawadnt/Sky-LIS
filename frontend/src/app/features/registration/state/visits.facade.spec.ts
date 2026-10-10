import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { PRICING_API } from '../../../core/api/pricing.contracts';
import { REGISTRATION_API } from '../../../core/api/registration.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { localDayEndUtc, localDayStartUtc } from '../data/registration-format';
import { cashTier, insurerTier, page, settle, visitRow } from '../testing/registration.fixtures';
import { VisitsFacade } from './visits.facade';

describe('VisitsFacade (CP-10)', () => {
  let facade: VisitsFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthStore, useValue: { currentBranch: signal<string | null>('b-1'), hasPermission: () => true } satisfies Pick<AuthStore, 'currentBranch' | 'hasPermission'> },
        VisitsFacade,
      ],
    });
    facade = TestBed.inject(VisitsFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  it('loads today of this branch on start and lists the payers of the active tiers once each', async () => {
    await runEffects();
    const list = backend.expectOne((request) => request.url === REGISTRATION_API.visits);
    const params = list.request.params;
    expect(params.get('branchId')).toBe('b-1');
    expect(params.get('from')).toBe(localDayStartUtc(facade.days().from));
    expect(params.get('to')).toBe(localDayEndUtc(facade.days().to));
    expect(params.get('page')).toBe('1');
    expect(params.get('pageSize')).toBe('50');
    expect(params.has('accession')).toBe(false);
    expect(params.has('status')).toBe(false);
    list.flush(page([visitRow()]));
    backend.expectOne((request) => request.url === PRICING_API.tierOptions).flush([cashTier, insurerTier, { ...insurerTier, id: 'tier-ins-2', nameEn: 'Silver' }]);
    await runEffects();

    expect(facade.visits.value()?.items.length).toBe(1);
    expect(facade.payers().map((payer) => payer.id)).toEqual(['payer-cash', 'payer-ins']);
    backend.verify();
  });

  it('classifies the search text, drops the day range for "all dates" and resets to the first page', async () => {
    await runEffects();
    backend.match(() => true).forEach((call) => call.flush(call.request.url === PRICING_API.tierOptions ? [] : page([])));
    facade.setPage({ page: 3, pageSize: 20 });

    facade.setText('ACC-2026-00001');
    facade.allDays();
    facade.setBranchOnly(false);
    facade.setStatus('Completed');
    facade.setPayer('payer-ins');
    await runEffects();

    const list = backend.expectOne((request) => request.url === REGISTRATION_API.visits);
    const params = list.request.params;
    expect(params.get('accession')).toBe('ACC-2026-00001');
    expect(params.has('name')).toBe(false);
    expect(params.has('from')).toBe(false);
    expect(params.has('to')).toBe(false);
    expect(params.has('branchId')).toBe(false);
    expect(params.get('status')).toBe('Completed');
    expect(params.get('payerId')).toBe('payer-ins');
    expect(params.get('page')).toBe('1');
    expect(params.get('pageSize')).toBe('20');
    list.flush(page([]));

    facade.setText('0100 123 4567');
    await runEffects();
    expect(backend.expectOne((request) => request.url === REGISTRATION_API.visits).request.params.get('mobile')).toBe('01001234567');
  });

  it('cancels services with the reason and reloads the register', async () => {
    await runEffects();
    backend.match(() => true).forEach((call) => call.flush(call.request.url === PRICING_API.tierOptions ? [] : page([])));
    await runEffects();
    // no cash tier yet: the api provisions the default payer (idempotent) and re-reads the tiers
    backend.expectOne(PRICING_API.defaultPayer).flush({ payerId: 'p-cash', tierId: 't-cash', created: true });
    await runEffects();
    backend.expectOne((request) => request.url === PRICING_API.tierOptions).flush([]);
    await runEffects();

    const pending = facade.cancel.execute({ visitId: 'v-1', request: { lineIds: ['l-1'], reasonId: 'r-cancel', reason: null } });
    await settle();
    const post = backend.expectOne(`${REGISTRATION_API.visits}/v-1/lines/cancel`);
    expect(post.request.body).toEqual({ lineIds: ['l-1'], reasonId: 'r-cancel', reason: null });
    post.flush(null, { status: 204, statusText: 'No Content' });
    expect(await pending).toBeUndefined();
    await runEffects();
    backend.expectOne((request) => request.url === REGISTRATION_API.visits).flush(page([]));
    backend.verify();
  });

  it('keeps the refusal code of a cancellation (no Cancel right → REG-061)', async () => {
    await runEffects();
    backend.match(() => true).forEach((call) => call.flush(call.request.url === PRICING_API.tierOptions ? [] : page([])));

    const pending = facade.cancel.execute({ visitId: 'v-1', request: { lineIds: ['l-1'], reasonId: null, reason: 'typed' } });
    await settle();
    backend.expectOne(`${REGISTRATION_API.visits}/v-1/lines/cancel`).flush({ type: 'about:blank', title: 'Forbidden', status: 403, code: 'REG-061' }, { status: 403, statusText: 'Forbidden' });
    expect(await pending).toBeNull();
    expect(facade.cancel.problem()?.code).toBe('REG-061');
  });

  it('exports the rows of the current filter without paging', async () => {
    await runEffects();
    backend.match(() => true).forEach((call) => call.flush(call.request.url === PRICING_API.tierOptions ? [] : page([])));
    facade.setText('Mona');

    const pending = facade.exportRows();
    await settle();
    backend.match((request) => request.url === REGISTRATION_API.visits).forEach((call) => call.flush(page([])));
    const call = backend.expectOne((request) => request.url === REGISTRATION_API.exportVisits);
    expect(call.request.params.get('name')).toBe('Mona');
    expect(call.request.params.has('page')).toBe(false);
    call.flush([visitRow()]);
    expect((await pending).length).toBe(1);
  });
});
