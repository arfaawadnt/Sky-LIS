import { type Injector, type Signal, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import type { AbstractControl, ValidationErrors } from '@angular/forms';
import { map, startWith } from 'rxjs';

/** A snapshot of what a field needs to render its state. */
export interface ControlSnapshot {
  readonly errors: ValidationErrors | null;
  readonly touched: boolean;
  readonly invalid: boolean;
  readonly disabled: boolean;
  readonly required: boolean;
}

function snapshot(control: AbstractControl): ControlSnapshot {
  return {
    errors: control.errors,
    touched: control.touched,
    invalid: control.invalid,
    disabled: control.disabled,
    required: control.hasValidator(requiredProbe) || probeRequired(control),
  };
}

// `hasValidator` only recognises the exact function reference; a probe on an empty value tells for custom ones.
function requiredProbe(): null {
  return null;
}

function probeRequired(control: AbstractControl): boolean {
  const validator = control.validator;
  if (!validator) {
    return false;
  }

  const result = validator({ value: null } as AbstractControl);
  return result !== null && 'required' in result;
}

/**
 * Turns a reactive control into a signal of its state (errors, touched, validity), so an OnPush component can
 * render it without subscribing itself. `control.events` emits on value, status, touched and pristine changes.
 */
export function controlState(control: AbstractControl | null, injector: Injector): Signal<ControlSnapshot> {
  if (control === null) {
    return signal<ControlSnapshot>({ errors: null, touched: false, invalid: false, disabled: false, required: false }).asReadonly();
  }

  return toSignal(
    control.events.pipe(
      startWith(null),
      map(() => snapshot(control)),
    ),
    { initialValue: snapshot(control), injector },
  );
}
