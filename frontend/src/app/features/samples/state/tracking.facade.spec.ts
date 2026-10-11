import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { SAMPLES_API } from '../../../core/api/samples.contracts';
import { sampleDetail, settle, timeline } from '../testing/samples.fixtures';
import { TrackingFacade } from './tracking.facade';

describe('TrackingFacade (CP-17)', () => {
  let facade: TrackingFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), TrackingFacade],
    });
    facade = TestBed.inject(TrackingFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  it('resolves the barcode, then loads the timeline of that sample', async () => {
    await runEffects();
    backend.expectNone(() => true);

    facade.track(' smp-2026-00001 ');
    await runEffects();
    backend.expectOne(SAMPLES_API.byNumber('SMP-2026-00001')).flush(sampleDetail('Received'));
    await settle();
    backend.expectOne(SAMPLES_API.timeline('smp-1')).flush(timeline());
    await runEffects();

    expect(facade.timeline.value()?.steps.map((step) => step.action)).toEqual(['Ordered', 'Collected', 'Received']);
    expect(facade.timeline.value()?.durations.orderedToReceivedSeconds).toBe(4200);

    facade.track('');
    expect(facade.sampleNumber()).toBeNull();
    backend.verify();
  });

  it('re-reads the tracked sample when the page opens again, and stays idle without one (UAT D-14, A-01)', async () => {
    facade.refresh();
    await runEffects();
    backend.expectNone(() => true);

    facade.track('SMP-2026-00001');
    await runEffects();
    backend.expectOne(SAMPLES_API.byNumber('SMP-2026-00001')).flush(sampleDetail('Received'));
    await settle();
    backend.expectOne(SAMPLES_API.timeline('smp-1')).flush(timeline());
    await runEffects();

    facade.refresh();
    await runEffects();
    backend.expectOne(SAMPLES_API.byNumber('SMP-2026-00001')).flush(sampleDetail('Rejected'));
    await settle();
    backend.expectOne(SAMPLES_API.timeline('smp-1')).flush(timeline());
    backend.verify();
  });
});
