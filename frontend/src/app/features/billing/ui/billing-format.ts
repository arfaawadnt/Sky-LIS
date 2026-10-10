/**
 * Number and date presentation of the Billing screens and documents. Arabic uses Latin digits (`-u-nu-latn`) so
 * receipt, accession and reference numbers read the same in both languages on paper.
 */

const moneyFormats = new Map<string, Intl.NumberFormat>();
const dateFormats = new Map<string, Intl.DateTimeFormat>();

function locale(lang: string): string {
  return lang === 'ar' ? 'ar-EG-u-nu-latn' : 'en-GB';
}

/** `1234.5` → `1,234.50` (two decimals, intake Q15); empty values render as an em dash. */
export function formatMoney(value: number | null | undefined, lang: string): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '—';
  }

  let format = moneyFormats.get(lang);
  if (format === undefined) {
    format = new Intl.NumberFormat(locale(lang), { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    moneyFormats.set(lang, format);
  }

  return format.format(value);
}

/** An instant of the API (ISO 8601, UTC) in the reader's local time. */
export function formatDateTime(iso: string | null | undefined, lang: string): string {
  if (iso === null || iso === undefined || iso === '') {
    return '—';
  }

  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return '—';
  }

  let format = dateFormats.get(lang);
  if (format === undefined) {
    format = new Intl.DateTimeFormat(locale(lang), { dateStyle: 'medium', timeStyle: 'short' });
    dateFormats.set(lang, format);
  }

  return format.format(date);
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
