/**
 * The pure rules of `app-mobile-input` (change request LIS-CR-007, reviewer point C-05): a mobile is typed as a dial code
 * plus a national number, the trunk 0 of the national part is dropped, and the forms people paste (`+20 101…`,
 * `0020101…`, `0101…`) are recognised. The control's value is the E.164 string (`+201004439855`) or `''`; the server keeps
 * its own rules (GEO-010/014, `IUserMobileNumbers`, PAT-008), so nothing here is a gate.
 */

/** A country as the mobile control needs it; the public countries list (`GET /api/v1/public/countries`) has every field. */
export interface MobileDialCountry {
  /** `+20` */
  readonly dialCode: string;
  /** Digits of a national mobile without the trunk 0 (Egypt 10); `null` when the platform does not publish it. */
  readonly mobileLength: number | null;
  /** `EG`: the option's key and its short label. */
  readonly isoCode?: string | null;
  readonly nameEn?: string | null;
  readonly nameAr?: string | null;
}

/** The two parts the control shows. `national` holds `+…` when an international number names no known dial code. */
export interface MobileParts {
  readonly dial: string;
  readonly national: string;
}

const SEPARATORS = /[\s()./-]/g;

/** Arabic-Indic and Extended Arabic-Indic digits → ASCII. */
function foldDigits(value: string): string {
  return value.replace(/[٠-٩]/g, (digit) => String(digit.charCodeAt(0) - 0x0660)).replace(/[۰-۹]/g, (digit) => String(digit.charCodeAt(0) - 0x06f0));
}

/** The digits of a dial code (`+20` → `20`). */
export function dialDigits(dial: string): string {
  return foldDigits(dial).replace(/\D/g, '');
}

/** A dial code as the server accepts it: `+` and one to four digits, not starting with 0 (GEO-002). */
export function isDialCode(dial: string): boolean {
  return /^\+?[1-9]\d{0,3}$/.test(foldDigits(dial).replace(/\s/g, ''));
}

/** What was typed, with Arabic digits folded and the separators people type (spaces, dashes, dots, brackets) removed. */
export function cleanMobileText(text: string): string {
  return foldDigits(text).replace(SEPARATORS, '');
}

/** The option key of a country: its ISO code, else its dial code. */
export function mobileCountryKey(country: MobileDialCountry): string {
  return country.isoCode?.trim() || country.dialCode;
}

/** The country whose dial code starts `digits` (the longest one wins: `+1` and `+1868` are different countries). */
export function countryOfInternational(digits: string, countries: readonly MobileDialCountry[]): MobileDialCountry | null {
  let best: MobileDialCountry | null = null;
  let bestLength = 0;
  for (const country of countries) {
    const dial = dialDigits(country.dialCode);
    if (dial.length > bestLength && digits.startsWith(dial)) {
      best = country;
      bestLength = dial.length;
    }
  }

  return best;
}

/** The digits after `+` or `00`, or `null` when the text is not written in an international form. */
function internationalDigits(clean: string): string | null {
  if (clean.startsWith('+')) {
    return clean.slice(1);
  }

  return clean.startsWith('00') ? clean.slice(2) : null;
}

/**
 * Splits what was typed or pasted (or a stored value) into the two parts: an international form takes its dial code from
 * `fixed` (when it starts with it) or from the list, and keeps `+…` in the national part when no dial code matches; a
 * local form keeps `currentDial`. The trunk 0 is dropped whenever a dial code is known.
 */
export function splitMobile(text: string, countries: readonly MobileDialCountry[], fixed: MobileDialCountry | null, currentDial: string): MobileParts {
  const clean = cleanMobileText(text);
  const international = internationalDigits(clean);
  if (international === null || !/^\d*$/.test(international)) {
    return { dial: currentDial, national: dialDigits(currentDial) === '' ? clean : clean.replace(/^0+(?=\d)/, '') };
  }

  const candidates = fixed === null ? countries : [fixed];
  const country = countryOfInternational(international, candidates);
  if (country === null) {
    return { dial: currentDial, national: international === '' ? '' : `+${international}` };
  }

  return { dial: country.dialCode, national: international.slice(dialDigits(country.dialCode).length).replace(/^0+/, '') };
}

/**
 * The control's value for the two parts: `''` when nothing was typed, the E.164 string otherwise. Text that is not a
 * number (letters) is passed on as it is, so the form's validator refuses it; without a dial code the digits are passed
 * on as typed (the patient form's stored local form).
 */
export function composeMobile(parts: MobileParts): string {
  const national = cleanMobileText(parts.national);
  if (national === '' || national === '+') {
    return '';
  }

  if (!/^\+?\d+$/.test(national)) {
    return national;
  }

  if (national.startsWith('+')) {
    return national;
  }

  const dial = dialDigits(parts.dial);
  if (dial === '') {
    return national;
  }

  const digits = national.replace(/^0+/, '');
  return digits === '' ? '' : `+${dial}${digits}`;
}

/** The value of what was typed: {@link splitMobile} then {@link composeMobile}. */
export function mobileValueOf(text: string, countries: readonly MobileDialCountry[], fixed: MobileDialCountry | null, currentDial: string): string {
  return composeMobile(splitMobile(text, countries, fixed, currentDial));
}

/** A country of a raw lookup row (`{ dialCode, mobileLength, isoCode|code, nameEn, nameAr }`); `null` without a dial code. */
export function mobileDialCountryOf(value: unknown): MobileDialCountry | null {
  if (typeof value !== 'object' || value === null) {
    return null;
  }

  const row = value as Readonly<Record<string, unknown>>;
  const dial = typeof row['dialCode'] === 'string' ? row['dialCode'].trim() : '';
  if (!isDialCode(dial) || row['isActive'] === false) {
    return null;
  }

  const length = typeof row['mobileLength'] === 'number' && Number.isInteger(row['mobileLength']) && row['mobileLength'] > 0 ? row['mobileLength'] : null;
  const text = (key: string): string | null => (typeof row[key] === 'string' && (row[key] as string).trim() !== '' ? (row[key] as string).trim() : null);
  return { dialCode: `+${dialDigits(dial)}`, mobileLength: length, isoCode: text('isoCode') ?? text('code'), nameEn: text('nameEn'), nameAr: text('nameAr') };
}
