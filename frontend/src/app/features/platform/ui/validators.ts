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
