import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { SAMPLES_API } from '../../../core/api/samples.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { page, problemBody, sampleDetail, sampleItem, settle } from '../testing/samples.fixtures';
import { RejectionsFacade } from './rejections.facade';

describe('RejectionsFacade (CP-16, BR-18)', () => {
  let facade: RejectionsFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        RejectionsFacade,
      ],
    });
    facade = TestBed.inject(RejectionsFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  async function flushInitialLoads(newBarcodeOnReject = true): Promise<void> {
    await runEffects();
    const rejected = backend.expectOne((request) => request.url === SAMPLES_API.samples);
    expect(rejected.request.params.get('status')).toBe('Rejected');
    rejected.flush(page([sampleItem({ status: 'Rejected' })]));
    const reasons = backend.expectOne((request) => request.url === SAMPLES_API.rejectionReasons);
    expect(reasons.request.params.get('kind')).toBe('Rejection');
    expect(reasons.request.params.get('isActive')).toBe('true');
    reasons.flush(page([
      { id: 'r1', kind: 'Rejection', nameEn: 'Hemolysis', nameAr: 'تحلل', isActive: true },
      { id: 'r2', kind: 'Rejection', nameEn: 'Old', nameAr: 'قديم', isActive: false },
    ]));
    backend.expectOne(SAMPLES_API.settings).flush({ autoCollect: true, autoReceive: true, newBarcodeOnReject, labelCopies: 1 });
    await runEffects();
  }

  it('re-reads the list, the reasons and the parameters when the page opens again (UAT D-14, A-01)', async () => {
    await flushInitialLoads(true);
    expect(facade.reinstatementAllowed()).toBe(false);

    facade.refresh();
    await runEffects();
    backend.expectOne((request) => request.url === SAMPLES_API.samples).flush(page([]));
    backend.expectOne((request) => request.url === SAMPLES_API.rejectionReasons).flush(page([]));
    backend.expectOne(SAMPLES_API.settings).flush({ autoCollect: true, autoReceive: true, newBarcodeOnReject: false, labelCopies: 1 });
    await runEffects();
    expect(facade.reinstatementAllowed()).toBe(true);
    backend.verify();
  });

  it('offers active rejection reasons only and follows the reinstatement policy', async () => {
    await flushInitialLoads(true);

    expect(facade.reasons.value()?.map((reason) => reason.id)).toEqual(['r1']);
    expect(facade.reinstatementAllowed()).toBe(false);
    backend.verify();
  });

  it('looks a barcode up, rejects it with the reason and reloads lookup and list', async () => {
    await flushInitialLoads();
    facade.find(' smp-2026-00001 ');
    await runEffects();
    backend.expectOne(SAMPLES_API.byNumber('SMP-2026-00001')).flush(sampleDetail('Received'));
    await runEffects();
    expect(facade.lookup.value()?.summary.status).toBe('Received');

    const pending = facade.reject.execute({ id: 'smp-1', reasonId: 'r1', note: 'clotted' });
    await settle();
    const post = backend.expectOne(SAMPLES_API.reject('smp-1'));
    expect(post.request.body).toEqual({ reasonId: 'r1', note: 'clotted' });
    post.flush(null, { status: 204, statusText: 'No Content' });
    expect(await pending).toBeUndefined();   // a 204 is success, not the failure null
    await runEffects();

    backend.expectOne((request) => request.url === SAMPLES_API.samples).flush(page([]));
    backend.expectOne(SAMPLES_API.byNumber('SMP-2026-00001')).flush(sampleDetail('Rejected'));
    backend.verify();
  });

  it('replaces a rejected sample and keeps the new sample for its label', async () => {
    await flushInitialLoads();

    const pending = facade.replace.execute({ id: 'smp-1', note: null });
    await settle();
    backend.expectOne(SAMPLES_API.replace('smp-1')).flush({ id: 'smp-2' }, { status: 201, statusText: 'Created' });
    await settle();
    backend.expectOne(SAMPLES_API.sample('smp-2')).flush(sampleDetail('Received', { id: 'smp-2', sampleNumber: 'SMP-2026-00077', generation: 1 }));

    expect((await pending)?.summary.sampleNumber).toBe('SMP-2026-00077');
    expect(facade.replacement()?.summary.id).toBe('smp-2');
    await runEffects();
    backend.expectOne((request) => request.url === SAMPLES_API.samples).flush(page([]));
    backend.verify();
  });

  it('keeps the SMP-016 refusal of an unreject under the new-barcode policy', async () => {
    await flushInitialLoads();

    const pending = facade.unreject.execute({ id: 'smp-1', note: null });
    await settle();
    backend.expectOne(SAMPLES_API.unreject('smp-1')).flush(problemBody('SMP-016', 403), { status: 403, statusText: 'Forbidden' });

    expect(await pending).toBeNull();
    expect(facade.unreject.problem()?.code).toBe('SMP-016');
    backend.verify();
  });

  it('B-03: sorts the rejected samples on the server from page 1 (rejection time)', async () => {
    await flushInitialLoads();
    facade.setSort({ key: 'rejectedAtUtc', direction: 'desc' });
    await runEffects();
    const sorted = backend.expectOne((request) => request.url === SAMPLES_API.samples);
    expect([sorted.request.params.get('sort'), sorted.request.params.get('direction'), sorted.request.params.get('status')]).toEqual(['rejectedAtUtc', 'desc', 'Rejected']);
    sorted.flush(page([]));
  });

  it('bounds the rejected list by local dates', async () => {
    await flushInitialLoads();

    facade.setDates('2026-10-01', '2026-10-09');
    await runEffects();
    const filtered = backend.expectOne((request) => request.url === SAMPLES_API.samples);
    expect(filtered.request.params.get('from')).toBe(new Date(2026, 9, 1).toISOString());
    expect(filtered.request.params.get('to')).toBe(new Date(2026, 9, 10).toISOString());
    filtered.flush(page([]));
    backend.verify();
  });
});
