import { ChangeDetectionStrategy, Component, inject, input, model } from '@angular/core';

import type { FormulaOperandChoice, FormulaOperandItem, FormulaOperandServiceOption } from '../../../core/api/catalogue-a.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';

/** One row of the operand table: a referenced test, the services that may be ordered for it and the laboratory's choice. */
export interface FormulaOperandRow {
  readonly testCode: string;
  readonly testNameEn: string | null;
  readonly testNameAr: string | null;
  readonly services: readonly FormulaOperandServiceOption[];
  readonly defaultServiceId: string | null;
  readonly serviceId: string | null;
  readonly visibleOnReceipt: boolean;
}

/**
 * The rows for the choices of a formula check (LIS-CR-015): per reference, the choice the user made on the page, else the stored
 * operand, else the default service off the receipt. A remembered service that no longer carries the test falls back to the default.
 */
export function formulaOperandRows(choices: readonly FormulaOperandChoice[], current: readonly FormulaOperandRow[], stored: readonly FormulaOperandItem[]): FormulaOperandRow[] {
  return choices.map((choice) => {
    const prior = current.find((row) => row.testCode === choice.testCode) ?? stored.find((item) => item.testCode === choice.testCode) ?? null;
    const remembered = prior?.serviceId ?? null;
    const serviceId = prior === null ? choice.defaultServiceId : remembered === null || choice.services.some((service) => service.id === remembered) ? remembered : choice.defaultServiceId;
    return {
      testCode: choice.testCode,
      testNameEn: choice.testNameEn,
      testNameAr: choice.testNameAr,
      services: choice.services,
      defaultServiceId: choice.defaultServiceId,
      serviceId,
      visibleOnReceipt: serviceId !== null && (prior?.visibleOnReceipt ?? false),
    };
  });
}

/** The request entries of the rows. */
export function formulaOperandItems(rows: readonly FormulaOperandRow[]): FormulaOperandItem[] {
  return rows.map((row) => ({ testCode: row.testCode, serviceId: row.serviceId, visibleOnReceipt: row.serviceId !== null && row.visibleOnReceipt }));
}

/**
 * `<app-formula-operands [(rows)]="…" [disabled]="…" />` (LIS-CR-015 M3b / M3c): under the formula, one row per referenced test —
 * code and name, *Service to order* (the services that carry the test, the default preselected, "None — not ordered" with a warning)
 * and *Visible in receipt* (unchecked by default). Saved with the formula by the page.
 */
@Component({
  selector: 'app-formula-operands',
  template: `
    <section class="operands" [attr.aria-label]="i18n.t('catalogue-a.tests.formula.operands.title')">
      <h3>{{ i18n.t('catalogue-a.tests.formula.operands.title') }}</h3>
      <p class="hint">{{ i18n.t('catalogue-a.tests.formula.operands.hint') }}</p>
      <table>
        <thead>
          <tr>
            <th scope="col">{{ i18n.t('catalogue-a.tests.formula.operands.test') }}</th>
            <th scope="col">{{ i18n.t('catalogue-a.tests.formula.operands.service') }}</th>
            <th scope="col" class="center">{{ i18n.t('catalogue-a.tests.formula.operands.visible') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (row of rows(); track row.testCode; let index = $index) {
            <tr [attr.data-operand]="row.testCode">
              <td>
                <span class="mono">{{ row.testCode }}</span>
                <span class="name">{{ testName(row) }}</span>
              </td>
              <td>
                <select
                  class="inp sm"
                  [disabled]="disabled()"
                  [attr.aria-label]="i18n.tf('catalogue-a.tests.formula.operands.serviceFor', { code: row.testCode })"
                  (change)="choose(index, $event)"
                >
                  <option value="" [selected]="row.serviceId === null">{{ i18n.t('catalogue-a.tests.formula.operands.none') }}</option>
                  @for (service of row.services; track service.id) {
                    <option [value]="service.id" [selected]="service.id === row.serviceId">{{ serviceText(service, row) }}</option>
                  }
                </select>
                @if (row.serviceId === null) {
                  <p class="warn-strip" role="note">
                    {{ i18n.tf(row.services.length === 0 ? 'catalogue-a.tests.formula.operands.noService' : 'catalogue-a.tests.formula.operands.notOrdered', { code: row.testCode }) }}
                  </p>
                }
              </td>
              <td class="center">
                <input
                  type="checkbox"
                  [checked]="row.visibleOnReceipt"
                  [disabled]="disabled() || row.serviceId === null"
                  [attr.aria-label]="i18n.tf('catalogue-a.tests.formula.operands.visibleFor', { code: row.testCode })"
                  (change)="toggle(index, $event)"
                />
              </td>
            </tr>
          }
        </tbody>
      </table>
    </section>
  `,
  styles: `
    .operands {
      margin-block-start: var(--sp-4);
    }

    h3 {
      font-size: var(--fs-md);
      margin-block-end: var(--sp-1);
    }

    .hint {
      margin: 0 0 var(--sp-2);
      color: var(--muted);
      font-size: var(--fs-sm);
    }

    table {
      width: 100%;
      border-collapse: collapse;
    }

    th {
      text-align: start;
      font-size: var(--fs-sm);
      font-weight: 600;
      color: var(--ink-2);
      padding: 6px;
      border-bottom: 1px solid var(--line);
    }

    td {
      padding: 4px 6px;
      border-bottom: 1px solid var(--line-soft);
      vertical-align: top;
    }

    .center {
      text-align: center;
    }

    .name {
      margin-inline-start: var(--sp-2);
    }

    select {
      max-width: 24rem;
    }

    .warn-strip {
      margin: var(--sp-1) 0 0;
      padding: 4px 8px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FormulaOperandsComponent {
  protected readonly i18n = inject(I18nService);

  readonly rows = model.required<readonly FormulaOperandRow[]>();
  readonly disabled = input(false);

  protected testName(row: FormulaOperandRow): string {
    return row.testNameEn === null ? '' : localizedPick({ en: row.testNameEn, ar: row.testNameAr ?? row.testNameEn }, this.i18n.lang());
  }

  protected serviceText(service: FormulaOperandServiceOption, row: FormulaOperandRow): string {
    const name = localizedPick({ en: service.nameEn, ar: service.nameAr }, this.i18n.lang());
    const type = this.i18n.t(`catalogue-a.serviceType.${service.type}`);
    const suffix = service.id === row.defaultServiceId ? ` · ${this.i18n.t('catalogue-a.tests.formula.operands.default')}` : '';
    return `${service.code} · ${name} (${type})${suffix}`;
  }

  protected choose(index: number, event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.update(index, (row) => ({ ...row, serviceId: value === '' ? null : value, visibleOnReceipt: value === '' ? false : row.visibleOnReceipt }));
  }

  protected toggle(index: number, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.update(index, (row) => ({ ...row, visibleOnReceipt: checked }));
  }

  private update(index: number, change: (row: FormulaOperandRow) => FormulaOperandRow): void {
    this.rows.update((rows) => rows.map((row, position) => (position === index ? change(row) : row)));
  }
}
