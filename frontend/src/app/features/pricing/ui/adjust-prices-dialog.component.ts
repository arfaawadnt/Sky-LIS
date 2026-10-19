import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import type { ProblemDetails } from '../../../core/api/problem-details';
import { ADJUST_LIMITS, type AdjustBasePriceListRequest, type AdjustBasePriceListResult, PRICE_LIMITS, PRICE_ROUNDINGS, type PriceAdjustMode, type PriceRounding } from '../../../core/api/pricing.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { adjustPrice, formatAmount } from '../data/money';

export type AdjustScope = 'all' | 'filtered';

/** The dialog's form value, as `toAdjustRequest` reads it. */
export interface AdjustFormValue {
  readonly mode: PriceAdjustMode;
  readonly value: number;
  readonly rounding: PriceRounding;
  readonly scope: AdjustScope;
}

/**
 * "Bulk adjust" of the price list editor (CR 2026-10-19 C21): amount or percent, rounding, scope (every line or the rows
 * of the current filter); *Preview* asks the server for the rows (`preview`), the table shows old → new with the lines
 * held at 0, *Apply* writes them (`apply`). Dumb: the page runs the mutation and passes `pending` / `result` / `problem`.
 */
@Component({
  selector: 'app-adjust-prices-dialog',
  imports: [ReactiveFormsModule, DialogComponent, FormFieldComponent, IconComponent, SaveShortcutDirective],
  template: `
    <app-dialog [(open)]="open" [title]="i18n.t('pricing.adjust.title')" size="lg">
      <form [formGroup]="form" (ngSubmit)="runPreview()" appSaveShortcut (appSaveShortcut)="runPreview()" novalidate>
        <p class="muted small">{{ i18n.t('pricing.adjust.help') }}</p>
        @if (problem(); as current) {
          <p class="problem" role="alert">{{ problemText(current) }}</p>
        }
        <div class="form-grid">
          <app-form-field [label]="i18n.t('pricing.adjust.mode')">
            <select formControlName="mode">
              <option value="Percent">{{ i18n.t('pricing.adjust.percent') }}</option>
              <option value="Amount">{{ i18n.t('pricing.adjust.amount') }}</option>
            </select>
          </app-form-field>
          <app-form-field [label]="i18n.t(form.controls.mode.value === 'Percent' ? 'pricing.adjust.percentValue' : 'pricing.adjust.amountValue')" [hint]="i18n.t('pricing.adjust.valueHint')">
            <input type="number" formControlName="value" step="0.01" />
          </app-form-field>
          <app-form-field [label]="i18n.t('pricing.adjust.rounding')">
            <select formControlName="rounding">
              @for (rounding of roundings; track rounding) {
                <option [value]="rounding">{{ i18n.t('pricing.adjust.rounding.' + rounding) }}</option>
              }
            </select>
          </app-form-field>
          <app-form-field [label]="i18n.t('pricing.adjust.scope')">
            <select formControlName="scope">
              <option value="all">{{ i18n.tf('pricing.adjust.scopeAll', { count: lineCount() }) }}</option>
              <option value="filtered" [disabled]="!filterActive()">{{ i18n.tf('pricing.adjust.scopeFiltered', { count: filteredIds().length }) }}</option>
            </select>
          </app-form-field>
          <p class="example small full" aria-live="polite">{{ example() }}</p>
        </div>
      </form>

      @if (result(); as outcome) {
        <p class="small summary" role="status">
          {{ i18n.tf('pricing.adjust.summary', { rows: outcome.rows.length, changed: outcome.changed }) }}
          @if (outcome.clamped > 0) {
            · <span class="warn">{{ i18n.tc('pricing.adjust.clamped', outcome.clamped) }}</span>
          }
        </p>
        <div class="preview">
          <table>
            <caption class="visually-hidden">{{ i18n.t('pricing.adjust.previewCaption') }}</caption>
            <thead>
              <tr>
                <th scope="col">{{ i18n.t('pricing.grid.code') }}</th>
                <th scope="col">{{ i18n.t('pricing.grid.service') }}</th>
                <th scope="col" class="num">{{ i18n.t('pricing.adjust.oldPrice') }}</th>
                <th scope="col" class="num">{{ i18n.t('pricing.adjust.newPrice') }}</th>
              </tr>
            </thead>
            <tbody>
              @for (row of outcome.rows; track row.serviceId) {
                <tr [class.changed]="row.oldPrice !== row.newPrice" [class.clamped]="row.clamped">
                  <td class="mono">{{ row.code ?? '—' }}</td>
                  <td>{{ i18n.lang() === 'ar' ? (row.nameAr ?? row.nameEn ?? '—') : (row.nameEn ?? row.nameAr ?? '—') }}</td>
                  <td class="num">{{ amount(row.oldPrice) }}</td>
                  <td class="num">
                    {{ amount(row.newPrice) }}
                    @if (row.clamped) {
                      <span class="tag">{{ i18n.t('pricing.adjust.heldAtZero') }}</span>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }

      <ng-container dialogFooter>
        <button type="button" class="btn" (click)="open.set(false)">{{ i18n.t('pricing.cancel') }}</button>
        <button type="button" class="btn" [disabled]="pending()" (click)="runPreview()">
          <app-icon name="search" [size]="15" />{{ i18n.t('pricing.adjust.preview') }}
        </button>
        <button type="button" class="btn primary" [disabled]="!canApply()" (click)="runApply()">
          <app-icon name="check" [size]="15" />{{ i18n.t(pending() ? 'pricing.adjust.applying' : 'pricing.adjust.apply') }}
        </button>
      </ng-container>
    </app-dialog>
  `,
  styles: `
    form {
      display: grid;
      gap: var(--sp-3);
    }
    .example,
    .summary {
      margin: 0;
      color: var(--ink-2);
    }
    .summary {
      margin-top: var(--sp-3);
    }
    .warn {
      color: var(--warn);
      font-weight: 600;
    }
    .preview {
      max-height: 40vh;
      overflow: auto;
      margin-top: var(--sp-2);
    }
    table {
      width: 100%;
      border-collapse: collapse;
      font-size: var(--fs-sm);
    }
    th,
    td {
      padding: 4px var(--sp-2);
      border-bottom: 1px solid var(--line);
      text-align: start;
    }
    .num {
      text-align: end;
      font-variant-numeric: tabular-nums;
    }
    tr.changed td {
      font-weight: 600;
    }
    tr.clamped td {
      background: var(--warn-soft, transparent);
    }
    .tag {
      margin-inline-start: var(--sp-1);
      padding: 0 var(--sp-1);
      border-radius: var(--r-pill);
      background: var(--warn-soft, transparent);
      color: var(--warn);
      font-size: var(--fs-xs);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdjustPricesDialogComponent {
  protected readonly i18n = inject(I18nService);

  readonly open = model(false);
  readonly pending = input(false);
  readonly result = input<AdjustBasePriceListResult | null>(null);
  readonly problem = input<ProblemDetails | null>(null);
  /** Every line of the list (scope "all"). */
  readonly lineCount = input(0);
  /** The ids of the rows the editor's filter shows (scope "filtered"); meaningful when `filterActive`. */
  readonly filteredIds = input<readonly string[]>([]);
  readonly filterActive = input(false);
  readonly preview = output<AdjustBasePriceListRequest>();
  readonly apply = output<AdjustBasePriceListRequest>();

  protected readonly roundings = PRICE_ROUNDINGS;

  protected readonly form = new FormGroup({
    mode: new FormControl<PriceAdjustMode>('Percent', { nonNullable: true }),
    value: new FormControl(10, { nonNullable: true, validators: [Validators.required, Validators.min(-PRICE_LIMITS.maxAmount), Validators.max(PRICE_LIMITS.maxAmount)] }),
    rounding: new FormControl<PriceRounding>('None', { nonNullable: true }),
    scope: new FormControl<AdjustScope>('all', { nonNullable: true }),
  });

  private readonly value = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });

  /** "100.00 → 110.00" with the current mode, value and rounding. */
  protected readonly example = computed(() => {
    const current = this.value();
    const adjusted = adjustPrice(100, current.mode ?? 'Percent', Number(current.value ?? 0), current.rounding ?? 'None');
    return this.i18n.tf('pricing.adjust.example', { from: formatAmount(100, this.i18n.lang()), to: formatAmount(adjusted.price, this.i18n.lang()) });
  });

  protected readonly canApply = computed(() => {
    const outcome = this.result();
    return outcome !== null && !outcome.applied && outcome.changed > 0 && !this.pending();
  });

  constructor() {
    effect(() => {
      if (this.open()) {
        untracked(() => this.form.reset({ mode: 'Percent', value: 10, rounding: 'None', scope: this.filterActive() ? 'filtered' : 'all' }));
      }
    });
  }

  protected runPreview(): void {
    const request = this.request(true);
    if (request !== null) {
      this.preview.emit(request);
    }
  }

  protected runApply(): void {
    const request = this.request(false);
    if (request !== null && this.canApply()) {
      this.apply.emit(request);
    }
  }

  protected amount(value: number): string {
    return formatAmount(value, this.i18n.lang());
  }

  protected problemText(problem: ProblemDetails): string {
    return problemMessage(this.i18n, 'pricing', problem);
  }

  private request(preview: boolean): AdjustBasePriceListRequest | null {
    if (this.pending() || !markAllTouched(this.form)) {
      return null;
    }

    return toAdjustRequest(this.form.getRawValue(), this.filteredIds(), preview);
  }
}

/** The request of the form: the filtered rows' ids as the scope, or null for every line; the value as the server bounds it. */
export function toAdjustRequest(value: AdjustFormValue, filteredIds: readonly string[], preview: boolean): AdjustBasePriceListRequest {
  const bounded = value.mode === 'Percent' ? Math.min(Math.max(value.value, ADJUST_LIMITS.minPercent), ADJUST_LIMITS.maxPercent) : value.value;
  return {
    mode: value.mode,
    value: Math.round(bounded * 100) / 100,
    rounding: value.rounding,
    serviceIds: value.scope === 'filtered' ? [...filteredIds] : null,
    preview,
  };
}
