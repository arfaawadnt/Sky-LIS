import { ChangeDetectionStrategy, Component, inject, input, linkedSignal, output } from '@angular/core';

import { PRICING_PERMISSIONS } from '../../../core/api/pricing.contracts';
import { REPORT_DESIGNS, type ReportDesign, reportDesignOverride } from '../../../core/api/report-designs';
import { I18nService } from '../../../core/i18n/i18n.service';
import { CanDirective } from '../../../shared/ui/can.directive';

/**
 * `<app-contract-report-design [design]="…" [pending]="…" (save)="…" />`: C7 of CP-34 (LIS-CR-010, C-14) — the report
 * design every result report of a visit under this tier prints in. Empty follows the report template, then the
 * laboratory parameter; the contract wins over both. Saving needs `pricing.edit` (the server decides).
 */
@Component({
  selector: 'app-contract-report-design',
  imports: [CanDirective],
  template: `
    <div class="row">
      <label class="field">
        <span>{{ i18n.t('pricing.contract.design.label') }}</span>
        <select class="inp" (change)="onChange($event)" [disabled]="pending()">
          <option value="" [selected]="choice() === null">{{ i18n.t('pricing.contract.design.inherit') }}</option>
          @for (design of designs; track design) {
            <option [value]="design" [selected]="design === choice()">{{ i18n.t('pricing.contract.design.' + design) }}</option>
          }
        </select>
      </label>
      <button type="button" class="btn primary" *appCan="permissions.edit" [disabled]="pending() || choice() === (design() ?? null)" (click)="save.emit(choice())">
        {{ i18n.t(pending() ? 'pricing.saving' : 'pricing.contract.design.save') }}
      </button>
    </div>
    <p class="muted small">{{ i18n.t('pricing.contract.design.hint') }}</p>
  `,
  styles: `
    :host { display: grid; gap: var(--sp-2); }
    .row { display: flex; flex-wrap: wrap; align-items: flex-end; gap: var(--sp-3); }
    .field { display: grid; gap: 4px; min-inline-size: 240px; }
    .field span { font-size: 12px; color: var(--ink-2); }
    p { margin: 0; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ContractReportDesignComponent {
  protected readonly i18n = inject(I18nService);

  /** The tier's saved override (null: none). */
  readonly design = input<ReportDesign | null | undefined>(null);

  readonly pending = input(false);

  /** The chosen design, or null to clear the override. */
  readonly save = output<ReportDesign | null>();

  protected readonly designs = REPORT_DESIGNS;
  protected readonly permissions = PRICING_PERMISSIONS;

  protected readonly choice = linkedSignal<ReportDesign | null>(() => this.design() ?? null);

  protected onChange(event: Event): void {
    this.choice.set(reportDesignOverride((event.target as HTMLSelectElement).value));
  }
}
