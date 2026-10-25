import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import type { CashClosingPreview } from '../../../core/api/billing.contracts';
import type { ProblemDetails } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { formatDateTime, formatMoney, hasTwoDecimals, roundMoney } from './billing-format';

/** What the day close submits; the scope and the period are the preview's. */
export interface DayCloseInput {
  readonly countedCash: number | null;
  readonly note: string | null;
}

/**
 * "Close the day" of the cash ledger (CP-70 / CP-30, change request LIS-CR-011): the drawer of the ledger's scope over its close
 * window, previewed by the server — opening balance, cash receipts, refunds, cash in and out, expected cash — then the counted
 * cash, the difference and its note (required when the count differs: BIL-034). The closed period becomes read-only.
 */
@Component({
  selector: 'app-billing-day-close-dialog',
  imports: [ReactiveFormsModule, DialogComponent, FormFieldComponent, SaveShortcutDirective],
  template: `
    <app-dialog [open]="open()" (openChange)="open.set($event)" [title]="i18n.t('billing.ledger.close.title')" size="md">
      @if (preview(); as drawer) {
        <p class="muted small">{{ i18n.tf('billing.ledger.close.period', { from: at(drawer.periodFromUtc), to: at(drawer.periodToUtc), cashier: cashierLabel() }) }}</p>
        <table class="figures">
          <tbody>
            <tr><th scope="row">{{ i18n.t('billing.ledger.opening') }}</th><td class="num">{{ money(drawer.totals.openingBalance ?? 0) }}</td></tr>
            <tr><th scope="row">{{ i18n.t('billing.closing.cashPayments') }}</th><td class="num">{{ money(drawer.totals.cashPayments) }}</td></tr>
            <tr><th scope="row">{{ i18n.t('billing.ledger.cashRefunds') }}</th><td class="num">−{{ money(drawer.totals.cashRefunds) }}</td></tr>
            <tr><th scope="row">{{ i18n.t('billing.ledger.cashIn') }}</th><td class="num">{{ money(drawer.totals.cashIn ?? 0) }}</td></tr>
            <tr><th scope="row">{{ i18n.t('billing.ledger.cashOut') }}</th><td class="num">−{{ money(drawer.totals.cashOut ?? 0) }}</td></tr>
            <tr class="total"><th scope="row">{{ i18n.t('billing.closing.expectedCash') }}</th><td class="num">{{ money(drawer.totals.expectedCash) }} {{ drawer.currency }}</td></tr>
          </tbody>
        </table>
        @if (drawer.overlapsClosedPeriod) {
          <p class="warn-strip">{{ i18n.t('billing.closing.overlaps') }}</p>
        }
        <form [formGroup]="form" (ngSubmit)="submit()" appSaveShortcut (appSaveShortcut)="submit()" novalidate>
          <div class="form-grid">
            <app-form-field [label]="i18n.t('billing.closing.countedCash')">
              <input type="number" inputmode="decimal" step="0.01" min="0" formControlName="countedCash" />
            </app-form-field>
            <p class="difference" aria-live="polite">{{ i18n.t('billing.closing.difference') }}: {{ money(difference()) }}</p>
            <app-form-field class="full" [label]="i18n.t('billing.closing.note')" [hint]="i18n.t('billing.closing.noteHint')">
              <textarea formControlName="note" rows="2" maxlength="1000"></textarea>
            </app-form-field>
          </div>
          @if (noteMissing()) {
            <p class="problem" role="alert">{{ i18n.t('billing.error.BIL-034') }}</p>
          }
        </form>
      } @else if (loading()) {
        <p class="muted">{{ i18n.t('billing.loading') }}</p>
      }
      @if (problem(); as problem) {
        <p class="problem" role="alert">{{ problemText(problem) }}</p>
      }

      <ng-container dialogFooter>
        <button type="button" class="btn ghost" (click)="open.set(false)">{{ i18n.t('shared.dialog.cancel') }}</button>
        <button type="button" class="btn primary" (click)="submit()" [disabled]="!ready()">
          {{ pending() ? i18n.t('billing.pay.working') : i18n.t('billing.ledger.close.submit') }}
        </button>
      </ng-container>
    </app-dialog>
  `,
  styles: `
    .figures {
      width: 100%;
      border-collapse: collapse;
      font-size: 13px;
      margin-bottom: var(--sp-3);
    }

    .figures th,
    .figures td {
      padding: 4px 6px;
      border-bottom: 1px solid var(--line);
      text-align: start;
      font-weight: 400;
    }

    .figures .num {
      text-align: end;
      font-variant-numeric: tabular-nums;
    }

    .figures .total th,
    .figures .total td {
      font-weight: 700;
    }

    .difference {
      align-self: end;
      margin: 0 0 var(--sp-2);
      font-weight: 600;
    }

    .small,
    .problem {
      font-size: 12px;
      margin: var(--sp-2) 0;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DayCloseDialogComponent {
  protected readonly i18n = inject(I18nService);

  readonly open = model(false);
  readonly preview = input<CashClosingPreview | null>(null);
  readonly loading = input(false);
  readonly pending = input(false);
  readonly problem = input<ProblemDetails | null>(null);
  /** "Cashier One" or "All cashiers", already translated. */
  readonly cashierLabel = input('');

  readonly closeRequested = output<DayCloseInput>();

  protected readonly form = new FormGroup({
    countedCash: new FormControl<number | null>(null, { validators: [Validators.min(0)] }),
    note: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(1000)] }),
  });

  private readonly counted = toSignal(this.form.controls.countedCash.valueChanges, { initialValue: null });
  private readonly noteText = toSignal(this.form.controls.note.valueChanges, { initialValue: '' });

  protected readonly difference = computed<number | null>(() => {
    const drawer = this.preview();
    const counted = this.counted();
    return drawer === null || counted === null || counted === undefined ? null : roundMoney(counted - drawer.totals.expectedCash);
  });

  /** BIL-034: a non-zero difference needs a note (checked here so the cashier sees it before closing). */
  protected readonly noteMissing = computed(() => {
    const difference = this.difference();
    return difference !== null && difference !== 0 && this.noteText().trim() === '';
  });

  protected readonly ready = computed(() => {
    const drawer = this.preview();
    return drawer !== null && !drawer.overlapsClosedPeriod && !this.pending() && !this.noteMissing();
  });

  constructor() {
    effect(() => {
      const isOpen = this.open();
      untracked(() => {
        if (isOpen) {
          this.form.reset({ countedCash: null, note: '' });
        }
      });
    });
  }

  protected submit(): void {
    const counted = this.form.controls.countedCash.value;
    if (!this.ready() || !markAllTouched(this.form) || (counted !== null && !hasTwoDecimals(counted))) {
      return;
    }

    const note = this.form.controls.note.value.trim();
    this.closeRequested.emit({ countedCash: counted, note: note === '' ? null : note });
  }

  protected money(value: number | null): string {
    return formatMoney(value, this.i18n.lang());
  }

  protected at(value: string): string {
    return formatDateTime(value, this.i18n.lang());
  }

  protected problemText(problem: ProblemDetails): string {
    return problemMessage(this.i18n, 'billing', problem);
  }
}
