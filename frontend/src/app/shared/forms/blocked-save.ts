import { ElementRef, Injector, afterNextRender, inject, signal } from '@angular/core';
import type { AbstractControl } from '@angular/forms';

import { markAllTouched } from './mark-all-touched';

/**
 * A marked field: a reactive-form leaf (`formControlName`, or a native field carrying `ng-invalid` through `[formControl]`,
 * which leaves no attribute in the DOM). Group hosts (`form`, `[formGroup]`, `formGroupName`) are never targets.
 */
const INVALID_LEAF = ['[formcontrolname].ng-invalid', 'input.ng-invalid', 'select.ng-invalid', 'textarea.ng-invalid'].join(', ');

/** What can take the focus inside a custom control (a picker, a chip list). */
const FOCUSABLE = ['input:not([disabled]):not([type="hidden"])', 'select:not([disabled])', 'textarea:not([disabled])', 'button:not([disabled])', '[tabindex]:not([tabindex="-1"])'].join(', ');

function isNativeField(element: Element): element is HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement {
  return element instanceof HTMLInputElement || element instanceof HTMLSelectElement || element instanceof HTMLTextAreaElement;
}

/**
 * Focuses the first marked field under `root` in document order and returns it (`null` when there is none). A custom
 * control (`<app-catalogue-b-picker formControlName>`) hands the focus to its first focusable part; one that holds
 * marked fields of its own defers to them (the Arabic half of a name, not the English one that is filled).
 */
export function focusFirstInvalidField(root: ParentNode | null | undefined): HTMLElement | null {
  if (!root) {
    return null;
  }

  for (const element of Array.from(root.querySelectorAll<HTMLElement>(INVALID_LEAF))) {
    if (element.querySelector(INVALID_LEAF) !== null) {
      continue;   // a wrapper: its own marked field comes next in document order
    }

    const target = isNativeField(element) ? (element.disabled ? null : element) : element.querySelector<HTMLElement>(FOCUSABLE);
    if (target !== null) {
      target.focus();
      return target;
    }
  }

  return null;
}

/**
 * The refused save of a hand-written form (validation v0.5.0 F-03), the same behaviour as the kit's master-data drawer:
 * every field shows its line, the form says "The record was not saved: correct the marked fields first."
 * (`shared.masterData.invalid`) and the first marked field takes the focus.
 *
 * ```ts
 * protected readonly blockedSave = blockedSave();            // field initializer (injection context)
 * if (!this.blockedSave.check(form, '#my-form')) return;      // first line of save()
 * ```
 * ```html
 * @if (blockedSave.blocked()) { <p class="problem" role="alert">{{ i18n.t('shared.masterData.invalid') }}</p> }
 * ```
 * Call `reset()` when the form is opened again.
 */
export class BlockedSave {
  private readonly shown = signal(false);

  /** True after a refused save, until the next valid save or `reset()`. */
  readonly blocked = this.shown.asReadonly();

  constructor(
    private readonly host: HTMLElement,
    private readonly injector: Injector,
  ) {}

  /**
   * Marks the form touched and returns whether it is valid. When it is not, shows the notice and focuses the first marked
   * field under `scope` (a selector inside the component, e.g. the form id; the component itself when omitted).
   */
  check(form: AbstractControl, scope?: string): boolean {
    if (markAllTouched(form)) {
      this.shown.set(false);
      return true;
    }

    this.shown.set(true);
    if (focusFirstInvalidField(this.root(scope)) === null) {
      // The field is not on screen yet (a section rendered by the same change): try again once it is.
      afterNextRender({ read: () => void focusFirstInvalidField(this.root(scope)) }, { injector: this.injector });
    }

    return false;
  }

  reset(): void {
    this.shown.set(false);
  }

  private root(scope: string | undefined): ParentNode | null {
    return scope === undefined ? this.host : this.host.querySelector(scope);
  }
}

/** A `BlockedSave` bound to the calling component; call it in a field initializer. */
export function blockedSave(): BlockedSave {
  return new BlockedSave(inject<ElementRef<HTMLElement>>(ElementRef).nativeElement, inject(Injector));
}
