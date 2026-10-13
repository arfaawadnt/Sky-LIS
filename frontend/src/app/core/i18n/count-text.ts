import type { Language } from './i18n.service';

/**
 * The grammatical form of a counted sentence, as the suffix of its dictionary key:
 *
 * | count          | English        | Arabic                                   | suffix |
 * | -------------- | -------------- | ---------------------------------------- | ------ |
 * | 0              | `<key>None`    | `<key>None` ("لم يُحفظ أي إعداد")          | `None` |
 * | 1              | `<key>One`     | `<key>One` (noun + واحد)                  | `One`  |
 * | 2              | `<key>` (plural) | `<key>Two` (dual)                       | `Two`  |
 * | 3–10 (mod 100) | `<key>`        | `<key>` (plural noun)                     | ``     |
 * | 11+            | `<key>`        | `<key>Many` (singular noun, accusative)   | `Many` |
 *
 * The base key always holds the plural ("{count} parameters saved."), so a missing variant falls back to it and a
 * dictionary never needs "(s)" or "إعداد/إعدادات" again (validation v0.5.0 F-04). Same rule as the Registration
 * tube texts (`registration-format.ts` `countKey`).
 */
export type CountForm = 'None' | 'One' | 'Two' | '' | 'Many';

export function countForm(value: number, lang: Language = 'en'): CountForm {
  if (value === 0) {
    return 'None';
  }

  if (value === 1) {
    return 'One';
  }

  if (lang !== 'ar') {
    return '';
  }

  const rest = value % 100;
  return value === 2 ? 'Two' : rest >= 3 && rest <= 10 ? '' : 'Many';
}

/** `countKey('platform.parameters.saved', 1)` → `platform.parameters.savedOne`. */
export function countKey(base: string, value: number, lang: Language = 'en'): string {
  return `${base}${countForm(value, lang)}`;
}

/** The keys to try for a count, most specific first, ending with the base key (the plural). */
export function countKeys(base: string, value: number, lang: Language = 'en'): readonly string[] {
  const specific = countKey(base, value, lang);
  return specific === base ? [base] : [specific, base];
}
