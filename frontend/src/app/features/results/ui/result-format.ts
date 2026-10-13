import type { DelayState, ResultFlag, ResultRowView, ResultStatus, ResultValueItem } from '../../../core/api/results.contracts';
import type { BadgeTone } from '../../../shared/ui/badge/status-tones';

/** The report symbol of a flag (LDM §5.3 colour key): H, L, HH, LL, A; nothing for a normal value. */
export function flagSymbol(flag: ResultFlag | null): string {
  switch (flag) {
    case 'High':
      return 'H';
    case 'Low':
      return 'L';
    case 'CriticalHigh':
      return 'HH';
    case 'CriticalLow':
      return 'LL';
    case 'Abnormal':
      return 'A';
    default:
      return '';
  }
}

export function flagTone(flag: ResultFlag | null): BadgeTone {
  switch (flag) {
    case 'CriticalHigh':
    case 'CriticalLow':
      return 'bad';
    case 'High':
    case 'Low':
    case 'Abnormal':
      return 'warn';
    default:
      return 'ok';
  }
}

/** The CSS class of a grid row for its flag (high pink, low orange, panic red; LDM §3.3). */
export function flagClass(flag: ResultFlag | null): string {
  switch (flag) {
    case 'CriticalHigh':
    case 'CriticalLow':
      return 'flag-panic';
    case 'High':
      return 'flag-high';
    case 'Low':
      return 'flag-low';
    case 'Abnormal':
      return 'flag-abnormal';
    default:
      return '';
  }
}

export function delayTone(state: DelayState): BadgeTone {
  switch (state) {
    case 'Late':
      return 'bad';
    case 'Warning':
      return 'warn';
    case 'Done':
      return 'ok';
    default:
      return 'info';
  }
}

/** The rank of a status on the track (BR-10 / BR-04). */
export function statusRank(status: ResultStatus): number {
  return ['Ordered', 'Complete', 'Reviewed', 'Verified', 'Printed'].indexOf(status);
}

export function isSigned(status: ResultStatus): boolean {
  return status === 'Verified' || status === 'Printed';
}

/** Units of a duration, already translated. */
export interface DurationUnits {
  readonly days: string;
  readonly hours: string;
  readonly minutes: string;
}

/**
 * A number on screen in the digits of the language: Arabic-Indic in Arabic (validation v0.5.0 F-09: ages, percentages and
 * counts stayed Latin beside Arabic-Indic dates). Never grouped, at most two decimals. Printed documents and exported
 * files keep Latin digits ({@link latinNumber}).
 */
export function screenNumber(value: number, lang: string): string {
  return new Intl.NumberFormat(lang === 'ar' ? 'ar-EG' : 'en-US', { maximumFractionDigits: 2, useGrouping: false }).format(value);
}

/** A number on a printed document or in an exported file: Latin digits in every language. */
export function latinNumber(value: number): string {
  return String(value);
}

/** `2d 12h 38m` style (LDM §6 "Delay"); empty below a minute. `digits` writes the numbers (Latin by default). */
export function formatDuration(seconds: number, units: DurationUnits, digits: (value: number) => string = latinNumber): string {
  if (seconds < 60) {
    return '';
  }

  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const parts: string[] = [];
  if (days > 0) {
    parts.push(`${digits(days)}${units.days}`);
  }

  if (hours > 0) {
    parts.push(`${digits(hours)}${units.hours}`);
  }

  if (minutes > 0 && days === 0) {
    parts.push(`${digits(minutes)}${units.minutes}`);
  }

  return parts.join(' ');
}

/**
 * Date and time on screen in the user's language and its digits (Arabic-Indic in Arabic), like the rest of the portal
 * (decision 2026-10-12 after validation R3-02). Printed documents and exported files use {@link formatPrintDateTime}.
 */
export function formatDateTime(iso: string | null | undefined, lang: string): string {
  return format(iso, lang === 'ar' ? 'ar-EG' : 'en-GB');
}

/** Date and time of the printed reports, worklists and exported files: the user's language with Latin digits in both. */
export function formatPrintDateTime(iso: string | null | undefined, lang: string): string {
  return format(iso, lang === 'ar' ? 'ar-EG-u-nu-latn' : 'en-GB');
}

function format(iso: string | null | undefined, locale: string): string {
  if (!iso) {
    return '—';
  }

  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return '—';
  }

  return new Intl.DateTimeFormat(locale, { dateStyle: 'short', timeStyle: 'short' }).format(date);
}

/** A local calendar day (`yyyy-MM-dd`) as the UTC instant of its start, for the API's date windows. */
export function localDayStart(day: string | null | undefined, addDays = 0): string | null {
  if (!day || !/^\d{4}-\d{2}-\d{2}$/.test(day)) {
    return null;
  }

  const [year, month, date] = day.split('-').map(Number);
  if (year === undefined || month === undefined || date === undefined) {
    return null;
  }

  return new Date(year, month - 1, date + addDays, 0, 0, 0, 0).toISOString();
}

