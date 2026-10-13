import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import resultsAr from '../../../../../public/i18n/results.ar.json';
import resultsEn from '../../../../../public/i18n/results.en.json';
import sharedAr from '../../../../../public/i18n/shared.ar.json';
import { reportTemplateImageUrl } from '../../../core/api/catalogue-b.contracts';
import type { LaboratoryReportDocument, ReportTemplateInfo } from '../../../core/api/results.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { documentTexts } from '../../../shared/print/document-texts';
import { PrintService } from '../../../shared/print/print.service';
import type { ReportTemplateImageUrls } from '../../../shared/print/report-template-images';
import { ResultsPrinter } from '../state/results-printer';
import { reportDocument, settle } from '../testing/results.fixtures';
import { ReportDocumentComponent } from './report.document';

const template: ReportTemplateInfo = {
  templateId: 'tpl-1',
  nameEn: 'Results A4',
  language: 'En',
  showQr: true,
  logoFileId: 'file-logo',
  signatureFileId: 'file-sig',
  headerTextEn: 'Accredited laboratory',
  headerTextAr: 'مختبر معتمد',
  footerTextEn: 'Thank you',
  footerTextAr: 'شكرا',
};

const images: ReportTemplateImageUrls = { logoUrl: 'blob:logo', signatureUrl: 'blob:signature' };

describe('Laboratory report with the template images (validation F-02)', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  function render(doc: LaboratoryReportDocument, urls: ReportTemplateImageUrls | null): HTMLElement {
    const fixture = TestBed.createComponent(ReportDocumentComponent);
    fixture.componentRef.setInput('doc', doc);
    fixture.componentRef.setInput('images', urls);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('heads the report with the template logo and signs a verified report with the template signature', () => {
    const host = render(reportDocument({ template }), images);

    expect(host.querySelector('img.template-logo')?.getAttribute('src')).toBe('blob:logo');
    expect(host.querySelector('img[src="/brand/sky-lis-logo.png"]')).toBeNull();
    expect(host.querySelector('.signature img.template-signature')?.getAttribute('src')).toBe('blob:signature');
    expect(host.textContent).toContain('Accredited laboratory');
    expect(host.textContent).toContain('Thank you');
  });

  it('keeps the Sky LIS mark without a template logo and never signs a preliminary or unverified report', () => {
    const withoutImages = render(reportDocument({ template }), null);
    expect(withoutImages.querySelector('img[src="/brand/sky-lis-logo.png"]')).not.toBeNull();
    expect(withoutImages.querySelector('img.template-signature')).toBeNull();

    expect(render(reportDocument({ template, isPreliminary: true }), images).querySelector('img.template-signature')).toBeNull();
    const unverified = reportDocument({ template, signature: { verifiedByName: null, verifiedAtUtc: null, reviewedByName: null, reviewedAtUtc: null } });
    expect(render(unverified, images).querySelector('img.template-signature')).toBeNull();
  });

  it('prints the official copy with the images of its template, then releases them', async () => {
    const created: string[] = [];
    const revoked: string[] = [];
    const original = { create: URL.createObjectURL, revoke: URL.revokeObjectURL };
    URL.createObjectURL = (): string => {
      created.push(`blob:${created.length + 1}`);
      return created[created.length - 1]!;
    };
    URL.revokeObjectURL = (url: string): void => void revoked.push(url);
    try {
      let printedInputs: Readonly<Record<string, unknown>> | undefined;
      TestBed.overrideProvider(PrintService, {
        useValue: {
          print: (request: { component?: { inputs?: Readonly<Record<string, unknown>> } }) => {
            printedInputs = request.component?.inputs;
            return Promise.resolve();
          },
        },
      });
      const printer = TestBed.inject(ResultsPrinter);
      const backend = TestBed.inject(HttpTestingController);

      const printing = printer.report(reportDocument({ template }));
      await settle();
      backend.expectOne(reportTemplateImageUrl('tpl-1', 'logo')).flush(new Blob(['png']));
      backend.expectOne(reportTemplateImageUrl('tpl-1', 'signature')).flush(new Blob(['sig']));
      await printing;

      const printed = printedInputs?.['images'] as ReportTemplateImageUrls;
      expect([printed.logoUrl, printed.signatureUrl]).toEqual(['blob:1', 'blob:2']);
      expect(revoked).toEqual(['blob:1', 'blob:2']);
    } finally {
      URL.createObjectURL = original.create;
      URL.revokeObjectURL = original.revoke;
    }
  });
});

