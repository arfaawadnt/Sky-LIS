import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import organizationAr from '../../../../../public/i18n/organization.ar.json';
import organizationEn from '../../../../../public/i18n/organization.en.json';
import type { ResultRight } from '../../../core/api/organization.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { ResultRightsGridComponent } from './result-rights-grid.component';

function rights(count: number): readonly ResultRight[] {
  return Array.from({ length: count }, (_, index) => ({ clinicalGroupId: `g${index}`, branchId: null, canSee: true, canReview: false, canVerify: false, canEdit: false, canPrint: false }));
}

/** Validation v0.5.0 F-04: "1 rule(s) configured" above the result rights now reads in the right form in both languages. */
describe('ResultRightsGridComponent count wording (F-04)', () => {
  afterEach(() => vi.unstubAllGlobals());

  async function configured(lang: 'en' | 'ar', count: number): Promise<string> {
    vi.stubGlobal('fetch', (url: string) =>
      Promise.resolve(new Response(JSON.stringify(url.endsWith('/organization.en.json') ? organizationEn : url.endsWith('/organization.ar.json') ? organizationAr : {}), { status: 200 })));
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    const i18n = TestBed.inject(I18nService);
    i18n.setLanguage(lang);
    await i18n.load('organization');
    const fixture = TestBed.createComponent(ResultRightsGridComponent);
    fixture.componentRef.setInput('groups', []);
    fixture.componentRef.setInput('branches', []);
    fixture.componentRef.setInput('value', rights(count));
    await fixture.whenStable();
    return (fixture.nativeElement as HTMLElement).querySelector('.scope .muted')?.textContent?.trim() ?? '';
  }

  it.each([
    ['en', 0, 'No rule configured'],
    ['en', 1, '1 rule configured'],
    ['en', 5, '5 rules configured'],
    ['ar', 0, 'لم تُضبط أي قاعدة'],
    ['ar', 1, 'تم ضبط قاعدة واحدة'],
    ['ar', 2, 'تم ضبط قاعدتين'],
    ['ar', 5, 'تم ضبط 5 قواعد'],
    ['ar', 11, 'تم ضبط 11 قاعدة'],
  ] as const)('%s, %i → "%s"', async (lang, count, text) => {
    expect(await configured(lang, count)).toBe(text);
  });
});
