import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import resultsAr from '../../../../../../../public/i18n/results.ar.json';
import resultsEn from '../../../../../../../public/i18n/results.en.json';
import sharedAr from '../../../../../../../public/i18n/shared.ar.json';
import sharedEn from '../../../../../../../public/i18n/shared.en.json';
import { REPORT_DESIGNS, type ReportDesign } from '../../../../../core/api/report-designs';
import type { LaboratoryReportDocument, ReportLine } from '../../../../../core/api/results.contracts';
import { I18nService } from '../../../../../core/i18n/i18n.service';
import { type DocumentTexts, documentTexts } from '../../../../../shared/print/document-texts';
import { reportDocument } from '../../../testing/results.fixtures';
import { ReportDocumentComponent } from '../../report.document';

/** LIS-CR-010 (reviewer point C-14): the dispatcher and the five designs over one sample case, in both languages. */
describe('Report designs (LIS-CR-010)', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    TestBed.inject(I18nService).setLanguage('en');
  });

  const culture: ReportLine = {
    resultId: 'res-c',
    nameEn: 'Urine culture',
    nameAr: 'مزرعة بول',
    testType: 'Culture',
    value: null,
    unit: null,
    range: null,
    flag: null,
    isPanic: false,
    status: 'Verified',
    comment: null,
    paragraphs: null,
    culture: { noGrowth: true, isolates: [] },
    methodology: null,
  };

  const high: ReportLine = {
    resultId: 'res-2',
    nameEn: 'Potassium',
    nameAr: 'بوتاسيوم',
    testType: 'Numeric',
    value: '6.8',
    unit: 'mmol/L',
    range: '3.5 – 5.0',
    flag: 'CriticalHigh',
    isPanic: true,
    status: 'Verified',
    comment: 'Repeat advised.',
    paragraphs: null,
    culture: null,
    methodology: null,
    previous: [
      { accession: 'ACC-2026-00009', registeredAtUtc: '2026-09-20T08:00:00Z', value: '5.2', unit: 'mmol/L', flag: 'High' },
      { accession: 'ACC-2026-00005', registeredAtUtc: '2026-08-15T08:00:00Z', value: '4.4', unit: 'mmol/L', flag: 'Normal' },
    ],
  };

  function sample(design: ReportDesign | undefined, overrides: Partial<LaboratoryReportDocument> = {}): LaboratoryReportDocument {
    const base = reportDocument(overrides);
    const section = base.sections[0]!;
    return { ...base, ...(design === undefined ? {} : { design }), sections: [{ ...section, lines: [...section.lines, high, culture] }] };
  }

  const english = (): DocumentTexts => documentTexts('en', { results: resultsEn, shared: sharedEn });

  function render(doc: LaboratoryReportDocument, texts: DocumentTexts | null = null, images: { logoUrl: string | null; signatureUrl: string | null } | null = null): HTMLElement {
    const fixture = TestBed.createComponent(ReportDocumentComponent);
    fixture.componentRef.setInput('doc', doc);
    fixture.componentRef.setInput('texts', texts);
    fixture.componentRef.setInput('images', images);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('dispatches on doc.design and prints Classic for a document without one', () => {
    expect(render(sample(undefined)).querySelector('.design-classic')).not.toBeNull();
    for (const design of REPORT_DESIGNS) {
      const host = render(sample(design));
      expect(host.querySelector(`.design-${design.toLowerCase()}`)).not.toBeNull();
      expect(host.querySelectorAll('.print-page')).toHaveLength(1);
    }
  });

  it.each(REPORT_DESIGNS)('%s renders the sample case: patient, every line, the culture, the comment and the signature', (design) => {
    const host = render(sample(design), english());
    const text = host.textContent ?? '';
    expect(text).toContain('Mona Adel');
    expect(text).toContain('PAT-2026-00001');
    expect(text).toContain('Glucose');
    expect(text).toContain('Potassium');
    expect(text).toContain('6.8');
    expect(text).toContain(resultsEn['report.noGrowth']);
    expect(text).toContain('Repeat advised.');
    expect(text).toContain('Dr. Hassan');
    expect(host.querySelector('.signature')).not.toBeNull();
  });

  it.each(REPORT_DESIGNS)('%s is Arabic as a whole from an English screen: direction, captions, Latin digits', (design) => {
    const host = render(sample(design, { language: 'ar' }), documentTexts('ar', { results: resultsAr, shared: sharedAr }));
    const page = host.querySelector('.print-page')!;
    expect([page.getAttribute('lang'), page.getAttribute('dir')]).toEqual(['ar', 'rtl']);
    const text = host.textContent ?? '';
    expect(text).toContain(resultsAr['report.patient']);
    expect(text).not.toContain(resultsEn['report.patient']);
    expect(text).toContain('منى عادل');
    expect(text).not.toMatch(/[٠-٩]/);
  });

  it('Modern colours each row by its flag and draws a range bar where the value and the range are numeric', () => {
    const host = render(sample('Modern'));
    const rows = Array.from(host.querySelectorAll<HTMLTableRowElement>('table.results tbody tr'));
    expect(rows.map((row) => row.className)).toEqual(['tone-normal', 'tone-critical', '']);
    const markers = Array.from(host.querySelectorAll<HTMLElement>('.track .marker'));
    expect(markers).toHaveLength(2);
    expect(markers[0]!.classList.contains('inside')).toBe(true);
    expect(markers[1]!.classList.contains('above')).toBe(true);
    expect(markers[1]!.style.getPropertyValue('--marker')).toBe('98%');
  });

  it('Letterhead leaves the top band blank: no logo and no laboratory name', () => {
    const host = render(sample('Letterhead'), null, { logoUrl: 'blob:logo', signatureUrl: null });
    expect(host.querySelector('.band')).not.toBeNull();
    expect(host.querySelector('img')).toBeNull();
    expect(host.textContent).not.toContain('Sky Lab');
    expect(host.textContent).toContain('ACC-2026-00001');
  });

  it('Compact keeps every section in one table with the profile heading as a row', () => {
    const doc = sample('Compact');
    const twoSections = { ...doc, sections: [{ ...doc.sections[0]!, isProfile: true }, { ...doc.sections[0]!, serviceId: 'svc-2', headingEn: 'Electrolytes', isProfile: true }] };
    const host = render(twoSections);
    expect(host.querySelectorAll('table.results')).toHaveLength(1);
    expect(Array.from(host.querySelectorAll('tr.group')).map((row) => row.textContent?.trim())).toEqual(['Chemistry', 'Electrolytes']);
  });

  it('Cumulative prints three previous columns, newest first, and leaves missing ones empty', () => {
    const host = render(sample('Cumulative'), english());
    const potassium = Array.from(host.querySelectorAll('table.results tbody tr')).find((row) => row.textContent?.includes('Potassium'))!;
    const cells = Array.from(potassium.querySelectorAll('td.previous')).map((cell) => [cell.querySelector('.past')?.textContent?.trim() ?? null, cell.querySelector('.day')?.textContent?.trim()]);
    expect(cells).toEqual([
      ['5.2 H', '20/09/2026'],
      ['4.4', '15/08/2026'],
      [null, '—'],
    ]);
    expect(host.textContent).toContain(resultsEn['report.previousResults']);
  });
});
