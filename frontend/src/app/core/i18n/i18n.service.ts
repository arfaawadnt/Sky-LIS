import { DOCUMENT } from '@angular/common';
import { Injectable, computed, inject, signal } from '@angular/core';

export type Language = 'en' | 'ar';
export type Direction = 'ltr' | 'rtl';

type Dictionary = Readonly<Record<string, string>>;

/**
 * Runtime language switching with RTL (01 §6.6): `lang` and `dir` signals at the shell root, one JSON dictionary
 * per feature loaded with the route (`public/i18n/<feature>.<lang>.json`), and `t(key)` that reads signals so
 * templates re-render on a switch. Keys are `feature.key`; an unknown key renders as itself so it is visible.
 */
@Injectable({ providedIn: 'root' })
export class I18nService {
  private static readonly SUPPORTED: readonly Language[] = ['en', 'ar'];
  private readonly document = inject(DOCUMENT);
  private readonly dictionaries = signal<Readonly<Record<string, Dictionary>>>({});
  private readonly loading = new Map<string, Promise<void>>();

  readonly lang = signal<Language>('en');

  readonly dir = computed<Direction>(() => (this.lang() === 'ar' ? 'rtl' : 'ltr'));

  setLanguage(language: string): void {
    const next = I18nService.SUPPORTED.find((supported) => supported === language) ?? 'en';
    this.lang.set(next);
    this.document.documentElement.lang = next;
    this.document.documentElement.dir = this.dir();
    for (const feature of new Set([...Object.keys(this.dictionaries())].map((key) => key.split('.')[0] ?? ''))) {
      void this.load(feature);
    }
  }

  /** Loads a feature's dictionary for the current language (idempotent per feature and language). */
  load(feature: string): Promise<void> {
    const language = this.lang();
    const cacheKey = `${feature}.${language}`;
    if (cacheKey in this.dictionaries()) {
      return Promise.resolve();
    }

    const inFlight = this.loading.get(cacheKey);
    if (inFlight) {
      return inFlight;
    }

    const request = fetch(`/i18n/${feature}.${language}.json`, { credentials: 'same-origin' })
      .then(async (response) => (response.ok ? ((await response.json()) as Dictionary) : {}))
      .catch((): Dictionary => ({}))
      .then((dictionary) => {
        this.dictionaries.update((all) => ({ ...all, [cacheKey]: dictionary }));
        this.loading.delete(cacheKey);
      });
    this.loading.set(cacheKey, request);
    return request;
  }

  /** `t('auth.signIn')` → the English or Arabic text for the current language. */
  t(key: string): string {
    const dot = key.indexOf('.');
    if (dot <= 0) {
      return key;
    }

    const feature = key.slice(0, dot);
    const entry = key.slice(dot + 1);
    return this.dictionaries()[`${feature}.${this.lang()}`]?.[entry] ?? key;
  }

  /** True when the key has a translation in the loaded dictionary (so a caller can fall back to another key). */
  has(key: string): boolean {
    return this.t(key) !== key;
  }

  /** `tf('shared.paginator.showing', { from: 1, to: 50, total: 120 })` → the text with `{name}` placeholders filled. */
  tf(key: string, params: Readonly<Record<string, string | number>>): string {
    return this.t(key).replace(/\{(\w+)\}/g, (match, name: string) => {
      const value = params[name];
      return value === undefined ? match : String(value);
    });
  }
}
