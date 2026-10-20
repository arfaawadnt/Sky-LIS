import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { BILLING_API, type VisitAccountView } from '../../../core/api/billing.contracts';
import { REGISTRATION_API } from '../../../core/api/registration.contracts';
import { RESULTS_API, type ResultCaseView } from '../../../core/api/results.contracts';
import { SAMPLES_API } from '../../../core/api/samples.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { page as pageOf, settle, visitDetail, visitRow } from '../testing/registration.fixtures';
import { ACCESSION_NOT_FOUND, AccessionFacade, pickSingle } from './accession.facade';

/** Billing's account with one posted payment; only the fields the card reads are meaningful. */
const account = {
  visitId: 'v-1',
  accession: 'ACC-2026-00001',
  patientId: 'p-1',
  patientNumber: 'PAT-2026-00001',
  patientName: 'Mona Adel',
  branchId: 'b-1',
  payerId: 'payer-cash',
  payerNameEn: 'Cash',
  payerNameAr: 'نقدي',
  batchId: null,
  batchNumber: null,
  batchKind: null,
  visitStatus: 'Open',
  registeredAtUtc: '2026-10-10T08:00:00Z',
  totalListPrice: 100,
  discountTotal: 0,
  patientShareTotal: 100,
  payerShareTotal: 0,
  paid: 40,
  refunded: 0,
  remaining: 60,
  refundable: 40,
  balanceStatus: 'Partial',
  billedThroughClaims: false,
  currency: 'EGP',
  payments: [],
  refunds: [],
} as unknown as VisitAccountView;

/** A result case with one unit whose two results are at different stages, one of them cancelled. */
const resultCase = {
  visit: { visitId: 'v-1', accession: 'ACC-2026-00001' },
  patient: {},
  units: [
    {
      key: 'u-1',
      status: 'Complete',
      results: [
        { id: 'r-1', lineId: 'l-1', status: 'Verified' },
        { id: 'r-2', lineId: 'l-1', status: 'Cancelled' },
        { id: 'r-3', lineId: 'l-2', status: 'Ordered' },
      ],
    },
  ],
  hiddenUnits: 0,
  hiddenResults: 0,
  scannedSampleNumber: null,
  settings: { reviewBeforeVerify: false, printUnverified: false, resultLanguage: 'en' },
} as unknown as ResultCaseView;

let granted = new Set<string>();

