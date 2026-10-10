import type { AbstractControl, FormGroup } from '@angular/forms';

import type { ProblemDetails } from '../../core/api/problem-details';

/** The error key the kit uses for server-reported field errors; `FormFieldComponent` renders it verbatim. */
export const SERVER_ERROR_KEY = 'server';

function lowerFirst(segment: string): string {
  return segment.length === 0 ? segment : segment.charAt(0).toLowerCase() + segment.slice(1);
}

/** `Lines[0].NameEn` → `lines.0.nameEn`, the path `FormGroup.get()` understands. */
export function toControlPath(serverKey: string): string {
  return serverKey
    .replace(/\[(\d+)\]/g, '.$1')
    .split('.')
    .filter((segment) => segment.length > 0)
    .map(lowerFirst)
    .join('.');
}

function findControl(form: FormGroup, path: string): AbstractControl | null {
  const direct = form.get(path);
  if (direct) {
    return direct;
  }

  // Flat DTO ↔ localized group: `nameEn` → `name.en`, `nameAr` → `name.ar`.
  const localized = /^(.*)(En|Ar)$/.exec(path);
  if (localized) {
    return form.get(`${localized[1]}.${(localized[2] ?? '').toLowerCase()}`);
  }

  return null;
}

/**
 * Maps a `VALIDATION-400` problem (`errors: { nameEn: ["..."] }`) onto the controls of a form: each matched control
 * gets `{ server: "message" }` and is marked touched so the field shows it; the entries without a control come back
 * so the page can show them as a general message. Returns an empty map when the problem carries no field errors.
 */
export function problemToFormErrors(problem: ProblemDetails | null, form: FormGroup): Readonly<Record<string, readonly string[]>> {
  const unmatched: Record<string, readonly string[]> = {};
  if (!problem?.errors) {
    return unmatched;
  }

  for (const [key, messages] of Object.entries(problem.errors)) {
    const control = findControl(form, toControlPath(key));
    if (control === null || messages.length === 0) {
      unmatched[key] = messages;
      continue;
    }

    control.setErrors({ ...(control.errors ?? {}), [SERVER_ERROR_KEY]: messages.join(' ') });
    control.markAsTouched();
  }

  return unmatched;
}

/** Clears the server errors set by `problemToFormErrors` (the user is editing again). */
export function clearServerErrors(control: AbstractControl): void {
  const errors = control.errors;
  if (errors && SERVER_ERROR_KEY in errors) {
    const rest = Object.fromEntries(Object.entries(errors).filter(([key]) => key !== SERVER_ERROR_KEY));
    control.setErrors(Object.keys(rest).length === 0 ? null : rest);
  }

  const children = (control as { controls?: unknown }).controls;
  if (children && typeof children === 'object') {
    for (const child of Object.values(children as Record<string, AbstractControl>)) {
      clearServerErrors(child);
    }
  }
}
