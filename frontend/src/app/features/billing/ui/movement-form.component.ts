import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, untracked } from '@angular/core';
import { type AbstractControl, FormControl, FormGroup, ReactiveFormsModule, type ValidationErrors, type ValidatorFn, Validators } from '@angular/forms';

import { PAYMENT_METHODS, type BillingReasonOption, type BillingTreasuryItem, type PaymentMethod } from '../../../core/api/billing.contracts';
import type { ProblemDetails } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { formatMoney, hasTwoDecimals, roundMoney } from './billing-format';

/** What a cashier submits: a payment (CP-23, CP-27) or a refund (CP-24, with reason and note). */
export interface MovementInput {
  readonly treasuryId: string;
  readonly amount: number;
  readonly method: PaymentMethod;
  readonly reference: string | null;
  readonly reasonId: string | null;
  readonly note: string | null;
}

export type MovementMode = 'payment' | 'refund';

/** The reference is mandatory for every method except cash (BR-26, BIL-003); re-run when the method changes. */
const referenceForNonCash: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const method = control.parent?.get('method')?.value as PaymentMethod | undefined;
  const value = typeof control.value === 'string' ? control.value.trim() : '';
  return method !== undefined && method !== 'Cash' && value === '' ? { required: true } : null;
};

const twoDecimals: ValidatorFn = (control: AbstractControl): ValidationErrors | null =>
  typeof control.value === 'number' && !hasTwoDecimals(control.value) ? { pattern: true } : null;

/**
 * The money form of the cash desk (dumb component): treasury approved for this device (BR-27), amount defaulting to
 * what is due and capped by it (BR-26 / BR-14), method, reference for non-cash; in refund mode a catalogue reason
 * (mandatory) and a note. Ctrl+S submits. The server decides; the form only prevents obvious refusals.
 */
