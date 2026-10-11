import platformAr from '../../../../../public/i18n/platform.ar.json';
import platformEn from '../../../../../public/i18n/platform.en.json';
import { SETTING_KEYS } from '../../../core/api/settings.contracts';

/**
 * Validation 2026-10-11 C-11: every catalogued parameter has its label and its hint in both dictionaries, so the Arabic
 * page never falls back to the server's English description.
 */
describe('Platform parameters dictionaries', () => {
  const keys = Object.values(SETTING_KEYS);

  it.each([
    ['en', platformEn as Record<string, string>],
    ['ar', platformAr as Record<string, string>],
  ])('%s carries a label and a hint for every parameter', (_, dictionary) => {
    const missing = keys.flatMap((key) => [`parameters.key.${key}`, `parameters.hint.${key}`]).filter((entry) => !(entry in dictionary));
    expect(missing).toEqual([]);
  });

  it('the Arabic hints are Arabic, not copies of the English ones', () => {
    const ar = platformAr as Record<string, string>;
    const en = platformEn as Record<string, string>;
    const copied = keys.map((key) => `parameters.hint.${key}`).filter((entry) => ar[entry] === en[entry] || !/[؀-ۿ]/.test(ar[entry] ?? ''));
    expect(copied).toEqual([]);
  });
});
