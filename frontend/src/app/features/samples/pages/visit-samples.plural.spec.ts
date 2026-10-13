import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import samplesAr from '../../../../../public/i18n/samples.ar.json';
import samplesEn from '../../../../../public/i18n/samples.en.json';
import { SAMPLES_API } from '../../../core/api/samples.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { SampleLabelPrinter } from '../state/sample-label-printer';
import { VisitSamplesFacade } from '../state/visit-samples.facade';
import { page, sampleItem, settle } from '../testing/samples.fixtures';
import type { LabelSize } from '../ui/sample-labels.document';
import { VisitSamplesPage } from './visit-samples.page';

class FakeLabelPrinter implements Pick<SampleLabelPrinter, 'size' | 'printSample' | 'printVisit'> {
  readonly size = signal<LabelSize>('label-50x25');
  labels = 2;

  async printSample(): Promise<string> {
    return 'SMP';
  }

  async printVisit(): Promise<number> {
    return this.labels;
  }
}

/** Validation v0.5.0 F-04: "1 label(s) sent to the printer." now reads in the singular, the dual or the plural. */
describe('VisitSamplesPage label count wording (F-04)', () => {
  const printer = new FakeLabelPrinter();

  afterEach(() => vi.unstubAllGlobals());

  async function printAll(lang: 'en' | 'ar', labels: number): Promise<string | undefined> {
    vi.stubGlobal('fetch', (url: string) =>
      Promise.resolve(new Response(JSON.stringify(url.endsWith('/samples.en.json') ? samplesEn : url.endsWith('/samples.ar.json') ? samplesAr : {}), { status: 200 })));
    printer.labels = labels;
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
    const i18n = TestBed.inject(I18nService);
    i18n.setLanguage(lang);
    const fixture = TestBed.createComponent(VisitSamplesPage);
    const host = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('accession', 'ACC-2026-00001');
    TestBed.tick();
    await settle();
    TestBed.inject(HttpTestingController)
      .expectOne((request) => request.url === SAMPLES_API.samples)
      .flush(page([sampleItem({ id: 'a', sampleNumber: 'SMP-1', status: 'Received' })]));
    await i18n.load('samples');
    await settle();
    await fixture.whenStable();

    const button = [...host.querySelectorAll<HTMLButtonElement>('button')].find((candidate) => candidate.textContent?.includes(samplesEn['labels.printAll']) || candidate.textContent?.includes(samplesAr['labels.printAll']));
    button?.click();
    await settle();
    return TestBed.inject(ToastService).toasts().at(-1)?.text;
  }

  it.each([
    ['en', 1, '1 label sent to the printer.'],
    ['en', 3, '3 labels sent to the printer.'],
    ['ar', 1, 'تم إرسال ملصق واحد إلى الطابعة.'],
    ['ar', 2, 'تم إرسال ملصقين إلى الطابعة.'],
    ['ar', 4, 'تم إرسال 4 ملصقات إلى الطابعة.'],
    ['ar', 12, 'تم إرسال 12 ملصقًا إلى الطابعة.'],
  ] as const)('%s, %i printed → "%s"', async (lang, labels, text) => {
    expect(await printAll(lang, labels)).toBe(text);
  });
});
