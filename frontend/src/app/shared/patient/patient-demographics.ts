import type { PatientAge, PatientDetail, PatientGender, PatientListItem, PatientSummary, PatientTitle, SimilarPatient } from '../../core/api/patients.contracts';

/**
 * Pure helpers of the patient form and picker, mirroring the server's rules so the screen agrees with the aggregate:
 * the age ↔ date-of-birth calendar rule (`PatientAge.Between`), the title's default gender, mobile normalisation.
 * Moves to `shared/` together with the patient picker.
 */

const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;

/** Mobile numbers: optional `+` and 8–15 digits after removing spaces, dashes, dots, slashes and parentheses. */
export const MOBILE_MIN_DIGITS = 8;
export const MOBILE_MAX_DIGITS = 15;
export const MAX_AGE_YEARS = 150;

/** UTC calendar date (the server computes ages for the UTC date of its clock). */
export function todayIso(now: Date = new Date()): string {
  return toIso(now.getUTCFullYear(), now.getUTCMonth() + 1, now.getUTCDate());
}

export function toIso(year: number, month: number, day: number): string {
  return `${String(year).padStart(4, '0')}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}

interface CalendarDate {
  readonly year: number;
  readonly month: number;
  readonly day: number;
}

export function parseIso(value: string | null | undefined): CalendarDate | null {
  const match = ISO_DATE.exec(value ?? '');
  if (!match) {
    return null;
  }

  const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])];
  if (month < 1 || month > 12 || day < 1 || day > daysInMonth(year, month)) {
    return null;
  }

  return { year, month, day };
}

export function daysInMonth(year: number, month: number): number {
  return new Date(Date.UTC(year, month, 0)).getUTCDate();
}

/** `date + months`, clamped to the last day of the target month (DateOnly.AddMonths). */
function addMonths(date: CalendarDate, months: number): CalendarDate {
  const index = date.year * 12 + (date.month - 1) + months;
  const year = Math.floor(index / 12);
  const month = (index % 12) + 1;
  return { year, month, day: Math.min(date.day, daysInMonth(year, month)) };
}

function dayNumber(date: CalendarDate): number {
  return Math.round(Date.UTC(date.year, date.month - 1, date.day) / 86_400_000);
}

function compare(left: CalendarDate, right: CalendarDate): number {
  return dayNumber(left) - dayNumber(right);
}

/** The age on `on` of someone born on `birth` (whole months while the clamped monthly anniversary is not later; the rest are days). */
export function ageBetween(birthIso: string, onIso: string): PatientAge {
  const birth = parseIso(birthIso);
  const on = parseIso(onIso);
  if (!birth || !on || compare(birth, on) >= 0) {
    return { years: 0, months: 0, days: 0 };
  }

  let totalMonths = (on.year - birth.year) * 12 + on.month - birth.month;
  if (compare(addMonths(birth, totalMonths), on) > 0) {
    totalMonths--;
  }

  const anniversary = addMonths(birth, totalMonths);
  return { years: Math.floor(totalMonths / 12), months: totalMonths % 12, days: dayNumber(on) - dayNumber(anniversary) };
}

/** The date of birth implied by an age stated on `onIso` (DateOnly.AddYears/AddMonths/AddDays). */
export function birthDateFromAge(age: PatientAge, onIso: string): string | null {
  const on = parseIso(onIso);
  if (!on || !isValidAge(age)) {
    return null;
  }

  const shifted = addMonths(addMonths(on, -age.years * 12), -age.months);   // years first, then months: each step clamps like DateOnly
  const date = new Date(Date.UTC(shifted.year, shifted.month - 1, shifted.day - age.days));
  return toIso(date.getUTCFullYear(), date.getUTCMonth() + 1, date.getUTCDate());
}

export function isValidAge(age: PatientAge): boolean {
  const whole = (value: number, max: number): boolean => Number.isInteger(value) && value >= 0 && value <= max;
  return whole(age.years, MAX_AGE_YEARS) && whole(age.months, 11) && whole(age.days, 31);
}

/** Mr/Master → Male; Mrs/Miss/Ms → Female; Dr/Prof/Baby → no default (the title proposes, the user decides). */
export function defaultGender(title: PatientTitle): PatientGender | null {
  switch (title) {
    case 'Mr':
    case 'Master':
      return 'Male';
    case 'Mrs':
    case 'Miss':
    case 'Ms':
      return 'Female';
    default:
      return null;
  }
}

/** Arabic-Indic and Extended Arabic-Indic digits → ASCII. */
export function foldDigits(value: string): string {
  return value.replace(/[\u0660-\u0669]/g, (digit) => String(digit.charCodeAt(0) - 0x0660)).replace(/[\u06F0-\u06F9]/g, (digit) => String(digit.charCodeAt(0) - 0x06f0));
}

/** The mobile as the server stores it: separators removed, digits folded; empty input → empty string. */
export function normalizeMobile(value: string | null | undefined): string {
  return foldDigits((value ?? '').trim()).replace(/[\s()./-]/g, '');
}

/**
 * True when the mobile is acceptable: optional `+` and 8–15 digits; with a local `countryLength` (from the platform
 * country list) a number without `+` must have exactly that many digits.
 */
export function isValidMobile(value: string, countryLength: number | null = null): boolean {
  const normalized = normalizeMobile(value);
  const international = normalized.startsWith('+');
  const digits = international ? normalized.slice(1) : normalized;
  if (!/^\d+$/.test(digits)) {
    return false;
  }

  if (!international && countryLength !== null && countryLength > 0) {
    return digits.length === countryLength;
  }

  return digits.length >= MOBILE_MIN_DIGITS && digits.length <= MOBILE_MAX_DIGITS;
}

/** "36 y 5 m" style text from the dictionary (`<prefix>.age.years|months|days`), largest units first, at most two. */
export function ageParts(age: PatientAge): readonly { readonly unit: 'years' | 'months' | 'days'; readonly value: number }[] {
  const parts: { unit: 'years' | 'months' | 'days'; value: number }[] = [];
  if (age.years > 0) {
    parts.push({ unit: 'years', value: age.years });
  }

  if (age.months > 0) {
    parts.push({ unit: 'months', value: age.months });
  }

  if (age.days > 0 || parts.length === 0) {
    parts.push({ unit: 'days', value: age.days });
  }

  return parts.slice(0, 2);
}

/** A whole number in the digits of the language: Arabic-Indic in Arabic (validation R-02), never grouped. */
export function localDigits(value: number, language: string): string {
  return new Intl.NumberFormat(language === 'ar' ? 'ar-EG' : 'en-US', { maximumFractionDigits: 0, useGrouping: false }).format(value);
}

/**
 * The age text of the patient screens ("52 y", "٥٢ سنة"): {@link ageParts} through the `patients.age.<unit>` dictionary
 * entries, with the numbers in the language's digits (validation R-02: Arabic pages showed "52 سنة").
 */
export function ageText(
  age: PatientAge,
  language: string,
  translate: (key: string, params: Readonly<Record<string, string | number>>) => string,
): string {
  return ageParts(age)
    .map((part) => translate(`patients.age.${part.unit}`, { value: localDigits(part.value, language) }))
    .join(' ');
}

/** The picker's output from any projection of a file. */
export function toSummary(source: PatientListItem | SimilarPatient | PatientDetail, isVip: boolean | null = 'isVip' in source ? source.isVip : null): PatientSummary {
  return {
    id: source.id,
    number: source.number,
    title: source.title,
    fullName: source.fullName,
    nameAr: source.nameAr,
    gender: source.gender,
    dateOfBirth: source.dateOfBirth,
    dateOfBirthIsEstimated: source.dateOfBirthIsEstimated,
    age: source.age,
    mobile: source.mobile,
    nationalId: source.nationalId,
    isVip,
  };
}

/** A human file size: bytes, KB or MB with one decimal. */
export function fileSize(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`;
  }

  return bytes < 1024 * 1024 ? `${(bytes / 1024).toFixed(1)} KB` : `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/**
 * A calendar date (`yyyy-MM-dd`, a date of birth or a registration day) in the user's language and digits, never shifted
 * by the time zone: `12 Oct 1979` / `١٢‏/١٠‏/١٩٧٩` (round 2 R2-02: the patient file showed the ISO text in Arabic). Anything
 * else is returned as given.
 */
export function formatCalendarDate(iso: string, language: string): string {
  const date = parseIso(iso);
  if (date === null) {
    return iso;
  }

  const local = new Date(2000, 0, 1);
  local.setFullYear(date.year, date.month - 1, date.day);
  return new Intl.DateTimeFormat(language === 'ar' ? 'ar-EG' : 'en-GB', { dateStyle: 'medium' }).format(local);
}

/** Date and time in the user's language without Angular locale data (Intl): `en-GB` / `ar-EG`, medium date, short time. */
export function formatDateTime(iso: string, language: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return iso;
  }

  return new Intl.DateTimeFormat(language === 'ar' ? 'ar-EG' : 'en-GB', { dateStyle: 'medium', timeStyle: 'short' }).format(date);
}