describe('Laboratory report language and identity (validation v0.5.0 F-12, F-06, F-11)', () => {
  afterEach(() => vi.restoreAllMocks());

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  function render(doc: LaboratoryReportDocument, texts: ReturnType<typeof documentTexts> | null): HTMLElement {
    const fixture = TestBed.createComponent(ReportDocumentComponent);
    fixture.componentRef.setInput('doc', doc);
    fixture.componentRef.setInput('texts', texts);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('an Arabic report printed from the English screen is Arabic as a whole: frame, direction, captions; numbers stay Latin', () => {
    TestBed.inject(I18nService).setLanguage('en');
    const arabic = documentTexts('ar', { results: resultsAr, shared: sharedAr });
    const host = render(reportDocument({ language: 'ar', printCount: 2, amendmentCount: 3 }), arabic);

    const page = host.querySelector('.print-page')!;
    expect([page.getAttribute('lang'), page.getAttribute('dir')]).toEqual(['ar', 'rtl']);
    const text = host.textContent ?? '';
    expect(text).toContain(resultsAr['report.patient']);
    expect(text).not.toContain(resultsEn['report.patient']);
    expect(text).toContain(sharedAr['print.printedBy']);
    expect(text).toContain('تقرير المختبر');
    expect(text).toContain('منى عادل');
    // F-11 forms with Latin digits on paper: two copies, three amendments
    expect(text).toContain('إعادة طباعة (طُبعت نسختان)');
    expect(text).toContain('عُدِّل 3 مرات');
    expect(text).not.toMatch(/[٠-٩]/);
  });

  it('an English report keeps English captions and its left-to-right frame on an Arabic screen', () => {
    TestBed.inject(I18nService).setLanguage('ar');
    const host = render(reportDocument({ language: 'en', printCount: 12 }), documentTexts('en', { results: resultsEn }));
    const page = host.querySelector('.print-page')!;
    expect([page.getAttribute('lang'), page.getAttribute('dir')]).toEqual(['en', 'ltr']);
    expect(host.textContent).toContain(resultsEn['report.patient']);
    expect(host.textContent).toContain('Reprint (12 copies printed)');
    expect(host.textContent).not.toContain(resultsAr['report.patient']);
    TestBed.inject(I18nService).setLanguage('en');
  });

  it('F-06: a VIP patient previewed without the See-VIP right reads "VIP patient" in the report language', () => {
    const doc = reportDocument({ isPreview: true });
    const host = render({ ...doc, patient: { ...doc.patient, fullName: 'VIP patient', nameAr: null, dateOfBirth: null, isMasked: true } }, documentTexts('ar', { results: resultsAr }));
    expect(host.textContent).toContain(resultsAr.vipMasked);
    expect(host.textContent).toContain('PAT-2026-00001');
  });

  it('the printer renders the report with the texts of its own language', async () => {
    const fetched: string[] = [];
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input: string | URL | Request) => {
      fetched.push(String(input));
      return new Response(JSON.stringify(String(input).endsWith('results.ar.json') ? resultsAr : {}), { status: 200 });
    });
    let request: { lang?: string; dir?: string; component?: { inputs?: Readonly<Record<string, unknown>> } } | undefined;
    TestBed.overrideProvider(PrintService, {
      useValue: {
        print: (value: typeof request) => {
          request = value;
          return Promise.resolve();
        },
      },
    });
    TestBed.inject(I18nService).setLanguage('en');

    await TestBed.inject(ResultsPrinter).report(reportDocument({ language: 'ar' }));

    expect([request?.lang, request?.dir]).toEqual(['ar', 'rtl']);
    const texts = request?.component?.inputs?.['texts'] as ReturnType<typeof documentTexts>;
    expect(texts.t('results.report.patient')).toBe(resultsAr['report.patient']);
    expect(fetched).toEqual(expect.arrayContaining(['/i18n/results.ar.json', '/i18n/shared.ar.json']));
  });
});