describe('AccessionFacade (CP-10b)', () => {
  let backend: HttpTestingController;
  let facade: AccessionFacade;

  beforeEach(() => {
    granted = new Set(['registration.view', 'samples.view', 'billing.view', 'results.view']);
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthStore, useValue: { currentBranch: signal<string | null>('b-1'), hasPermission: (key: string) => granted.has(key) } satisfies Pick<AuthStore, 'currentBranch' | 'hasPermission'> },
        AccessionFacade,
      ],
    });
    backend = TestBed.inject(HttpTestingController);
    facade = TestBed.inject(AccessionFacade);
  });

  afterEach(() => backend.verify());

  /** Two rounds: a resolved resource makes its dependants (the cards keyed on the visit id) load on the next tick. */
  async function tick(): Promise<void> {
    TestBed.tick();
    await settle();
    TestBed.tick();
    await settle();
  }

  it('opens a visit by accession: an exact lookup, then the detail and the three cards of the permitted modules', async () => {
    facade.open({ accession: 'ACC-2026-00001' });
    await tick();
    const lookup = backend.expectOne((request) => request.url === REGISTRATION_API.visits);
    expect(lookup.request.params.get('accession')).toBe('ACC-2026-00001');
    expect(lookup.request.params.get('from')).toBeNull();
    lookup.flush(pageOf([visitRow({ id: 'v-10', accession: 'ACC-2026-000010' }), visitRow()]));
    await settle();
    backend.expectOne(`${REGISTRATION_API.visits}/v-1`).flush(visitDetail());
    await tick();

    expect(facade.visitId()).toBe('v-1');
    const samples = backend.expectOne((request) => request.url === SAMPLES_API.samples);
    expect(samples.request.params.get('visitId')).toBe('v-1');
    samples.flush(pageOf([]));
    backend.expectOne(`${BILLING_API.visits}/v-1/account`).flush(account);
    const results = backend.expectOne((request) => request.url === RESULTS_API.results);
    expect(results.request.params.get('visitId')).toBe('v-1');
    results.flush(resultCase);
    await tick();

    expect(facade.account.value()?.remaining).toBe(60);
    expect(facade.lineResults().get('l-1')).toBe('Verified');   // the cancelled result never hides the live one
    expect(facade.lineResults().get('l-2')).toBe('Ordered');
    expect([...facade.lockedLines()]).toEqual(['l-1']);   // verified → no Cancel on the page (REG-106); l-2 stays cancellable
  });

  it('asks no card of a module the user may not read, and a refused account is an empty card', async () => {
    granted = new Set(['registration.view', 'billing.view']);
    facade.open({ visitId: 'v-1' });
    await tick();
    backend.expectOne(`${REGISTRATION_API.visits}/v-1`).flush(visitDetail());
    await tick();
    backend.expectOne(`${BILLING_API.visits}/v-1/account`).flush({ type: 'about:blank', title: 'Forbidden', status: 403, code: 'AUTH-403' }, { status: 403, statusText: 'Forbidden' });
    await tick();

    backend.expectNone((request) => request.url === SAMPLES_API.samples);
    backend.expectNone((request) => request.url === RESULTS_API.results);
    expect(facade.account.value()).toBeNull();
    expect(facade.account.error()).toBeUndefined();
  });

  it('reports an accession nobody carries', async () => {
    facade.open({ accession: 'ACC-2026-09999' });
    await tick();
    backend.expectOne((request) => request.url === REGISTRATION_API.visits).flush(pageOf([]));
    await tick();

    expect(facade.detail.hasValue()).toBe(false);
    expect((facade.detail.error() as Error).message).toBe(ACCESSION_NOT_FOUND);
  });

  it('search: one match opens, several open the list, the exact accession wins among several, nothing says so', async () => {
    const one = facade.search('Mona');
    await settle();
    let call = backend.expectOne((request) => request.url === REGISTRATION_API.visits);
    expect(call.request.params.get('name')).toBe('Mona');
    call.flush(pageOf([visitRow()]));
    expect(await one).toEqual({ kind: 'opened', visit: visitRow() });
    expect(facade.listOpen()).toBe(false);

    const several = facade.search('01001234567');
    await settle();
    call = backend.expectOne((request) => request.url === REGISTRATION_API.visits);
    expect(call.request.params.get('mobile')).toBe('01001234567');
    call.flush(pageOf([visitRow(), visitRow({ id: 'v-2', accession: 'ACC-2026-00002' })]));
    expect(await several).toEqual({ kind: 'list', count: 2 });
    expect(facade.listOpen()).toBe(true);
    expect(facade.matches().length).toBe(2);

    const exact = facade.search('acc-2026-00002');
    await settle();
    backend.expectOne((request) => request.url === REGISTRATION_API.visits).flush(pageOf([visitRow(), visitRow({ id: 'v-2', accession: 'ACC-2026-00002' })]));
    expect(await exact).toEqual({ kind: 'opened', visit: visitRow({ id: 'v-2', accession: 'ACC-2026-00002' }) });
    expect(facade.listOpen()).toBe(false);

    const none = facade.search('PAT-2026-77777');
    await settle();
    backend.expectOne((request) => request.url === REGISTRATION_API.visits).flush(pageOf([]));
    expect(await none).toEqual({ kind: 'none' });
    expect(await facade.search('   ')).toEqual({ kind: 'none' });
  });

  it('cancels lines and re-reads the visit and its cards', async () => {
    facade.open({ visitId: 'v-1' });
    await tick();
    backend.expectOne(`${REGISTRATION_API.visits}/v-1`).flush(visitDetail());
    await tick();
    backend.match(() => true).forEach((call) => call.flush(pageOf([])));

    const done = facade.cancel.execute({ visitId: 'v-1', request: { lineIds: ['l-1'], reasonId: 'r-1', reason: null } });
    await settle();
    backend.expectOne(`${REGISTRATION_API.visits}/v-1/lines/cancel`).flush(null, { status: 204, statusText: 'No Content' });
    await done;
    await tick();
    backend.expectOne(`${REGISTRATION_API.visits}/v-1`).flush(visitDetail());
    backend.match((request) => request.url !== `${REGISTRATION_API.visits}/v-1`).forEach((call) => call.flush(pageOf([])));
    await tick();
    expect(facade.cancel.problem()).toBeNull();
  });

  it('collects a sample through the phlebotomy scan and re-reads the samples', async () => {
    facade.open({ visitId: 'v-1' });
    await tick();
    backend.expectOne(`${REGISTRATION_API.visits}/v-1`).flush(visitDetail());
    await tick();
    backend.match(() => true).forEach((call) => call.flush(pageOf([])));

    const done = facade.collect.execute('SMP-2026-00001');
    await settle();
    const scan = backend.expectOne(SAMPLES_API.scan);
    expect(scan.request.body).toEqual({ sampleNumber: 'SMP-2026-00001', mode: 'Collect' });
    scan.flush({ summary: { status: 'Collected' } });
    expect(await done).not.toBeNull();
    await tick();
    expect(backend.match((request) => request.url === SAMPLES_API.samples).length).toBe(1);
    backend.match(() => true).forEach((call) => call.flush(pageOf([])));
  });

  it('pickSingle designates the only match or the exact accession', () => {
    const a = visitRow();
    const b = visitRow({ id: 'v-2', accession: 'ACC-2026-00002' });
    expect(pickSingle([a], 'anything')).toBe(a);
    expect(pickSingle([a, b], 'acc-2026-00002')).toBe(b);
    expect(pickSingle([a, b], 'Mona')).toBeNull();
    expect(pickSingle([], 'x')).toBeNull();
  });
});
