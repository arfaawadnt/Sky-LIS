import type { AbstractControl } from '@angular/forms';

/**
 * Marks a control tree touched (so every field shows its validation line) and returns whether it is valid; the
 * usual first line of a save handler: `if (!markAllTouched(this.form)) return;`.
 */
export function markAllTouched(control: AbstractControl): boolean {
  control.markAllAsTouched();
  control.updateValueAndValidity({ onlySelf: false, emitEvent: true });
  return control.valid;
}

/** The first invalid leaf control in document order (depth-first), for focusing. */
export function firstInvalid(control: AbstractControl): AbstractControl | null {
  if (control.valid) {
    return null;
  }

  const children = (control as { controls?: unknown }).controls;
  if (children && typeof children === 'object') {
    for (const child of Object.values(children as Record<string, AbstractControl>)) {
      const found = firstInvalid(child);
      if (found) {
        return found;
      }
    }
  }

  return control.invalid ? control : null;
}