@Component({
  selector: 'app-billing-movement-form',
  imports: [ReactiveFormsModule, FormFieldComponent, SaveShortcutDirective],
  template: `
    <form [formGroup]="form" (ngSubmit)="submit()" appSaveShortcut (appSaveShortcut)="submit()" novalidate>
      <div class="form-grid">
        <app-form-field [label]="i18n.t('billing.field.treasury')">
          <select formControlName="treasuryId">
            <option value="">{{ i18n.t('billing.pay.chooseTreasury') }}</option>
            @for (treasury of treasuries(); track treasury.id) {
              <option [value]="treasury.id">{{ treasury.code }} · {{ name(treasury) }}</option>
            }
          </select>
        </app-form-field>

        <app-form-field [label]="i18n.t('billing.field.amount')" [hint]="amountHint()">
          <input type="number" inputmode="decimal" step="0.01" min="0.01" formControlName="amount" />
        </app-form-field>

        <app-form-field [label]="i18n.t('billing.field.method')">
          <select formControlName="method" (change)="onMethodChange()">
            @for (method of methods; track method) {
              <option [value]="method">{{ i18n.t('billing.method.' + method) }}</option>
            }
          </select>
        </app-form-field>

        <app-form-field [label]="i18n.t('billing.field.reference')" [hint]="i18n.t('billing.pay.referenceHint')">
          <input type="text" formControlName="reference" maxlength="64" autocomplete="off" />
        </app-form-field>

        @if (mode() === 'refund') {
          <app-form-field class="full" [label]="i18n.t('billing.field.reason')">
            <select formControlName="reasonId">
              <option value="">{{ i18n.t('billing.refund.chooseReason') }}</option>
              @for (reason of reasons(); track reason.id) {
                <option [value]="reason.id">{{ reasonName(reason) }}</option>
              }
            </select>
          </app-form-field>
          <app-form-field class="full" [label]="i18n.t('billing.field.note')">
            <textarea formControlName="note" rows="2" maxlength="500"></textarea>
          </app-form-field>
        }
      </div>

      @if (problem(); as problem) {
        <p class="problem" role="alert">{{ problemText(problem) }}</p>
      }

      <button type="submit" class="btn primary wide" [disabled]="pending() || disabled()">
        {{ pending() ? i18n.t('billing.pay.working') : submitLabel() }}
      </button>
    </form>
  `,
  styles: `
    form {
      display: grid;
      gap: var(--sp-3);
    }

    .wide {
      width: 100%;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MovementFormComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly methods = PAYMENT_METHODS;

  readonly mode = input<MovementMode>('payment');
  readonly treasuries = input.required<readonly BillingTreasuryItem[]>();
  /** The treasury in use, shared with the page so it survives between visits. */
  readonly treasuryId = model('');
  /** The amount the form proposes (remaining or refundable); also the cap. */
  readonly dueAmount = input.required<number>();
  readonly currency = input('');
  readonly reasons = input<readonly BillingReasonOption[]>([]);
  readonly pending = input(false);
  readonly disabled = input(false);
  readonly problem = input<ProblemDetails | null>(null);
  readonly submitLabel = input.required<string>();

  readonly submitted = output<MovementInput>();

  protected readonly form = new FormGroup({
    treasuryId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    amount: new FormControl<number | null>(null, { validators: [Validators.required, Validators.min(0.01), twoDecimals, (control) => this.capValidator(control)] }),
    method: new FormControl<PaymentMethod>('Cash', { nonNullable: true, validators: [Validators.required] }),
    reference: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(64), referenceForNonCash] }),
    reasonId: new FormControl('', { nonNullable: true }),
    note: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
  });

  protected readonly amountHint = computed(() => `${this.i18n.t(this.mode() === 'refund' ? 'billing.refund.refundable' : 'billing.account.remaining')}: ${formatMoney(this.dueAmount(), this.i18n.lang())} ${this.currency()}`);

  constructor() {
    // The proposed amount follows what is due (a new visit, or the balance after a movement); the cap is re-checked.
    effect(() => {
      const due = roundMoney(Math.max(this.dueAmount(), 0));
      untracked(() => {
        this.form.controls.amount.setValue(due > 0 ? due : null);
        this.form.controls.amount.markAsUntouched();
      });
    });

    effect(() => {
      const selected = this.treasuryId();
      untracked(() => {
        if (this.form.controls.treasuryId.value !== selected) {
          this.form.controls.treasuryId.setValue(selected);
        }
      });
    });

    effect(() => {
      const refund = this.mode() === 'refund';
      untracked(() => {
        this.form.controls.reasonId.setValidators(refund ? [Validators.required] : []);
        this.form.controls.reasonId.updateValueAndValidity();
      });
    });
  }

  /**
   * A fresh form for the next movement, called by the page after a successful save (UAT A-06: a second refund was stored
   * with the first one's note). The treasury in use stays; the amount is what is due again; method, reference, reason and
   * note go back to their defaults.
   */
  reset(): void {
    const due = roundMoney(Math.max(this.dueAmount(), 0));
    this.form.reset({ treasuryId: this.treasuryId(), amount: due > 0 ? due : null, method: 'Cash', reference: '', reasonId: '', note: '' });
  }

  protected name(treasury: BillingTreasuryItem): string {
    return localizedPick({ en: treasury.nameEn, ar: treasury.nameAr }, this.i18n.lang());
  }

  protected reasonName(reason: BillingReasonOption): string {
    return localizedPick({ en: reason.nameEn, ar: reason.nameAr }, this.i18n.lang());
  }

  protected onMethodChange(): void {
    this.form.controls.reference.updateValueAndValidity();
  }

  protected problemText(problem: ProblemDetails): string {
    return problemMessage(this.i18n, 'billing', problem);
  }

  protected submit(): void {
    this.form.controls.reference.updateValueAndValidity();
    if (!markAllTouched(this.form) || this.pending() || this.disabled()) {
      return;
    }

    const value = this.form.getRawValue();
    this.treasuryId.set(value.treasuryId);
    const reference = value.reference.trim();
    const note = value.note.trim();
    this.submitted.emit({
      treasuryId: value.treasuryId,
      amount: roundMoney(value.amount ?? 0),
      method: value.method,
      reference: reference === '' ? null : reference,
      reasonId: this.mode() === 'refund' && value.reasonId !== '' ? value.reasonId : null,
      note: this.mode() === 'refund' && note !== '' ? note : null,
    });
  }

  /** Runs from the control's construction on, before the inputs are bound: an empty value never reads the input. */
  private capValidator(control: AbstractControl): ValidationErrors | null {
    if (typeof control.value !== 'number') {
      return null;
    }

    const cap = roundMoney(Math.max(this.dueAmount(), 0));
    return control.value > cap ? { max: { max: cap, actual: control.value } } : null;
  }
}
