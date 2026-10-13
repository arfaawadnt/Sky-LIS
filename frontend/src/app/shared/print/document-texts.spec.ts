import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';

import { DocumentTextsLoader, documentTexts } from './document-texts';

describe('Document texts in the document language (validation v0.5.0 F-12)', () => {
  afterEach(() => vi.restoreAllMocks());

  it('translates, fills placeholders and picks the counted form with Latin digits', () => {
    const texts = documentTexts('ar', {
      results: { 'report.reprint': 'إعادة طباعة (طُبعت {count} نسخ)', 'report.reprintTwo': 'إعادة طباعة (طُبعت نسختان)', 'report.reprintMany': 'إعادة طباعة (طُبعت {count} نسخة)', 'report.title': 'تقرير {accession}' },
    });

    expect([texts.lang, texts.dir]).toEqual(['ar', 'rtl']);
    expect(texts.tf('results.report.title', { accession: 'ACC-1' })).toBe('تقرير ACC-1');
    expect(texts.tc('results.report.reprint', 2)).toBe('إعادة طباعة (طُبعت نسختان)');
    expect(texts.tc('results.report.reprint', 5)).toBe('إعادة طباعة (طُبعت 5 نسخ)');
    expect(texts.tc('results.report.reprint', 12)).toBe('إعادة طباعة (طُبعت 12 نسخة)');
    expect(texts.tc('results.report.reprint', 1)).toBe('إعادة طباعة (طُبعت 1 نسخ)');   // a missing form falls back to the base
    expect(texts.t('results.unknown')).toBe('results.unknown');
    expect(documentTexts('en', {}).dir).toBe('ltr');
  });

  it('loads each dictionary of the document language once, whatever the screen language', async () => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    const fetched: string[] = [];
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input: string | URL | Request) => {
      fetched.push(String(input));
      return new Response(JSON.stringify({ 'report.patient': 'المريض' }), { status: 200 });
    });
    const loader = TestBed.inject(DocumentTextsLoader);

    const first = await loader.load('ar', ['results', 'shared']);
    await loader.load('ar', ['results']);

    expect(first.t('results.report.patient')).toBe('المريض');
    expect(fetched).toEqual(['/i18n/results.ar.json', '/i18n/shared.ar.json']);
  });

  it('tries a failed dictionary again on the next document', async () => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    let calls = 0;
    vi.spyOn(globalThis, 'fetch').mockImplementation(async () => {
      calls++;
      return calls === 1 ? new Response('', { status: 503 }) : new Response(JSON.stringify({ 'report.patient': 'Patient' }), { status: 200 });
    });
    const loader = TestBed.inject(DocumentTextsLoader);

    expect((await loader.load('en', ['results'])).t('results.report.patient')).toBe('results.report.patient');
    expect((await loader.load('en', ['results'])).t('results.report.patient')).toBe('Patient');
  });
});
