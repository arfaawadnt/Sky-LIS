import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { SampleLabel, VisitLabelsDocument } from '../../../core/api/registration.contracts';
import { RegistrationLabelsDocument } from './registration-documents';

function label(overrides: Partial<SampleLabel> = {}): SampleLabel {
  return {
    tubeId: 'tube-1',
    sampleNumber: 'SMP-2026-00001',
    unitCode: 'C',
    sampleTypeNameEn: 'Serum',
    sampleTypeNameAr: 'مصل',
    sampleConditionNameEn: null,
    sampleConditionNameAr: null,
    accession: 'ACC-2026-00026',
    patientNumber: 'PAT-2026-00009',
    patientName: 'Mona Hassan',
    patientNameAr: 'منى حسن',
    gender: 'Female',
    ageYears: 30,
    registeredAtUtc: '2026-10-12T09:30:00Z',
    branchNameEn: 'Main Branch',
    branchNameAr: 'الفرع الرئيسي',
    urgent: false,
    serviceCodes: ['GLU'],
    biohazard: false,
    collectedOutside: false,
    ...overrides,
  };
}

function render(labels: readonly SampleLabel[], lang: 'en' | 'ar' = 'en'): HTMLElement {
  const document: VisitLabelsDocument = { letterhead: { labNameEn: 'Sky Lab', labNameAr: 'سكاي لاب', branchNameEn: null, branchNameAr: null }, accession: 'ACC-2026-00026', copies: 1, utcOffsetMinutes: 180, labels };
  const fixture = TestBed.createComponent(RegistrationLabelsDocument);
  fixture.componentRef.setInput('document', document);
  fixture.componentRef.setInput('lang', lang);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('RegistrationLabelsDocument (sample labels)', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
  });

  it('prints the biohazard mark "BIO" beside the urgent mark on a biohazard visit (validation 2026-10-11)', () => {
    const host = render([label({ urgent: true, biohazard: true }), label({ tubeId: 'tube-2', sampleNumber: 'SMP-2026-00002' })]);

    const pages = host.querySelectorAll('.print-page');
    expect(pages.length).toBe(2);
    expect(pages[0]?.querySelector('.biohazard')?.textContent?.trim()).toBe('BIO');
    expect(pages[0]?.textContent).toContain('!');
    expect(pages[1]?.querySelector('.biohazard')).toBeNull();
  });

  it('prints "EXT" on the label of a sample collected outside the laboratory only (LIS-CR-008 C-02)', () => {
    const host = render([label({ collectedOutside: true }), label({ tubeId: 'tube-2', sampleNumber: 'SMP-2026-00002' })]);

    const pages = host.querySelectorAll('.print-page');
    expect(pages[0]?.querySelector('.outside')?.textContent?.trim()).toBe('EXT');
    expect(pages[1]?.querySelector('.outside')).toBeNull();
  });

  it('keeps the Latin digits of the printed documents in Arabic', () => {
    const host = render([label({ biohazard: true })], 'ar');

    const text = host.querySelector('.print-page')?.textContent ?? '';
    expect(text).toContain('BIO');
    expect(text).toContain('منى حسن');
    expect(text).toMatch(/12\/10\/2026/);
    expect(text).not.toMatch(/[٠-٩]/);
  });
});
