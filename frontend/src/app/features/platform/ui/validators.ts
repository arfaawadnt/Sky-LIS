import type { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

import { SERVER_ERROR_KEY } from '../../../shared/forms/problem-to-form-errors';

/** The password rule of the platform (decision 2026-10-09, 02 §6.1): 8–128 characters, no composition rules, not containing the username. */
export const PASSWORD_MIN_LENGTH = 8;
export const PASSWORD_MAX_LENGTH = 128;

/** Portal sub-domain: 3–40 of a-z, 0-9 and '-', not starting or ending with '-' (CLI-001; reserved names are the server's). */
export const SLUG_PATTERN = /^[a-z0-9](?:[a-z0-9-]{1,38}[a-z0-9])$/;

/** Usernames of the platform: 4–64 of letters, digits, '.', '_', '-', '@' (identity's rule). */
export const USERNAME_PATTERN = /^[A-Za-z0-9._@-]{4,64}$/;

/**
 * Group validator: the confirmation control carries a message (shown by `app-form-field` like a server message)
 * while the two values differ; the group carries `{ mismatch: true }`.
 */
export function matchingPasswords(passwordKey: string, confirmKey: string, message: () => string): ValidatorFn {
  return (group: AbstractControl): ValidationErrors | null => {
    const password = group.get(passwordKey);
    const confirm = group.get(confirmKey);
    if (!password || !confirm || confirm.disabled) {
      return null;
    }

    const mismatch = confirm.value !== '' && password.value !== confirm.value;
    const errors: Record<string, unknown> = { ...(confirm.errors ?? {}) };
    if (mismatch) {
      errors[SERVER_ERROR_KEY] = message();
    } else {
      delete errors[SERVER_ERROR_KEY];
    }

    confirm.setErrors(Object.keys(errors).length > 0 ? errors : null);
    return mismatch ? { mismatch: true } : null;
  };
}

/** The password must not contain the value of a sibling control (the username, 02 §6.1); the server checks again (CLI-041). */
export function notContaining(siblingKey: string, message: () => string): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const sibling = control.parent?.get(siblingKey)?.value;
    const needle = typeof sibling === 'string' ? sibling.trim().toLowerCase() : '';
    const value = typeof control.value === 'string' ? control.value.toLowerCase() : '';
    return needle.length > 0 && value.includes(needle) ? { [SERVER_ERROR_KEY]: message() } : null;
  };
}

/** A message unless the text parses to a JSON object or array (SA-16; LIB-003 on the server). Empty passes. */
export function jsonValidator(message: (detail: string) => string): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const text = typeof control.value === 'string' ? control.value.trim() : '';
    if (text === '') {
      return null;
    }

    try {
      const parsed: unknown = JSON.parse(text);
      return typeof parsed === 'object' && parsed !== null ? null : { [SERVER_ERROR_KEY]: message('') };
    } catch (error: unknown) {
      return { [SERVER_ERROR_KEY]: message(error instanceof Error ? error.message : '') };
    }
  };
}

/** Pretty-prints JSON text (2 spaces); returns the input unchanged when it does not parse. */
export function formatJson(text: string): string {
  try {
    return JSON.stringify(JSON.parse(text), null, 2);
  } catch {
    return text;
  }
}

/** The country data a mobile is checked against (`CountryListItem` has both). */
export interface MobileCountry {
  readonly dialCode: string;
  readonly mobileLength: number;
}

/**
 * The E.164 form (`+201012345678`) of a mobile typed for `country`, or `null` when it does not have the country's length.
 * Mirrors `Country.NormaliseMobile` on the server (GEO-010 laboratory mobile, GEO-014 manager mobile); the server decides.
 */
export function normaliseCountryMobile(input: string, country: MobileCountry): string | null {
  const raw = input.trim();
  if (raw.length === 0 || !/^[\d+\s\-()]+$/.test(raw)) {
    return null;
  }

  const digits = raw.replace(/\D/g, '');
  const dial = country.dialCode.replace(/^\+/, '');
  let national: string;
  if (raw.startsWith('+')) {
    national = digits.startsWith(dial) ? digits.slice(dial.length) : '';
  } else if (digits.startsWith('00' + dial)) {
    national = digits.slice(dial.length + 2);
  } else {
    national = digits.length === country.mobileLength + 1 && digits.startsWith('0') ? digits.slice(1) : digits;
  }

  return national.length === country.mobileLength && !national.startsWith('0') ? `+${dial}${national}` : null;
}

/**
 * Validation 2026-10-11 C-03: the mobile must have the length of the country chosen in the sibling `countryKey` control.
 * Empty values and an unknown country pass (combine with required); re-run it when the country changes.
 */
export function countryMobileValidator(countryKey: string, countryOf: (id: string) => MobileCountry | null, message: (country: MobileCountry) => string): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value: unknown = control.value;
    const countryId: unknown = control.parent?.get(countryKey)?.value;
    const country = typeof countryId === 'string' && countryId !== '' ? countryOf(countryId) : null;
    if (typeof value !== 'string' || value.trim() === '' || country === null) {
      return null;
    }

    return normaliseCountryMobile(value, country) === null ? { [SERVER_ERROR_KEY]: message(country) } : null;
  };
}

/** Validation 2026-10-11 C-19: a money amount has at most two decimals (WAL-001 on the server); the value is never rounded. */
export function maxTwoDecimals(message: () => string): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value: unknown = control.value;
    if (typeof value !== 'number' || !Number.isFinite(value)) {
      return null;
    }

    return hasAtMostTwoDecimals(value) ? null : { [SERVER_ERROR_KEY]: message() };
  };
}

/** `250.55` and `250.5` pass, `250.555` does not (decided on the decimal text, not with floating-point arithmetic). */
export function hasAtMostTwoDecimals(value: number): boolean {
  const text = value.toString();
  if (/e/i.test(text)) {
    return Math.round(value * 100) / 100 === value && Math.abs(value) >= 1;
  }

  const point = text.indexOf('.');
  return point < 0 || text.length - point - 1 <= 2;
}

/**
 * The IANA time zone names the browser knows (validation 2026-10-11 C-08: the country's zone is picked, not typed), sorted,
 * with `UTC` and any `extra` names (the zones already stored) kept; the server validates again (GEO-007).
 */
export function ianaTimeZones(extra: readonly string[] = []): readonly string[] {
  const known = typeof Intl.supportedValuesOf === 'function' ? Intl.supportedValuesOf('timeZone') : [];
  return [...new Set([...known, 'UTC', ...extra.filter((zone) => zone.trim() !== '')])].sort((a, b) => a.localeCompare(b, 'en'));
}
