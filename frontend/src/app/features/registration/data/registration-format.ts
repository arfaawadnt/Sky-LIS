import type { Language } from '../../../core/i18n/i18n.service';

/** Pure helpers of the Registration pages: money, dates in the desk's local time, local-day ranges, search classification, discount arithmetic. */

/** Amounts with two decimals in the page language's digits. */
export function money(value: number | null | undefined, lang: Language = 'en'): string {
  const amount = typeof value === 'number' && Number.isFinite(value) ? value : 0;
  return new Intl.NumberFormat(lang === 'ar' ? 'ar-EG' : 'en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount);
}

export function round2(value: number): number {
  return Math.round((value + Number.EPSILON) * 100) / 100;
}

/** A UTC instant as local date and time (browser time zone), `—` when absent. */
export function dateTime(utc: string | null | undefined, lang: Language = 'en'): string {
  if (!utc) {
    return '—';
  }

  const date = new Date(utc);
  return Number.isNaN(date.getTime())
    ? '—'
    : new Intl.DateTimeFormat(lang === 'ar' ? 'ar-EG' : 'en-GB', { dateStyle: 'short', timeStyle: 'short' }).format(date);
}

export function dateOnly(utc: string | null | undefined, lang: Language = 'en'): string {
  if (!utc) {
    return '—';
  }

  const date = new Date(utc);
  return Number.isNaN(date.getTime()) ? '—' : new Intl.DateTimeFormat(lang === 'ar' ? 'ar-EG' : 'en-GB', { dateStyle: 'medium' }).format(date);
}

/** The browser's offset from UTC in minutes, east positive (what the API calls `utcOffsetMinutes`). */
export function utcOffsetMinutes(at: Date = new Date()): number {
  return -at.getTimezoneOffset();
}

/** `yyyy-MM-dd` of a local date. */
export function isoDay(date: Date): string {
  const pad = (value: number): string => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** The UTC instant of the local midnight that starts `day` (`yyyy-MM-dd`), or null when the text is not a date. */
export function localDayStartUtc(day: string | null): string | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(day ?? '');
  if (!match) {
    return null;
  }

  const date = new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]));
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

/** The exclusive end of a local day: the next local midnight. */
export function localDayEndUtc(day: string | null): string | null {
  const start = localDayStartUtc(day);
  if (start === null) {
    return null;
  }

  const date = new Date(start);
  date.setDate(date.getDate() + 1);
  return date.toISOString();
}

export type SearchTarget = 'accession' | 'patientNo' | 'mobile' | 'name';

/**
 * What a single search box text most likely is (CP-10): an accession (`ACC-…` or a short `nnnn-nnnnn`), a patient number
 * (`PAT-…`), a mobile (8 or more digits) or a name.
 */
export function classifySearch(text: string): { readonly target: SearchTarget; readonly value: string } | null {
  const value = text.trim();
  if (value === '') {
    return null;
  }

  const upper = value.toUpperCase();
  if (upper.startsWith('ACC') || /^\d{4}-\d{1,6}$/.test(value)) {
    return { target: 'accession', value: upper };
  }

  if (upper.startsWith('PAT')) {
    return { target: 'patientNo', value: upper };
  }

  const digits = value.replace(/[\s()+-]/g, '');
  if (/^[0-9٠-٩]{8,15}$/.test(digits)) {
    return { target: 'mobile', value: digits };
  }

  if (/^\d{1,7}$/.test(value)) {
    return { target: 'accession', value };
  }

  return { target: 'name', value };
}

/** The discount amount the server would compute (intake §5): percent of the base, or the amount capped at the base. */
export function discountAmount(kind: 'Amount' | 'Percent', value: number, base: number): number {
  if (!(base > 0) || !(value > 0)) {
    return 0;
  }

  const amount = kind === 'Percent' ? round2((base * Math.min(value, 100)) / 100) : round2(value);
  return Math.min(amount, base);
}

/** The percent of the base an amount represents (the cap check). */
export function discountPercent(amount: number, base: number): number {
  return base > 0 ? (amount * 100) / base : 0;
}

/** Picks the name in the page language with a fallback to the other one. */
export function pick(en: string | null | undefined, ar: string | null | undefined, lang: Language): string {
  const first = lang === 'ar' ? ar : en;
  const second = lang === 'ar' ? en : ar;
  return (first ?? '').trim() !== '' ? (first ?? '') : (second ?? '');
}
