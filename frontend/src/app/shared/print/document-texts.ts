import { Injectable } from '@angular/core';

import { countKeys } from '../../core/i18n/count-text';
import type { Language } from '../../core/i18n/i18n.service';

type Dictionary = Readonly<Record<string, string>>;

/**
 * The texts of a printed document in the document's own language, which may differ from the screen's (validation v0.5.0
 * F-12: a report printed from the Arabic screen had Arabic captions inside an English, left-to-right frame). Captions,
 * frame language and direction all come from one language; numbers stay in Latin digits (printed documents keep Latin,
 * F-09/F-10). Keys are `feature.key` as in `I18nService`; an unknown key renders as itself.
 */
export interface DocumentTexts {
  readonly lang: Language;
  readonly dir: 'ltr' | 'rtl';
  t(key: string): string;
  tf(key: string, params: Readonly<Record<string, string | number>>): string;
  /** A counted sentence in its grammatical form (`count-text.ts`), the count in Latin digits. */
  tc(key: string, count: number, params?: Readonly<Record<string, string | number>>): string;
}

/** Texts over already loaded dictionaries (`<feature>` → entries) of one language. */
export function documentTexts(lang: Language, dictionaries: Readonly<Record<string, Dictionary>>): DocumentTexts {
  const t = (key: string): string => {
    const dot = key.indexOf('.');
    return dot <= 0 ? key : (dictionaries[key.slice(0, dot)]?.[key.slice(dot + 1)] ?? key);
  };
  const tf = (key: string, params: Readonly<Record<string, string | number>>): string =>
    t(key).replace(/\{(\w+)\}/g, (match, name: string) => {
      const value = params[name];
      return value === undefined ? match : String(value);
    });
  return {
    lang,
    dir: lang === 'ar' ? 'rtl' : 'ltr',
    t,
    tf,
    tc: (key, count, params = {}) => tf(countKeys(key, count, lang).find((candidate) => t(candidate) !== candidate) ?? key, { count: String(count), ...params }),
  };
}

/**
 * Loads the dictionaries a document needs in its own language (`/i18n/<feature>.<lang>.json`, once per feature and
 * language) without switching the screen's language.
 */
@Injectable({ providedIn: 'root' })
export class DocumentTextsLoader {
  private readonly cache = new Map<string, Promise<Dictionary>>();

  async load(lang: Language, features: readonly string[]): Promise<DocumentTexts> {
    const loaded = await Promise.all(features.map(async (feature) => [feature, await this.dictionary(feature, lang)] as const));
    return documentTexts(lang, Object.fromEntries(loaded));
  }

  private dictionary(feature: string, lang: Language): Promise<Dictionary> {
    const key = `${feature}.${lang}`;
    let request = this.cache.get(key);
    if (request === undefined) {
      request = fetch(`/i18n/${key}.json`, { credentials: 'same-origin' })
        .then(async (response) => {
          if (!response.ok) {
            throw new Error(`${key}: ${response.status}`);
          }

          return (await response.json()) as Dictionary;
        })
        .catch((): Dictionary => {
          this.cache.delete(key);   // a failed load is tried again on the next document
          return {};
        });
      this.cache.set(key, request);
    }

    return request;
  }
}
