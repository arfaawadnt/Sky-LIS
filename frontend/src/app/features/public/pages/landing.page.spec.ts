import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import publicAr from '../../../../../public/i18n/public.ar.json';
import publicEn from '../../../../../public/i18n/public.en.json';
import { LandingPage } from './landing.page';

/** Validation 2026-10-11 C-12: the "Release 1 highlights" list Release 1 features only (sample tracking and contract pricing are Release 2). */
describe('LandingPage release highlights', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LandingPage],
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
  });

  it('shows six Release 1 highlights and none of Release 2', async () => {
    const fixture = TestBed.createComponent(LandingPage);
    await fixture.whenStable();

    const items = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('ol.release li'));
    expect(items.length).toBe(6);
    const text = items.map((item) => item.textContent ?? '').join(' ');
    expect(text).not.toMatch(/sample tracking|contract pricing|release\.samples/i);
    expect(text).toMatch(/catalogue/i);   // the key or its text: the dictionary may not be loaded in a test
  });

  it('the feature grid, the portal cards and the demo figures show Release 1 only (UAT R2-05)', async () => {
    const fixture = TestBed.createComponent(LandingPage);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;

    const features = Array.from(root.querySelectorAll('.feature-grid .feature')).map((item) => item.textContent ?? '');
    expect(features.length).toBe(5);
    const page = root.textContent ?? '';
    for (const key of ['feature.samples', 'feature.analysers', 'feature.automation', 'admin.library', 'demo.received']) {
      expect(page).not.toContain(`public.${key}`);
    }

    expect(page).toContain('public.feature.catalogue.title');
    expect(page).toContain('public.admin.operations');
  });

  it.each([
    ['en', publicEn as Record<string, string>],
    ['ar', publicAr as Record<string, string>],
  ])('%s portal and feature texts shown in Release 1 advertise no Release 2 feature (UAT R2-05)', (_, dictionary) => {
    const shown = ['portals.admin.text', 'admin.operations', 'feature.registration.text', 'feature.finance.text', 'feature.catalogue.text', 'feature.setup.text'];
    const text = shown.map((key) => dictionary[key] ?? '').join(' ');
    expect(shown.every((key) => (dictionary[key] ?? '').length > 0)).toBe(true);
    expect(text).not.toMatch(/support|integration library|contract tiers|analyser|الدعم|مكتبة الربط|فئات التعاقد|الأجهزة/i);
  });

  it.each([
    ['en', publicEn as Record<string, string>],
    ['ar', publicAr as Record<string, string>],
  ])('%s hero, catalogue and finance texts name no Release 2 function (UAT R4-01)', (_, dictionary) => {
    const text = ['hero.text', 'feature.catalogue.text', 'feature.finance.text'].map((key) => dictionary[key] ?? '').join(' ');
    expect(text.length).toBeGreaterThan(0);
    expect(text).not.toMatch(/sample collection|analys|cash closing|report templates|سحب العينات|الأجهزة|إقفال الخزينة|قوالب التقارير/i);
  });

  it.each([
    ['en', publicEn as Record<string, string>],
    ['ar', publicAr as Record<string, string>],
  ])('%s release texts name no Release 2 feature', (_, dictionary) => {
    const release = Object.entries(dictionary).filter(([key]) => key.startsWith('release.'));
    expect(release.map(([key]) => key)).not.toContain('release.samples.title');
    expect(release.map(([key]) => key)).toContain('release.catalogue.title');
    const text = release.map(([, value]) => value).join(' ');
    expect(text).not.toMatch(/contract pricing|tier|Sample tracking|أسعار التعاقد|الفئة|تتبع العينات/);
  });
});
