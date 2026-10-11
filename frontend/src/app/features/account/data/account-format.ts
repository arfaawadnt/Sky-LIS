/**
 * Formatting helpers of the account pages: local days for the dashboard query, and dates, times and money in the
 * user's language (Arabic uses Arabic-Indic digits through `ar-EG`). Pure functions; the pages pass `i18n.lang()`.
 */

/** The browser's local day as `yyyy-MM-dd`. */
export function localDay(date: Date = new Date()): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

/** Minutes east of UTC for the given instant (Cairo summer time → 180). */
export function utcOffsetMinutes(date: Date = new Date()): number {
  return -date.getTimezoneOffset();
}

function locale(lang: string): string {
  return lang === 'ar' ? 'ar-EG' : 'en-GB';
}

export function formatDateTime(value: string | null | undefined, lang: string): string {
  if (!value) {
    return '—';
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : new Intl.DateTimeFormat(locale(lang), { dateStyle: 'medium', timeStyle: 'short' }).format(date);
}

export function formatDate(value: string | null | undefined, lang: string): string {
  if (!value) {
    return '—';
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : new Intl.DateTimeFormat(locale(lang), { dateStyle: 'medium' }).format(date);
}

export function formatTime(value: string | null | undefined, lang: string): string {
  if (!value) {
    return '—';
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : new Intl.DateTimeFormat(locale(lang), { timeStyle: 'short' }).format(date);
}

/** A `yyyy-MM-dd` local day in the user's language and digits (the dashboard's day; never shifted by the time zone). */
export function formatDay(day: string, lang: string): string {
  const [year, month, date] = day.split('-').map(Number);
  if (!year || !month || !date) {
    return day;
  }

  return new Intl.DateTimeFormat(locale(lang), { dateStyle: 'medium' }).format(new Date(year, month - 1, date));
}

/** `ResultVerified` → "Result verified", `ResultLogEntry` → "Result log entry": a readable fallback for a technical name. */
export function humanize(name: string): string {
  const words = name.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2').trim();
  return words.length === 0 ? name : words.charAt(0).toUpperCase() + words.slice(1).toLowerCase();
}

/** A short weekday label for a `yyyy-MM-dd` local day (chart axis). */
export function weekday(day: string, lang: string): string {
  const [year, month, date] = day.split('-').map(Number);
  if (!year || !month || !date) {
    return day;
  }

  return new Intl.DateTimeFormat(locale(lang), { weekday: 'short' }).format(new Date(year, month - 1, date));
}

export function formatMoney(amount: number | null | undefined, currency: string | null | undefined, lang: string): string {
  if (amount === null || amount === undefined) {
    return '—';
  }

  const code = currency && /^[A-Z]{3}$/.test(currency) ? currency : null;
  try {
    return code
      ? new Intl.NumberFormat(locale(lang), { style: 'currency', currency: code, maximumFractionDigits: 2 }).format(amount)
      : new Intl.NumberFormat(locale(lang), { maximumFractionDigits: 2 }).format(amount);
  } catch {
    return `${code ?? ''} ${amount.toFixed(2)}`.trim();
  }
}

export function formatNumber(value: number | null | undefined, lang: string): string {
  return value === null || value === undefined ? '—' : new Intl.NumberFormat(locale(lang)).format(value);
}

/** The localized half of a bilingual pair, falling back to the other half. */
export function pick(en: string | null | undefined, ar: string | null | undefined, lang: string): string {
  const preferred = lang === 'ar' ? ar : en;
  return preferred?.trim() ? preferred : (lang === 'ar' ? en : ar) ?? '';
}
