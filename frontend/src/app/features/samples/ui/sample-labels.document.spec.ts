import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { SampleLabelDocument } from '../../../core/api/samples.contracts';
import { SampleLabelsDocumentComponent } from './sample-labels.document';

function label(overrides: Partial<SampleLabelDocument> = {}): SampleLabelDocument {
  return {
    sampleId: 'smp-1',
    sampleNumber: 'SMP-2026-00001',
    accession: 'ACC-2026-00001',
    unitCode: 'C',
    clinicalGroupNameEn: 'Chemistry',
    clinicalGroupNameAr: 'الكيمياء',
    sampleTypeId: 'type-1',
    sampleTypeNameEn: 'Serum',
    sampleTypeNameAr: 'مصل',
    sampleConditionId: 'cond-1',
    sampleConditionNameEn: 'Fasting',
    sampleConditionNameAr: 'صائم',
    patientId: 'pat-1',
    patientNumber: 'PAT-2026-00001',
    patientName: 'Mona Adel',
    patientNameAr: 'منى عادل',
    branchId: 'branch-1',
    branchNameEn: 'Main',
    branchNameAr: 'الرئيسي',
    labelAtUtc: '2026-10-09T08:00:00Z',
    services: [
      { id: 's1', code: 'GLU', nameEn: 'Glucose', nameAr: 'جلوكوز' },
      { id: 's2', code: null, nameEn: 'Urea', nameAr: 'يوريا' },
    ],
    copies: 2,
    ...overrides,
  };
}

describe('SampleLabelsDocumentComponent (CP-15 labels)', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [SampleLabelsDocumentComponent], providers: [provideZonelessChangeDetection()] });
  });

  it('renders one page per copy with the barcode and the LDM label content', async () => {
    const fixture = TestBed.createComponent(SampleLabelsDocumentComponent);
    fixture.componentRef.setInput('labels', [label(), label({ sampleId: 'smp-2', sampleNumber: 'SMP-2026-00002', copies: 1 })]);
    fixture.componentRef.setInput('lang', 'ar');
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;

    const pages = host.querySelectorAll('.print-page');
    expect(pages.length).toBe(3);
    expect(host.querySelectorAll('svg.code128').length).toBe(3);
    const first = pages[0]!.textContent ?? '';
    expect(first).toContain('ACC-2026-00001');
    expect(first).toContain('PAT-2026-00001 · منى عادل');
    expect(first).toContain('مصل · صائم — الرئيسي');
    expect(first).toContain('GLU, يوريا');
  });

  it('sizes the text for the small label', async () => {
    const fixture = TestBed.createComponent(SampleLabelsDocumentComponent);
    fixture.componentRef.setInput('labels', [label({ copies: 1 })]);
    fixture.componentRef.setInput('size', 'label-38x25');
    await fixture.whenStable();

    const pageElement = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.print-page');
    expect(pageElement?.style.fontSize).toBe('6pt');
  });
});
