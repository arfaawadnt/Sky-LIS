/**
 * Number and date presentation of the Billing screens and documents (validation v0.5.0 F-09). On screen an Arabic page
 * shows Arabic-Indic digits like the rest of the portal (`formatMoney`, `formatDateTime`); printed documents keep
 * Latin digits (`-u-nu-latn`, `formatPrintMoney`, `formatPrintDateTime`) so receipt, accession and reference numbers
 * read the same in both languages on paper.
 */

import type { PaymentMethod } from '../../../core/api/billing.contracts';
import { localizedPick } from '../../../shared/forms/localized-text';

const moneyFormats = new Map<string, Intl.NumberFormat>();
const dateFormats = new Map<string, Intl.DateTimeFormat>();

type Digits = 'screen' | 'print';

function locale(lang: string, digits: Digits): string {
  if (lang !== 'ar') {
    return 'en-GB';
  }

  return digits === 'print' ? 'ar-EG-u-nu-latn' : 'ar-EG';
}

function money(value: number | null | undefined, lang: string, digits: Digits): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '—';
  }

  const key = `${lang}.${digits}`;
  let format = moneyFormats.get(key);
  if (format === undefined) {
    format = new Intl.NumberFormat(locale(lang, digits), { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    moneyFormats.set(key, format);
  }

  return format.format(value);
}

function dateTime(iso: string | null | undefined, lang: string, digits: Digits): string {
  if (iso === null || iso === undefined || iso === '') {
    return '—';
  }

  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return '—';
  }

  const key = `${lang}.${digits}`;
  let format = dateFormats.get(key);
  if (format === undefined) {
    format = new Intl.DateTimeFormat(locale(lang, digits), { dateStyle: 'medium', timeStyle: 'short' });
    dateFormats.set(key, format);
  }

  return format.format(date);
}

/** On screen: `1234.5` → `1,234.50` / `١٬٢٣٤٫٥٠` (two decimals, intake Q15); empty values render as an em dash. */
export function formatMoney(value: number | null | undefined, lang: string): string {
  return money(value, lang, 'screen');
}

/** On a printed document: two decimals with Latin digits in both languages. */
export function formatPrintMoney(value: number | null | undefined, lang: string): string {
  return money(value, lang, 'print');
}

/** On screen: an instant of the API (ISO 8601, UTC) in the reader's local time and the language's digits. */
export function formatDateTime(iso: string | null | undefined, lang: string): string {
  return dateTime(iso, lang, 'screen');
}

/** On a printed document: the instant with Latin digits in both languages. */
export function formatPrintDateTime(iso: string | null | undefined, lang: string): string {
  return dateTime(iso, lang, 'print');
}

/** A whole count on screen in the language's digits (Arabic-Indic in Arabic). */
export function formatCount(value: number, lang: string): string {
  return new Intl.NumberFormat(lang === 'ar' ? 'ar-EG' : 'en-GB', { maximumFractionDigits: 0 }).format(value);
}

/** The value of an `<input type="datetime-local">` for a date (local time, minutes). */
export function toLocalInputValue(date: Date): string {
  const pad = (value: number): string => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/** A `datetime-local` value (local time) as an ISO instant for the API, or null when empty or invalid. */
export function localInputToIso(value: string | null | undefined): string | null {
  if (value === null || value === undefined || value.trim() === '') {
    return null;
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

/** Local midnight of the day of `now` (the default start of a closing period: the current shift's day). */
export function startOfDay(now: Date): Date {
  return new Date(now.getFullYear(), now.getMonth(), now.getDate(), 0, 0, 0, 0);
}

/** Rounds to cents the way the server checks amounts (two decimals). */
export function roundMoney(value: number): number {
  return Math.round(value * 100) / 100;
}

/** True when the value has at most two decimals (BIL-002). */
export function hasTwoDecimals(value: number): boolean {
  return Math.abs(roundMoney(value) - value) < 1e-9;
}

/** A movement's method as the lists and documents name it (change request 2026-10-19, CP-30b). */
export interface MovementMethod {
  readonly method: PaymentMethod;
  readonly methodCode?: string | null;
  readonly methodNameEn?: string | null;
  readonly methodNameAr?: string | null;
}

/**
 * The method's name: the laboratory's definition (localized) when the movement carries a code the definition still
 * answers for, otherwise the kind (`billing.method.<Kind>`: legacy rows, or a definition deleted from the database).
 */
export function movementMethodName(t: (key: string) => string, lang: string, movement: MovementMethod): string {
  const en = movement.methodNameEn ?? '';
  const ar = movement.methodNameAr ?? '';
  if (en === '' && ar === '') {
    return t(`billing.method.${movement.method}`);
  }

  return localizedPick({ en, ar }, lang);
}

/** The name followed by the code when the movement carries one (`Wallet · WALLET`), for documents and detail rows. */
export function movementMethodLabel(t: (key: string) => string, lang: string, movement: MovementMethod): string {
  const name = movementMethodName(t, lang, movement);
  const code = movement.methodCode ?? '';
  return code === '' ? name : `${name} · ${code}`;
}
