import type { WorkAmountRow } from '../../../core/api/reporting.contracts';

/**
 * Number and date presentation of the Reporting screens and documents (validation v0.5.0 F-09): on screen an Arabic page
 * shows Arabic-Indic digits; printed documents and exported files keep Latin digits in both languages.
 */

type Digits = 'screen' | 'print';

const formats = new Map<string, Intl.NumberFormat>();

function locale(lang: string, digits: Digits): string {
  if (lang !== 'ar') {
    return 'en-GB';
  }

  return digits === 'print' ? 'ar-EG-u-nu-latn' : 'ar-EG';
}

function format(value: number, lang: string, digits: Digits, fraction: number): string {
  const key = `${lang}.${digits}.${fraction}`;
  let found = formats.get(key);
  if (found === undefined) {
    found = new Intl.NumberFormat(locale(lang, digits), { minimumFractionDigits: fraction, maximumFractionDigits: fraction });
    formats.set(key, found);
  }

  return found.format(value);
}

/** An amount with two decimals: `1,234.50` / `١٬٢٣٤٫٥٠` on screen. */
export function formatMoney(value: number, lang: string): string {
  return format(value, lang, 'screen', 2);
}

export function formatPrintMoney(value: number, lang: string): string {
  return format(value, lang, 'print', 2);
}

export function formatCount(value: number, lang: string): string {
  return format(value, lang, 'screen', 0);
}

export function formatPrintCount(value: number, lang: string): string {
  return format(value, lang, 'print', 0);
}

/** An ISO day as the language shows it (`13/10/2026`); Latin digits on paper. */
export function formatDay(day: string, lang: string, digits: Digits = 'screen'): string {
  const [year, month, date] = day.split('-').map(Number);
  if (year === undefined || month === undefined || date === undefined || [year, month, date].some(Number.isNaN)) {
    return day;
  }

  return new Intl.DateTimeFormat(locale(lang, digits), { dateStyle: 'medium' }).format(new Date(year, month - 1, date));
}

/** The caption of a row in the page language, with the code when the group has one. */
export function rowLabel(row: WorkAmountRow, lang: string): string {
  const preferred = lang === 'ar' ? row.labelAr : row.labelEn;
  const other = lang === 'ar' ? row.labelEn : row.labelAr;
  const label = preferred.length > 0 ? preferred : other;
  return row.code ? `${row.code} · ${label}` : label;
}
