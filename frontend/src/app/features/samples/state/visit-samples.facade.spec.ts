import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { SAMPLES_API } from '../../../core/api/samples.contracts';
import { page, sampleItem, settle } from '../testing/samples.fixtures';
import { SampleLabelPrinter } from './sample-label-printer';
import { VisitSamplesFacade } from './visit-samples.facade';

describe('VisitSamplesFacade (CP-15)', () => {
  let facade: VisitSamplesFacade;
  let backend: HttpTestingController;
  const printed: string[] = [];

  beforeEach(() => {
    printed.length = 0;
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: SampleLabelPrinter,
          useValue: {
            printSample: async (id: string) => {
              printed.push(`sample:${id}`);
              return 'SMP';
            },
            printVisit: async (id: string) => {
              printed.push(`visit:${id}`);
              return 2;
            },
          } satisfies Pick<SampleLabelPrinter, 'printSample' | 'printVisit'>,
        },
        VisitSamplesFacade,
      ],
    });
    facade = TestBed.inject(VisitSamplesFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  it('re-reads the tubes of the visit on screen when the page opens again, and stays idle without one (UAT D-14, A-01)', async () => {
    facade.refresh();
    await runEffects();
    backend.expectNone(() => true);

    facade.show({ accession: 'ACC-2026-00001', visitId: null });
    await runEffects();
    backend.expectOne((request) => request.url === SAMPLES_API.samples).flush(page([]));
    await runEffects();

    facade.refresh();
    await runEffects();
    backend.expectOne((request) => request.url === SAMPLES_API.samples).flush(page([]));
    backend.verify();
  });

  it('stays idle until an accession is given, then lists its tubes by unit', async () => {
    await runEffects();
    backend.expectNone(() => true);
    expect(facade.visit()).toBeNull();

    facade.show({ accession: ' ACC-2026-00001 ', visitId: 'ignored-when-an-accession-is-given' });
    await runEffects();
    const request = backend.expectOne((candidate) => candidate.url === SAMPLES_API.samples);
    expect(request.request.params.get('accession')).toBe('ACC-2026-00001');
    expect(request.request.params.has('visitId')).toBe(false);
    expect(request.request.params.get('pageSize')).toBe('200');
    request.flush(page([sampleItem({ id: 'b', unitCode: 'H', sampleNumber: 'SMP-2' }), sampleItem({ id: 'a', unitCode: 'C', sampleNumber: 'SMP-1' })]));
    await runEffects();

    expect(facade.rows().map((row) => row.id)).toEqual(['a', 'b']);
    expect(facade.visit()?.accession).toBe('ACC-2026-00001');
    backend.verify();
  });

  it('opens a visit by id and prints one label or all labels', async () => {
    facade.show({ accession: null, visitId: 'visit-7' });
    await runEffects();
    backend.expectOne((candidate) => candidate.url === SAMPLES_API.samples && candidate.params.get('visitId') === 'visit-7').flush(page([sampleItem({ visitId: 'visit-7' })]));
    await runEffects();

    await facade.printOne.execute('smp-1');
    await facade.printAll.execute('visit-7');

    expect(printed).toEqual(['sample:smp-1', 'visit:visit-7']);
    backend.verify();
  });
});
