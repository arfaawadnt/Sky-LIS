import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import type { ProblemDetails } from '../../../core/api/problem-details';
import { type NegotiatePriceRequest, PRICE_LIMITS } from '../../../core/api/pricing.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { formatAmount, hasScale } from '../data/money';

/** The price being negotiated: the service's label and its current shares (zero for a service the tier does not price). */
export interface NegotiationTarget {
  readonly serviceId: string;
  readonly label: string;
  readonly listPrice: number;
  readonly patientShare: number;
  readonly payerShare: number;
  readonly isExcluded: boolean;
}

const amountValidators = [Validators.required, Validators.min(0), Validators.max(PRICE_LIMITS.maxAmount)];

/**
 * "Exclude a negotiated price" of CP-34 (LDM "Exclude"): the hand-typed patient and payer shares of one service, kept
 * through every Apply (BR-07). Dumb: emits the request; the page runs the mutation.
 */
@Component({
  selector: 'app-negotiate-price-dialog',
  imports: [ReactiveFormsModule, DialogComponent, FormFieldComponent, SaveShortcutDirective],
  template: `
    <app-dialog [(open)]="open" [title]="i18n.t('pricing.negotiate.title')" size="sm">
      <form [formGroup]="form" (ngSubmit)="submit()" appSaveShortcut (appSaveShortcut)="submit()" novalidate>
        <p class="target">{{ target()?.label }}</p>
        <p class="muted small">{{ i18n.t('pricing.negotiate.help') }}</p>
        @if (problem(); as current) {
          <p class="problem" role="alert">{{ problemText(current) }}</p>
        }
        <div class="form-grid">
          <app-form-field [label]="i18n.t('pricing.grid.patientShare')">
            <input type="number" formControlName="patientShare" min="0" step="0.01" />
          </app-form-field>
          <app-form-field [label]="i18n.t('pricing.grid.payerShare')">
            <input type="number" formControlName="payerShare" min="0" step="0.01" />
          </app-form-field>
        </div>
        <p class="total small">{{ i18n.t('pricing.grid.total') }}: <strong>{{ total() }}</strong></p>
        @if (scaleError()) {
          <p class="problem small" role="alert">{{ i18n.t('pricing.negotiate.twoDecimals') }}</p>
        }
      </form>
      <ng-container dialogFooter>
        <button type="button" class="btn" (click)="open.set(false)">{{ i18n.t('pricing.cancel') }}</button>
        <button type="button" class="btn primary" [disabled]="pending()" (click)="submit()">{{ i18n.t(pending() ? 'pricing.saving' : 'pricing.negotiate.submit') }}</button>
      </ng-container>
    </app-dialog>
  `,
  styles: `
    form {
      display: grid;
      gap: var(--sp-3);
    }
    .target {
      margin: 0;
      font-weight: 600;
    }
    .total,
    .muted,
    .problem {
      margin: 0;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NegotiatePriceDialogComponent {
  protected readonly i18n = inject(I18nService);

  readonly open = model(false);
  readonly target = input<NegotiationTarget | null>(null);
  readonly pending = input(false);
  readonly problem = input<ProblemDetails | null>(null);
  readonly negotiate = output<NegotiatePriceRequest>();

  protected readonly form = new FormGroup({
    patientShare: new FormControl(0, { nonNullable: true, validators: amountValidators }),
    payerShare: new FormControl(0, { nonNullable: true, validators: amountValidators }),
  });

  private readonly value = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });

  protected readonly total = computed(() => formatAmount(Number(this.value().patientShare ?? 0) + Number(this.value().payerShare ?? 0), this.i18n.lang()));

  protected readonly scaleError = computed(() => !hasScale(Number(this.value().patientShare ?? 0), 2) || !hasScale(Number(this.value().payerShare ?? 0), 2));

  constructor() {
    effect(() => {
      const target = this.target();
      if (this.open() && target) {
        untracked(() => this.form.reset({ patientShare: target.patientShare, payerShare: target.payerShare }));
      }
    });
  }

  protected submit(): void {
    if (this.pending() || !markAllTouched(this.form) || this.scaleError()) {
      return;
    }

    this.negotiate.emit(this.form.getRawValue());
  }

  protected problemText(problem: ProblemDetails): string {
    return problemMessage(this.i18n, 'pricing', problem);
  }
}
