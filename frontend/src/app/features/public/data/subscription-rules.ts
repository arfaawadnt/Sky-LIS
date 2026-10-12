import type { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

import type { PublicCountry } from '../../../core/api/public-subscription.contracts';
import { SERVER_ERROR_KEY } from '../../../shared/forms/problem-to-form-errors';

/**
 * The browser side of the wizard's rules, mirroring the server so the user learns of a mistake before sending:
 * the country's mobile rule (`Country.NormaliseMobile`, GEO-010), the username shape (`PlatformUser.NormaliseUsername`,
 * CLI-042) and the password policy of 2026-10-09 (length only, never the username, CLI-040/041). The server decides.
 */

/** The E.164 form (`+201012345678`) of a mobile typed for `country`, or `null` when it does not have the country's length. */
export function normaliseMobile(input: string, country: Pick<PublicCountry, 'dialCode' | 'mobileLength'>): string | null {
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
 * The message of `message(country)` (shown under the field like a server message) when the value is not a mobile of the
 * selected country; empty values pass (combine with required). Validation 2026-10-11 C-08: the wizard names the digit count
 * like the Add client form does, never the generic "The value is not valid.".
 */
export function countryMobileValidator(
  country: () => Pick<PublicCountry, 'dialCode' | 'mobileLength'> | null,
  message: (country: Pick<PublicCountry, 'dialCode' | 'mobileLength'>) => string,
): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value: unknown = control.value;
    const selected = country();
    if (typeof value !== 'string' || value.trim() === '' || selected === null) {
      return null;
    }

    return normaliseMobile(value, selected) === null ? { [SERVER_ERROR_KEY]: message(selected) } : null;
  };
}

export const USERNAME_PATTERN = /^[a-z0-9._@-]{4,64}$/;

/** The username as the server stores it (trimmed, lower case). */
export function normaliseUsername(value: string): string {
  return value.trim().toLowerCase();
}

export function isUsernameShapeValid(value: string): boolean {
  return USERNAME_PATTERN.test(normaliseUsername(value));
}

export interface PasswordChecks {
  readonly length: boolean;
  readonly notUsername: boolean;
  readonly matches: boolean;
}

/** The three lines of the rule the page shows, each true when satisfied. */
export function passwordChecks(password: string, confirmation: string, username: string, minLength: number, maxLength: number): PasswordChecks {
  const name = normaliseUsername(username);
  return {
    length: password.length >= minLength && password.length <= maxLength,
    notUsername: password.length > 0 && (name.length === 0 || !password.toLowerCase().includes(name)),
    matches: password.length > 0 && password === confirmation,
  };
}

export function allPassed(checks: PasswordChecks): boolean {
  return checks.length && checks.notUsername && checks.matches;
}
