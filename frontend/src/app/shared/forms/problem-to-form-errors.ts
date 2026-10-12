import type { AbstractControl, FormGroup } from '@angular/forms';

import type { FieldRule, ProblemDetails } from '../../core/api/problem-details';

/**
 * The error key the kit uses for server-reported field errors. `FormFieldComponent` renders it verbatim unless
 * `SERVER_RULES_KEY` sits beside it, in which case it shows its own localized text for those rules (C-06).
 */
export const SERVER_ERROR_KEY = 'server';

/** The machine-readable rules of a server field error (`FieldRule[]`), set by `problemToFormErrors` when the problem has them. */
export const SERVER_RULES_KEY = 'serverRules';

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
 * gets `{ server: "message" }` (plus `{ serverRules: [...] }` when the server named the rules, so the field shows a
 * localized text, C-06) and is marked touched so the field shows it; the entries without a control come back so the page
 * can show them as a general message. Returns an empty map when the problem carries no field errors.
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

    const rules: readonly FieldRule[] | undefined = problem.rules?.[key];
    control.setErrors({ ...(control.errors ?? {}), [SERVER_ERROR_KEY]: messages.join(' '), ...(rules && rules.length > 0 ? { [SERVER_RULES_KEY]: rules } : {}) });
    control.markAsTouched();
  }

  return unmatched;
}

/** Clears the server errors set by `problemToFormErrors` (the user is editing again). */
export function clearServerErrors(control: AbstractControl): void {
  const errors = control.errors;
  if (errors && SERVER_ERROR_KEY in errors) {
    const rest = Object.fromEntries(Object.entries(errors).filter(([key]) => key !== SERVER_ERROR_KEY && key !== SERVER_RULES_KEY));
    control.setErrors(Object.keys(rest).length === 0 ? null : rest);
  }

  const children = (control as { controls?: unknown }).controls;
  if (children && typeof children === 'object') {
    for (const child of Object.values(children as Record<string, AbstractControl>)) {
      clearServerErrors(child);
    }
  }
}

/** `true` when the control carries an error set by `problemToFormErrors`. */
function hasServerError(control: AbstractControl): boolean {
  if (control.errors && SERVER_ERROR_KEY in control.errors) {
    return true;
  }

  const children = (control as { controls?: unknown }).controls;
  return !!children && typeof children === 'object' && Object.values(children as Record<string, AbstractControl>).some(hasServerError);
}

/**
 * Validation v0.5.0 B-01: a server error about a pair of fields (ORG-017, the governorate of another country) lands on one
 * control, but the user may correct the *other* one. For each group of related control names, when any member's value
 * differs from `snapshot` (the raw form value when the errors arrived), the server errors of every member are cleared,
 * so the form is valid again and the next save reaches the server, which re-checks the pair. Returns whether it cleared any.
 */
export function clearLinkedServerErrors(form: FormGroup, groups: readonly (readonly string[])[], snapshot: Readonly<Record<string, unknown>>): boolean {
  let cleared = false;
  for (const group of groups) {
    const members = group.map((name) => form.get(name)).filter((control): control is AbstractControl => control !== null);
    if (!members.some(hasServerError)) {
      continue;
    }

    const changed = group.some((name) => JSON.stringify(form.get(name)?.getRawValue() ?? null) !== JSON.stringify(snapshot[name] ?? null));
    if (changed) {
      members.forEach(clearServerErrors);
      cleared = true;
    }
  }

  return cleared;
}
