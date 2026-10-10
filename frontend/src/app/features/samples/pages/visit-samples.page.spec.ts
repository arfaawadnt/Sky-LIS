import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { SAMPLES_API } from '../../../core/api/samples.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { SampleLabelPrinter } from '../state/sample-label-printer';
import { VisitSamplesFacade } from '../state/visit-samples.facade';
import { page, sampleItem, settle } from '../testing/samples.fixtures';
import type { LabelSize } from '../ui/sample-labels.document';
import { VisitSamplesPage } from './visit-samples.page';

/** Records what would be printed instead of opening the print dialog. */
class FakeLabelPrinter implements Pick<SampleLabelPrinter, 'size' | 'printSample' | 'printVisit'> {
  readonly size = signal<LabelSize>('label-50x25');
  readonly printed: string[] = [];

  async printSample(sampleId: string): Promise<string> {
    this.printed.push(`sample:${sampleId}`);
    return 'SMP';
  }

  async printVisit(visitId: string): Promise<number> {
    this.printed.push(`visit:${visitId}`);
    return 2;
  }
}

describe('VisitSamplesPage (CP-15)', () => {
  let fixture: ComponentFixture<VisitSamplesPage>;
  let backend: HttpTestingController;
  let host: HTMLElement;
  let printer: FakeLabelPrinter;

  beforeEach(async () => {
    printer = new FakeLabelPrinter();
    await TestBed.configureTestingModule({
      imports: [VisitSamplesPage],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
        { provide: SampleLabelPrinter, useValue: printer },
        VisitSamplesFacade,
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(VisitSamplesPage);
    backend = TestBed.inject(HttpTestingController);
    host = fixture.nativeElement as HTMLElement;
  });

  async function render(): Promise<void> {
    TestBed.tick();
    await settle();
    fixture.detectChanges();
  }

  it('starts empty, then shows the tubes of the accession from the query parameter and prints labels', async () => {
    await render();
    backend.expectNone(() => true);
    expect(host.querySelector('app-empty-state')).not.toBeNull();

    fixture.componentRef.setInput('accession', 'ACC-2026-00001');
    await render();
    backend
      .expectOne((request) => request.url === SAMPLES_API.samples && request.params.get('accession') === 'ACC-2026-00001')
      .flush(page([sampleItem({ id: 'a', sampleNumber: 'SMP-1', status: 'Received' }), sampleItem({ id: 'b', sampleNumber: 'SMP-2', status: 'Replaced', unitCode: 'H' })]));
    await render();

    expect(host.querySelectorAll('app-data-table tbody tr').length).toBe(2);
    expect(host.textContent).toContain('ACC-2026-00001');
    const printButtons = [...host.querySelectorAll<HTMLButtonElement>('app-data-table button')].filter((button) => button.textContent?.includes('samples.labels.printOne'));
    expect(printButtons.length).toBe(1); // the replaced tube prints no label

    [...host.querySelectorAll<HTMLButtonElement>('button')].find((button) => button.textContent?.includes('samples.labels.printAll'))!.click();
    printButtons[0]!.click();
    await render();

    expect(printer.printed).toEqual(['visit:visit-1', 'sample:a']);
    backend.verify();
  });
});
