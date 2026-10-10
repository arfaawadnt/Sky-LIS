import type { SampleListItem } from '../../../core/api/samples.contracts';

/** The name in the current language, falling back to the other language, then to `fallback` (an id or a dash). */
export function pickName(en: string | null | undefined, ar: string | null | undefined, lang: string, fallback = ''): string {
  const preferred = lang === 'ar' ? ar : en;
  const other = lang === 'ar' ? en : ar;
  if (preferred && preferred.trim().length > 0) {
    return preferred;
  }

  return other && other.trim().length > 0 ? other : fallback;
}

/** Patient label of a row: number and name in the current language, whichever are known. */
export function patientLabel(row: Pick<SampleListItem, 'patientNumber' | 'patientName' | 'patientNameAr'>, lang: string): string {
  const name = pickName(row.patientName, row.patientNameAr, lang);
  return [row.patientNumber, name].filter((part): part is string => !!part && part.length > 0).join(' · ');
}

/** Sample type with its condition ("Serum · Fasting"); ids are shortened when the catalogue names are not available. */
export function sampleTypeLabel(
  row: Pick<SampleListItem, 'sampleTypeId' | 'sampleTypeNameEn' | 'sampleTypeNameAr' | 'sampleConditionId' | 'sampleConditionNameEn' | 'sampleConditionNameAr'>,
  lang: string,
): string {
  const type = pickName(row.sampleTypeNameEn, row.sampleTypeNameAr, lang, shortId(row.sampleTypeId));
  const condition = row.sampleConditionId === null ? '' : pickName(row.sampleConditionNameEn, row.sampleConditionNameAr, lang, shortId(row.sampleConditionId));
  return condition.length > 0 ? `${type} · ${condition}` : type;
}

/** Unit letter and group name ("C · Chemistry"). */
export function unitLabel(row: Pick<SampleListItem, 'unitCode' | 'clinicalGroupId' | 'clinicalGroupNameEn' | 'clinicalGroupNameAr'>, lang: string): string {
  const name = pickName(row.clinicalGroupNameEn, row.clinicalGroupNameAr, lang);
  if (row.unitCode && name) {
    return `${row.unitCode} · ${name}`;
  }

  return row.unitCode ?? (name || shortId(row.clinicalGroupId));
}

export function shortId(id: string): string {
  return id.length > 8 ? `${id.slice(0, 8)}…` : id;
}

/** Date and time in the user's language (Gregorian calendar, Latin digits for barcodes and logs). */
export function formatDateTime(iso: string | null | undefined, lang: string): string {
  if (!iso) {
    return '—';
  }

  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return '—';
  }

  return new Intl.DateTimeFormat(lang === 'ar' ? 'ar-EG-u-nu-latn' : 'en-GB', { dateStyle: 'short', timeStyle: 'short' }).format(date);
}

/** Units used by {@link formatDuration}, already translated (`d`, `h`, `min`, `s`). */
export interface DurationUnits {
  readonly days: string;
  readonly hours: string;
  readonly minutes: string;
  readonly seconds: string;
}

/** "2d 3h 15min" from seconds (the LDM delay notation, CP-17 "time between stages"); `—` when unknown. */
export function formatDuration(seconds: number | null | undefined, units: DurationUnits): string {
  if (seconds === null || seconds === undefined || seconds < 0) {
    return '—';
  }

  if (seconds < 60) {
    return `${Math.floor(seconds)}${units.seconds}`;
  }

  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const parts: string[] = [];
  if (days > 0) {
    parts.push(`${days}${units.days}`);
  }

  if (hours > 0) {
    parts.push(`${hours}${units.hours}`);
  }

  if (minutes > 0 || parts.length === 0) {
    parts.push(`${minutes}${units.minutes}`);
  }

  return parts.join(' ');
}

/** The caller's local day as ISO instants, for "today's list" (intake §16 Q8). */
export function todayWindow(now: Date = new Date()): { readonly from: string; readonly to: string } {
  const start = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const end = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1);
  return { from: start.toISOString(), to: end.toISOString() };
}

/** Minutes east of UTC of the caller's clock (the server's `utcOffsetMinutes`). */
export function utcOffsetMinutes(now: Date = new Date()): number {
  return -now.getTimezoneOffset();
}

/** The barcode as stored: trimmed and upper-case, so a scanner in lower-case mode still matches. */
export function normalizeSampleNumber(value: string): string {
  return value.trim().toUpperCase();
}
