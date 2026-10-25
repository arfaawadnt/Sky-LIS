import type { ReportLine, ResultFlag } from '../../../../../core/api/results.contracts';

/**
 * Pure helpers of the five report designs (LIS-CR-010, C-14): the range bar of the Modern design, the tone of a flag
 * bar, the date of a previous value (Cumulative) and the columns of a line. Numbers and dates keep Latin digits on paper.
 */

/** The bar of a numeric result against its reference range: the normal zone spans 25 %–75 % of the track. */
export interface RangeBar {
  /** The marker position on the track, in percent from the inline start (clamped to 2–98). */
  readonly marker: number;
  /** Where the value sits: below, inside or above the normal zone. */
  readonly position: 'below' | 'inside' | 'above';
}

const TWO_SIDED = /^\s*(-?\d+(?:[.,]\d+)?)\s*[–—-]\s*(-?\d+(?:[.,]\d+)?)\s*$/u;
const UPPER_ONLY = /^\s*[<≤]\s*=?\s*(\d+(?:[.,]\d+)?)\s*$/u;
const NUMBER = /^\s*-?\d+(?:[.,]\d+)?\s*$/u;

function parseNumber(text: string): number {
  return Number(text.trim().replace(',', '.'));
}

/** The low and high limits of a printed range (`3.5 – 5.0`, `< 200`), or null when the range is not numeric. */
export function rangeLimits(range: string | null | undefined): { readonly low: number; readonly high: number } | null {
  if (!range) {
    return null;
  }

  const twoSided = TWO_SIDED.exec(range);
  if (twoSided?.[1] !== undefined && twoSided[2] !== undefined) {
    const low = parseNumber(twoSided[1]);
    const high = parseNumber(twoSided[2]);
    return Number.isFinite(low) && Number.isFinite(high) && high > low ? { low, high } : null;
  }

  const upper = UPPER_ONLY.exec(range);
  if (upper?.[1] !== undefined) {
    const high = parseNumber(upper[1]);
    return Number.isFinite(high) && high > 0 ? { low: 0, high } : null;
  }

  return null;
}

/** The range bar of a line, or null when its value or its range is not numeric. */
export function rangeBar(value: string | null | undefined, range: string | null | undefined): RangeBar | null {
  const limits = rangeLimits(range);
  if (limits === null || !value || !NUMBER.test(value)) {
    return null;
  }

  const number = parseNumber(value);
  const raw = 25 + ((number - limits.low) / (limits.high - limits.low)) * 50;
  const marker = Math.round(Math.min(98, Math.max(2, raw)) * 10) / 10;
  const position = number < limits.low ? 'below' : number > limits.high ? 'above' : 'inside';
  return { marker, position };
}

/** The colour family of a flag bar: normal, out of range, critical (a panic value is always critical). */
export type FlagTone = 'normal' | 'abnormal' | 'critical' | 'none';

export function flagToneOf(flag: ResultFlag | null, isPanic = false): FlagTone {
  if (isPanic || flag === 'CriticalHigh' || flag === 'CriticalLow') {
    return 'critical';
  }

  if (flag === 'High' || flag === 'Low' || flag === 'Abnormal') {
    return 'abnormal';
  }

  return flag === 'Normal' ? 'normal' : 'none';
}

/** A culture or a formatted text: printed across the table instead of in the value columns. */
export function isRichLine(line: ReportLine): boolean {
  return (line.testType === 'Culture' && line.culture !== null) || (line.testType === 'FormattedText' && line.paragraphs !== null);
}

/** The calendar day of a previous value on paper (`10/10/2026`), Latin digits in both languages. */
export function printDay(iso: string | null | undefined, lang: string): string {
  if (!iso) {
    return '—';
  }

  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? '—' : new Intl.DateTimeFormat(lang === 'ar' ? 'ar-EG-u-nu-latn' : 'en-GB', { dateStyle: 'short' }).format(date);
}

/** The Cumulative design prints three previous-value columns; a line with fewer values leaves the rest empty. */
export const CUMULATIVE_COLUMNS = 3;

export function previousColumns<T>(values: readonly T[] | null | undefined): readonly (T | null)[] {
  const list = values ?? [];
  return Array.from({ length: CUMULATIVE_COLUMNS }, (_, index) => list[index] ?? null);
}