/** Today in local time as `yyyy-MM-dd`. */
export function today(now: Date = new Date()): string {
  const pad = (value: number): string => String(value).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

/** The text a grid cell starts with: the stored value of typed tests (numbers without the reportable marker). */
export function initialDraft(row: ResultRowView): string {
  if (row.numericValue !== null && (row.testType === 'Numeric' || row.testType === 'Formula')) {
    return row.numericValue.toFixed(row.decimals);
  }

  return row.textValue ?? '';
}

/** True for the tests typed in the grid (formulas compute; culture and formatted text have their own dialogs). */
export function isTyped(row: ResultRowView): boolean {
  return row.testType === 'Numeric' || row.testType === 'NonNumeric' || row.testType === 'OptionalList' || row.testType === 'RestrictedList';
}

/** A grid draft as the save endpoint takes it: numbers for numeric tests (the server refuses what is not one), text otherwise. */
export function toValueItem(row: ResultRowView, draft: string): ResultValueItem {
  const text = draft.trim();
  if (row.testType === 'Numeric') {
    const number = /^[+-]?\d+(\.\d+)?$/.test(text) ? Number(text) : null;
    return { resultId: row.id, numericValue: number, textValue: number === null ? text : null, paragraphs: null, culture: null };
  }

  return { resultId: row.id, numericValue: null, textValue: text, paragraphs: null, culture: null };
}

/** `40y 3m` / `5m 12d` / `9d` from the age parts; `digits` writes the numbers (Latin by default, {@link screenNumber} on screen). */
export function ageText(
  years: number | null,
  months: number | null,
  days: number | null,
  units: { readonly y: string; readonly m: string; readonly d: string },
  digits: (value: number) => string = latinNumber,
): string {
  if (years === null && months === null && days === null) {
    return '—';
  }

  if ((years ?? 0) > 0) {
    return (months ?? 0) > 0 && (years ?? 0) < 5 ? `${digits(years ?? 0)}${units.y} ${digits(months ?? 0)}${units.m}` : `${digits(years ?? 0)}${units.y}`;
  }

  if ((months ?? 0) > 0) {
    return `${digits(months ?? 0)}${units.m} ${digits(days ?? 0)}${units.d}`;
  }

  return `${digits(days ?? 0)}${units.d}`;
}

/** The text of the reader's language, the other one when it is empty, `—` when both are. */
export function pickName(en: string | null | undefined, ar: string | null | undefined, lang: string): string {
  const preferred = lang === 'ar' ? ar : en;
  const other = lang === 'ar' ? en : ar;
  return (preferred && preferred.length > 0 ? preferred : other) ?? '—';
}

/** A patient line (`PAT-… · Name`) from a list item. */
/**
 * "number · name" of a row; a VIP name masked by the server (`patientMasked`, rule A-03) reads `maskedName` (the
 * dictionary's "VIP patient") when the caller passes it.
 */
export function patientLabel(
  item: { readonly patientNumber: string | null; readonly patientName: string | null; readonly patientNameAr: string | null; readonly patientMasked?: boolean },
  lang: string,
  maskedName?: string,
): string {
  const name = item.patientMasked === true && maskedName !== undefined ? maskedName : pickName(item.patientName, item.patientNameAr, lang);
  return item.patientNumber ? `${item.patientNumber} · ${name}` : name;
}

/** The name of a test on the grid and lists: the report name in English, the Arabic name in Arabic, the code last. */
export function testLabel(item: { readonly testCode: string | null; readonly testNameEn: string | null; readonly testNameAr: string | null; readonly reportName?: string | null }, lang: string): string {
  const en = item.reportName ?? item.testNameEn;
  const name = lang === 'ar' ? (item.testNameAr ?? en) : (en ?? item.testNameAr);
  return name ?? item.testCode ?? '—';
}

/** The CSS class of a worklist row for its BR-13 colour (LDM §6 legend). */
export function delayClass(state: DelayState): string {
  switch (state) {
    case 'Warning':
      return 'delay-warn';
    case 'Late':
      return 'delay-late';
    case 'Done':
      return 'delay-done';
    default:
      return '';
  }
}

/** A short age/sex line: `36y · F`; `digits` writes the age (Latin by default). */
export function ageSex(ageYears: number | null, gender: string | null, units: { readonly y: string }, genderLabel: (gender: string) => string, digits: (value: number) => string = latinNumber): string {
  const parts: string[] = [];
  if (ageYears !== null) {
    parts.push(`${digits(ageYears)}${units.y}`);
  }

  if (gender) {
    parts.push(genderLabel(gender));
  }

  return parts.join(' · ') || '—';
}

/** The sentence of a log row (value changes read "before → after"). */
export function logValueText(before: string | null, after: string | null): string {
  if (before === null && after === null) {
    return '';
  }

  return `${before ?? '—'} → ${after ?? '—'}`;
}
