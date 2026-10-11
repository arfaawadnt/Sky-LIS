import { ChangeDetectionStrategy, Component, ElementRef, Injector, type Signal, afterRenderEffect, computed, contentChild, effect, inject, input, signal, untracked } from '@angular/core';
import { NgControl } from '@angular/forms';

import type { FieldRule, ProblemDetails } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { type ControlSnapshot, controlState } from '../../forms/control-state';
import { fieldErrorText } from '../../forms/problem-text';
import { SERVER_ERROR_KEY, SERVER_RULES_KEY, toControlPath } from '../../forms/problem-to-form-errors';

let nextFieldId = 0;

/**
 * `<app-form-field [label]="…" [hint]="…"><input formControlName="code" /></app-form-field>`: label, required
 * marker, hint and the error line of the projected control. Errors come from the control's validators (translated
 * through `shared.validation.*`), from `problemToFormErrors` (`server` error) or from the `problem` input
 * (a `VALIDATION-400` whose `errors` carry this control's name); a server error whose rules are known is shown in the
 * user's language with its bounds (C-06), never the validator's English. The label is wired to the control (`for`/`id`,
 * `aria-describedby`, `aria-invalid`) without the page having to invent ids.
 */
@Component({
  selector: 'app-form-field',
  templateUrl: './form-field.component.html',
  styleUrl: './form-field.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.invalid]': 'showError()' },
})
export class FormFieldComponent {
  private readonly i18n = inject(I18nService);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly ngControl = contentChild(NgControl);
  private readonly generatedId = `app-field-${++nextFieldId}`;

  readonly label = input.required<string>();
  readonly hint = input<string | null>(null);
  /** Overrides the marker derived from the control's validators. */
  readonly required = input<boolean | null>(null);
  /** The control's DTO name when it differs from `formControlName` (used to pick the server error). */
  readonly name = input<string | null>(null);
  readonly problem = input<ProblemDetails | null>(null);

  protected readonly controlId = signal(this.generatedId);
  protected readonly errorId = `${this.generatedId}-error`;
  protected readonly hintId = `${this.generatedId}-hint`;

  private readonly stateSource = signal<Signal<ControlSnapshot>>(controlState(null, this.injector));
  protected readonly state = computed(() => this.stateSource()());

  protected readonly isRequired = computed(() => this.required() ?? this.state().required);

  private readonly serverMessage = computed<string | null>(() => {
    const errors = this.problem()?.errors;
    if (!errors) {
      return null;
    }

    const wanted = (this.name() ?? this.ngControl()?.name?.toString() ?? '').toLowerCase();
    if (wanted === '') {
      return null;
    }

    for (const [key, messages] of Object.entries(errors)) {
      const path = toControlPath(key).toLowerCase();
      if (path === wanted || path.endsWith(`.${wanted}`)) {
        return fieldErrorText(this.i18n, this.problem()?.rules?.[key], messages);
      }
    }

    return null;
  });

  protected readonly errorText = computed<string | null>(() => {
    const server = this.serverMessage();
    if (server !== null) {
      return server;
    }

    const { errors, touched, invalid } = this.state();
    if (!touched || !invalid || !errors) {
      return null;
    }

    return this.translate(errors);
  });

  protected readonly showError = computed(() => this.errorText() !== null);

  constructor() {
    effect(() => {
      const control = this.ngControl()?.control ?? null;
      untracked(() => this.stateSource.set(controlState(control, this.injector)));
    });

    afterRenderEffect(() => {
      this.ngControl();
      const invalid = this.showError();
      const describedBy = invalid ? this.errorId : this.hint() ? this.hintId : null;
      untracked(() => this.wire(invalid, describedBy));
    });
  }

  private wire(invalid: boolean, describedBy: string | null): void {
    const element = this.host.querySelector<HTMLElement>('input, select, textarea, [role="combobox"], [role="textbox"]');
    if (!element) {
      return;
    }

    if (element.id === '') {
      element.id = this.generatedId;
    }

    if (this.controlId() !== element.id) {
      this.controlId.set(element.id);
    }

    element.setAttribute('aria-invalid', invalid ? 'true' : 'false');
    if (describedBy) {
      element.setAttribute('aria-describedby', describedBy);
    } else {
      element.removeAttribute('aria-describedby');
    }
  }

  private translate(errors: Readonly<Record<string, unknown>>): string {
    if (typeof errors[SERVER_ERROR_KEY] === 'string') {
      const rules = errors[SERVER_RULES_KEY];
      return Array.isArray(rules) ? fieldErrorText(this.i18n, rules as readonly FieldRule[], [errors[SERVER_ERROR_KEY]]) : errors[SERVER_ERROR_KEY];
    }

    if (errors['required']) {
      return this.i18n.t('shared.validation.required');
    }

    const maxlength = errors['maxlength'] as { requiredLength?: number } | undefined;
    if (maxlength) {
      return this.i18n.tf('shared.validation.maxlength', { max: maxlength.requiredLength ?? 0 });
    }

    const minlength = errors['minlength'] as { requiredLength?: number } | undefined;
    if (minlength) {
      return this.i18n.tf('shared.validation.minlength', { min: minlength.requiredLength ?? 0 });
    }

    const min = errors['min'] as { min?: number } | undefined;
    if (min) {
      return this.i18n.tf('shared.validation.min', { min: min.min ?? 0 });
    }

    const max = errors['max'] as { max?: number } | undefined;
    if (max) {
      return this.i18n.tf('shared.validation.max', { max: max.max ?? 0 });
    }

    for (const key of ['code', 'mobile', 'email', 'pattern'] as const) {
      if (errors[key]) {
        return this.i18n.t(`shared.validation.${key}`);
      }
    }

    return this.i18n.t('shared.validation.invalid');
  }
}
