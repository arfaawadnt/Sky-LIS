import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, signal, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import {
  CASH_ENTRY_CATEGORIES,
  PAYMENT_METHODS,
  type CashEntryCategory,
  type CashEntryKind,
  type PaymentMethod,
  type PaymentMethodOption,
  type PostCashEntryRequest,
} from '../../../core/api/billing.contracts';
import type { ProblemDetails } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { hasTwoDecimals } from './billing-format';

const isKind = (value: string): value is PaymentMethod => (PAYMENT_METHODS as readonly string[]).includes(value);

/**
 * "Add cash out" / "Add cash in" of the cash ledger (CP-70, change request LIS-CR-011): category of the kind, amount, method
 * (the laboratory's definitions, cash first by default; a reference only when the method requires it), reason (required) and
 * note. The entry is posted for the signed-in cashier on the session branch at the moment it is saved. F10 / Ctrl+S save.
 */
@Component({
  selector: 'app-billing-cash-entry-dialog',
  imports: [ReactiveFormsModule, DialogComponent, FormFieldComponent, SaveShortcutDirective],
  template: `
    <app-dialog [open]="open()" (openChange)="open.set($event)" [title]="i18n.t('billing.ledger.entry.title' + kind())" size="md">
      <form [formGroup]="form" (ngSubmit)="submit()" appSaveShortcut (appSaveShortcut)="submit()" novalidate>
        <div class="form-grid">
          <app-form-field [label]="i18n.t('billing.ledger.entry.category')">
            <select formControlName="category">
              @for (category of categories(); track category) {
                <option [value]="category">{{ i18n.t('billing.ledger.category.' + category) }}</option>
              }
            </select>
          </app-form-field>
          <app-form-field [label]="i18n.t('billing.field.amount')" [hint]="currency()">
            <input type="number" inputmode="decimal" step="0.01" min="0.01" formControlName="amount" />
          </app-form-field>
          <app-form-field [label]="i18n.t('billing.field.method')" [hint]="i18n.t('billing.ledger.entry.methodHint')">
            <select formControlName="methodKey" (change)="onMethodChange()">
              @if (methods().length > 0) {
                @for (option of methods(); track option.id) {
                  <option [value]="option.code">{{ optionName(option) }}</option>
                }
              } @else {
                @for (method of kinds; track method) {
                  <option [value]="method">{{ i18n.t('billing.method.' + method) }}</option>
                }
              }
            </select>
          </app-form-field>
          @if (referenceRequired()) {
            <app-form-field [label]="i18n.t('billing.field.reference')">
              <input type="text" formControlName="reference" maxlength="64" autocomplete="off" />
            </app-form-field>
          }
          <app-form-field class="full" [label]="i18n.t('billing.field.reason')" [hint]="i18n.t('billing.ledger.entry.reasonHint')">
            <input type="text" formControlName="reason" maxlength="200" autocomplete="off" />
          </app-form-field>
          <app-form-field class="full" [label]="i18n.t('billing.field.note')">
            <textarea formControlName="note" rows="2" maxlength="500"></textarea>
          </app-form-field>
        </div>
        @if (problem(); as problem) {
          <p class="problem" role="alert">{{ problemText(problem) }}</p>
        }
      </form>

      <ng-container dialogFooter>
        <button type="button" class="btn ghost" (click)="open.set(false)">{{ i18n.t('shared.dialog.cancel') }}</button>
        <button type="button" class="btn primary" (click)="submit()" [disabled]="pending()">
          {{ pending() ? i18n.t('billing.pay.working') : i18n.t('billing.ledger.entry.save') }}
        </button>
      </ng-container>
    </app-dialog>
  `,
  styles: `
    .problem {
      margin: var(--sp-2) 0 0;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CashEntryDialogComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly kinds = PAYMENT_METHODS;

  readonly open = model(false);
  readonly kind = input<CashEntryKind>('CashOut');
  /** The laboratory's active payment methods in pick-list order; empty = the four kinds. */
  readonly methods = input<readonly PaymentMethodOption[]>([]);
  readonly currency = input('');
  readonly pending = input(false);
  readonly problem = input<ProblemDetails | null>(null);

  readonly submitted = output<PostCashEntryRequest>();

  protected readonly form = new FormGroup({
    category: new FormControl<CashEntryCategory>('Supplier', { nonNullable: true }),
    amount: new FormControl<number | null>(null, { validators: [Validators.required, Validators.min(0.01)] }),
    methodKey: new FormControl('Cash', { nonNullable: true }),
    reference: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(64)] }),
    reason: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(200)] }),
    note: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
  });

  private readonly methodKey = signal('Cash');

  protected readonly categories = computed(() => CASH_ENTRY_CATEGORIES[this.kind()]);

  private readonly selected = computed<PaymentMethodOption | null>(() => this.methods().find((option) => option.code === this.methodKey()) ?? null);

  /** The definition's rule when one is chosen; otherwise the legacy rule "every kind but cash". */
  protected readonly referenceRequired = computed(() => {
    const option = this.selected();
    return option !== null ? option.requiresReference : this.methodKey() !== 'Cash';
  });

  constructor() {
    // Opening (or a kind change) starts a fresh entry: the first category of the kind, the first cash method.
    effect(() => {
      const isOpen = this.open();
      const categories = this.categories();
      const methods = this.methods();
      untracked(() => {
        if (isOpen) {
          const cash = methods.find((option) => option.kind === 'Cash') ?? methods[0];
          const key = cash?.code ?? 'Cash';
          this.form.reset({ category: categories[0] ?? 'Other', amount: null, methodKey: key, reference: '', reason: '', note: '' });
          this.methodKey.set(key);
        }
      });
    });
  }

  protected onMethodChange(): void {
    this.methodKey.set(this.form.controls.methodKey.value);
  }

  protected submit(): void {
    const { category, amount, methodKey, reference, reason, note } = this.form.getRawValue();
    const referenceMissing = this.referenceRequired() && reference.trim() === '';
    if (!markAllTouched(this.form) || amount === null || !hasTwoDecimals(amount) || reason.trim() === '' || referenceMissing || this.pending()) {
      if (referenceMissing) {
        this.form.controls.reference.setErrors({ required: true });
      }

      return;
    }

    const option = this.selected();
    const method: PaymentMethod = option !== null ? option.kind : isKind(methodKey) ? methodKey : 'Cash';
    this.submitted.emit({
      kind: this.kind(),
      category,
      amount,
      reason: reason.trim(),
      method,
      ...(option !== null ? { methodCode: option.code } : {}),
      reference: this.referenceRequired() ? reference.trim() : null,
      note: note.trim() === '' ? null : note.trim(),
    });
  }

  protected optionName(option: PaymentMethodOption): string {
    return localizedPick({ en: option.nameEn, ar: option.nameAr }, this.i18n.lang());
  }

  protected problemText(problem: ProblemDetails): string {
    return problemMessage(this.i18n, 'billing', problem);
  }
}
