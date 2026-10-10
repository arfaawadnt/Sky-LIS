import type { Language } from '../../../core/i18n/i18n.service';

/** Display formats of the platform pages; Arabic uses Arabic-Egyptian conventions, numbers stay Latin digits for codes and money columns. */
function locale(lang: Language): string {
  return lang === 'ar' ? 'ar-EG-u-nu-latn' : 'en-GB';
}

export function formatMoney(amount: number | null | undefined, currency: string | null | undefined, lang: Language): string {
  if (amount === null || amount === undefined) {
    return '—';
  }

  const value = new Intl.NumberFormat(locale(lang), { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount);
  return currency ? `${value} ${currency}` : value;
}

export function formatNumber(value: number | null | undefined, lang: Language): string {
  return value === null || value === undefined ? '—' : new Intl.NumberFormat(locale(lang)).format(value);
}

export function formatDate(iso: string | null | undefined, lang: Language): string {
  if (!iso) {
    return '—';
  }

  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? '—' : new Intl.DateTimeFormat(locale(lang), { year: 'numeric', month: '2-digit', day: '2-digit' }).format(date);
}

export function formatDateTime(iso: string | null | undefined, lang: Language): string {
  if (!iso) {
    return '—';
  }

  const date = new Date(iso);
  return Number.isNaN(date.getTime())
    ? '—'
    : new Intl.DateTimeFormat(locale(lang), { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' }).format(date);
}

/** `yyyy-MM-dd` for file names. */
export function fileStamp(date: Date = new Date()): string {
  return date.toISOString().slice(0, 10);
}

/** The text of the current language with a fallback to the other one. */
export function pick(en: string | null | undefined, ar: string | null | undefined, lang: Language): string {
  const preferred = lang === 'ar' ? ar : en;
  const other = lang === 'ar' ? en : ar;
  return preferred && preferred.length > 0 ? preferred : (other ?? '');
}
