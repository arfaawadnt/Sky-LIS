import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { SAMPLES_API } from '../../../core/api/samples.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { page, problemBody, sampleDetail, sampleItem, settle } from '../testing/samples.fixtures';
import { CollectionFacade } from './collection.facade';

const settings = { autoCollect: false, autoReceive: false, newBarcodeOnReject: true, labelCopies: 1 };

describe('CollectionFacade (CP-14)', () => {
  let facade: CollectionFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthStore, useValue: { currentBranch: signal<string | null>('branch-1') } },
        CollectionFacade,
      ],
    });
    facade = TestBed.inject(CollectionFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  async function flushInitialLoads(): Promise<void> {
    await runEffects();
    backend.expectOne((request) => request.url === SAMPLES_API.samples).flush(page([]));
    backend.expectOne(SAMPLES_API.settings).flush(settings);
    await runEffects();
  }

  it("re-reads today's list and the parameters when the page opens again (UAT D-14, A-01)", async () => {
    await flushInitialLoads();
    facade.refresh();
    await runEffects();
    backend.expectOne((request) => request.url === SAMPLES_API.samples).flush(page([]));
    backend.expectOne(SAMPLES_API.settings).flush(settings);
    backend.verify();
  });

  it("loads today's samples of the session branch and narrows them by status", async () => {
    await runEffects();
    const first = backend.expectOne((request) => request.url === SAMPLES_API.samples);
    expect(first.request.params.get('branchId')).toBe('branch-1');
    expect(first.request.params.has('from')).toBe(true);
    expect(first.request.params.has('to')).toBe(true);
    expect(first.request.params.has('status')).toBe(false);
    first.flush(page([sampleItem()]));
    backend.expectOne(SAMPLES_API.settings).flush(settings);
    await runEffects();
    expect(facade.today.value()?.items.length).toBe(1);

    facade.setStatus('Received');
    facade.setBranchOnly(false);
    await runEffects();
    const filtered = backend.expectOne((request) => request.url === SAMPLES_API.samples);
    expect(filtered.request.params.get('status')).toBe('Received');
    expect(filtered.request.params.has('branchId')).toBe(false);
    filtered.flush(page([]));
    backend.verify();
  });

  it('scans the normalised barcode in the current mode, logs the outcome and reloads the list', async () => {
    await flushInitialLoads();
    facade.setMode('Receive');

    const pending = facade.scanNumber('  smp-2026-00001 ');
    await settle();
    const post = backend.expectOne(SAMPLES_API.scan);
    expect(post.request.body).toEqual({ sampleNumber: 'SMP-2026-00001', mode: 'Receive' });
    post.flush(sampleDetail('Received'));
    const outcome = await pending;

    expect(outcome?.ok).toBe(true);
    expect(facade.lastOutcome()?.detail?.summary.status).toBe('Received');
    await runEffects();
    backend.expectOne((request) => request.url === SAMPLES_API.samples).flush(page([]));
    backend.verify();
  });

  it('keeps the refusal code of a failed scan for the feedback strip', async () => {
    await flushInitialLoads();

    const pending = facade.scanNumber('SMP-2026-00001');
    await settle();
    backend.expectOne(SAMPLES_API.scan).flush(problemBody('SMP-010', 409), { status: 409, statusText: 'Conflict' });
    const outcome = await pending;

    expect(outcome?.ok).toBe(false);
    expect(outcome?.problem?.code).toBe('SMP-010');
    expect(facade.outcomes().length).toBe(1);
    expect(await facade.scanNumber('   ')).toBeNull();
    backend.verify();
  });

  it('resolves the visit of an accession, then receives all its tubes', async () => {
    await flushInitialLoads();

    const pending = facade.bulk.execute({ accession: 'ACC-2026-00001', action: 'receive' });
    await settle();
    const lookup = backend.expectOne((request) => request.url === SAMPLES_API.samples && request.params.get('accession') === 'ACC-2026-00001');
    expect(lookup.request.params.get('pageSize')).toBe('1');
    lookup.flush(page([sampleItem({ visitId: 'visit-9' })]));
    await settle();
    backend.expectOne(SAMPLES_API.receiveAll('visit-9')).flush({ visitId: 'visit-9', changed: 2, unchanged: 1, changedSampleNumbers: ['A', 'B'] });

    const result = await pending;
    expect(result?.changed).toBe(2);
    expect(result?.accession).toBe('ACC-2026-00001');
    await runEffects();
    backend.expectOne((request) => request.url === SAMPLES_API.samples).flush(page([]));
    backend.verify();
  });

  it('reports SMP-405 when the accession has no samples', async () => {
    await flushInitialLoads();

    const pending = facade.bulk.execute({ accession: 'ACC-404', action: 'collect' });
    await settle();
    backend.expectOne((request) => request.url === SAMPLES_API.samples).flush(page([]));

    expect(await pending).toBeNull();
    expect(facade.bulk.problem()?.code).toBe('SMP-405');
    backend.verify();
  });
});
