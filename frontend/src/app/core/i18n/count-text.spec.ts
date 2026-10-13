import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { countForm, countKey, countKeys } from './count-text';
import { I18nService } from './i18n.service';

describe('count text (validation v0.5.0 F-04)', () => {
  it.each([
    [0, 'None'],
    [1, 'One'],
    [2, ''],
    [3, ''],
    [11, ''],
    [100, ''],
  ])('English %i reads the %s form', (value, form) => {
    expect(countForm(value, 'en')).toBe(form);
  });

  it.each([
    [0, 'None'],
    [1, 'One'],
    [2, 'Two'],
    [3, ''],
    [10, ''],
    [11, 'Many'],
    [99, 'Many'],
    [103, ''],
    [111, 'Many'],
  ])('Arabic %i reads the %s form (zero, one, dual, 3–10 plural, 11+ singular)', (value, form) => {
    expect(countForm(value, 'ar')).toBe(form);
  });

  it('names the variant key and always ends the candidates with the plural base key', () => {
    expect(countKey('platform.parameters.saved', 1)).toBe('platform.parameters.savedOne');
    expect(countKeys('platform.parameters.saved', 1, 'en')).toEqual(['platform.parameters.savedOne', 'platform.parameters.saved']);
    expect(countKeys('platform.parameters.saved', 5, 'en')).toEqual(['platform.parameters.saved']);
    expect(countKeys('platform.parameters.saved', 2, 'ar')).toEqual(['platform.parameters.savedTwo', 'platform.parameters.saved']);
  });

  describe('I18nService.tc', () => {
    const dictionaries: Record<string, Record<string, string>> = {
      'demo.en': { saved: '{count} parameters saved.', savedOne: '1 parameter saved.', read: 'Read {count} samples from {serial}.' },
      'demo.ar': { saved: 'تم حفظ {count} إعدادات.', savedOne: 'تم حفظ إعداد واحد.', savedTwo: 'تم حفظ إعدادين.', savedMany: 'تم حفظ {count} إعدادًا.' },
    };

    async function service(lang: 'en' | 'ar'): Promise<I18nService> {
      vi.stubGlobal('fetch', (url: string) => {
        const name = /\/i18n\/(.+)\.json$/.exec(url)?.[1] ?? '';
        return Promise.resolve(new Response(JSON.stringify(dictionaries[name] ?? {}), { status: 200 }));
      });
      TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
      const i18n = TestBed.inject(I18nService);
      i18n.setLanguage(lang);
      await i18n.load('demo');
      return i18n;
    }

    afterEach(() => vi.unstubAllGlobals());

    it('reads the singular for one and the plural otherwise, in English', async () => {
      const i18n = await service('en');

      expect(i18n.tc('demo.saved', 1)).toBe('1 parameter saved.');
      expect(i18n.tc('demo.saved', 3)).toBe('3 parameters saved.');
      expect(i18n.tc('demo.saved', 0)).toBe('0 parameters saved.');   // no None variant: the plural base
    });

    it('reads one, dual, 3–10 and 11+ in Arabic, never "إعداد/إعدادات"', async () => {
      const i18n = await service('ar');

      expect(i18n.tc('demo.saved', 1)).toBe('تم حفظ إعداد واحد.');
      expect(i18n.tc('demo.saved', 2)).toBe('تم حفظ إعدادين.');
      expect(i18n.tc('demo.saved', 7)).toBe('تم حفظ 7 إعدادات.');
      expect(i18n.tc('demo.saved', 12)).toBe('تم حفظ 12 إعدادًا.');
    });

    it('fills the other placeholders and lets a caller override the count text', async () => {
      const i18n = await service('en');

      expect(i18n.tc('demo.read', 4, { serial: 'SN-1' })).toBe('Read 4 samples from SN-1.');
      expect(i18n.tc('demo.saved', 4, { count: 'four' })).toBe('four parameters saved.');
    });

    it('shows the base key when the dictionary has none of the forms', async () => {
      const i18n = await service('en');

      expect(i18n.tc('demo.unknown', 1)).toBe('demo.unknown');
    });
  });
});
