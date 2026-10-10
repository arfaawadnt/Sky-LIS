import { type AbstractControl, type ValidationErrors, type ValidatorFn, Validators } from '@angular/forms';

/** Codes of master data: upper-case letters, digits, `-`, `_` and `.`; 1…32 characters (the domain's `Code` value object). */
export const CODE_PATTERN = /^[A-Z0-9][A-Z0-9._-]{0,31}$/;

/** Mobile numbers as the SRS stores them: optional `+`, 8…15 digits, spaces allowed between groups. */
export const MOBILE_PATTERN = /^\+?\d(?:[ \d]{6,14})\d$/;

export const CODE_MAX_LENGTH = 32;

function isEmpty(value: unknown): boolean {
  return value === null || value === undefined || (typeof value === 'string' && value.trim() === '');
}

/** `{ code: true }` when the value is not a well-formed code. Empty values pass (combine with `Validators.required`). */
export const codeValidator: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const value: unknown = control.value;
  if (isEmpty(value)) {
    return null;
  }

  return typeof value === 'string' && CODE_PATTERN.test(value) ? null : { code: true };
};

/** `{ mobile: true }` when the value is not a mobile number. Empty values pass. */
export const mobileValidator: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const value: unknown = control.value;
  if (isEmpty(value)) {
    return null;
  }

  return typeof value === 'string' && MOBILE_PATTERN.test(value.trim()) ? null : { mobile: true };
};

/** `Validators.required` that also refuses whitespace-only strings. */
export const requiredText: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const value: unknown = control.value;
  return isEmpty(value) ? { required: true } : null;
};

/** The validators of a text field in one call: required (optional), max length. */
export function textValidators(options: { required?: boolean; maxLength?: number; minLength?: number }): ValidatorFn[] {
  const validators: ValidatorFn[] = [];
  if (options.required) {
    validators.push(requiredText);
  }

  if (options.minLength !== undefined) {
    validators.push(Validators.minLength(options.minLength));
  }

  if (options.maxLength !== undefined) {
    validators.push(Validators.maxLength(options.maxLength));
  }

  return validators;
}
