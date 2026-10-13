import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';

import type { WorkAmountReport, WorkAmountRow } from '../../../core/api/reporting.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { DocumentFrameComponent } from '../../../shared/print/document-frame.component';
import { formatDay, formatPrintCount, formatPrintMoney, rowLabel } from './report-format';

/**
 * The printed work amount report (A4 landscape; change request 2026-10-13 item 3): the letterhead, the period and the
 * grouping, one row per group and the grand total, with Latin digits in both languages like every printed document.
 */
@Component({
  selector: 'app-work-amount-document',
  imports: [DocumentFrameComponent],
  template: `
    <div class="print-page">
      <app-document-frame
        [labNameEn]="doc().letterhead.labNameEn"
        [labNameAr]="doc().letterhead.labNameAr"
        [branchEn]="doc().letterhead.branchNameEn"
        [branchAr]="doc().letterhead.branchNameAr"
        [title]="i18n.t('reporting.workAmount.title')"
        [subtitle]="subtitle()"
        [printedBy]="doc().generatedBy"
        [printedAt]="printedAt()"
      >
        <img documentLogo src="/brand/sky-lis-logo.png" alt="" height="40" />
        <table>
          <thead>
            <tr>
              <th scope="col">{{ i18n.t('reporting.workAmount.group.' + doc().groupBy) }}</th>
              <th scope="col" class="num">{{ i18n.t('reporting.workAmount.column.visits') }}</th>
              <th scope="col" class="num">{{ i18n.t('reporting.workAmount.column.services') }}</th>
              <th scope="col" class="num">{{ i18n.t('reporting.workAmount.column.gross') }}</th>
              <th scope="col" class="num">{{ i18n.t('reporting.workAmount.column.discount') }}</th>
              <th scope="col" class="num">{{ i18n.t('reporting.workAmount.column.cash') }}</th>
              <th scope="col" class="num">{{ i18n.t('reporting.workAmount.column.credit') }}</th>
              <th scope="col" class="num">{{ i18n.t('reporting.workAmount.column.net') }}</th>
            </tr>
          </thead>
          <tbody>
            @for (row of doc().rows; track row.key) {
              <tr>
                <td>{{ label(row) }}</td>
                <td class="num">{{ count(row.visits) }}</td>
                <td class="num">{{ count(row.services) }}</td>
                <td class="num">{{ money(row.gross) }}</td>
                <td class="num">{{ money(row.discount) }}</td>
                <td class="num">{{ money(row.cashIncome) }}</td>
                <td class="num">{{ money(row.creditIncome) }}</td>
                <td class="num">{{ money(row.net) }}</td>
              </tr>
            } @empty {
              <tr><td colspan="8">{{ i18n.t('reporting.workAmount.empty') }}</td></tr>
            }
          </tbody>
          <tfoot>
            <tr class="total">
              <th scope="row">{{ i18n.t('reporting.workAmount.total') }}</th>
              <td class="num">{{ count(doc().totals.visits) }}</td>
              <td class="num">{{ count(doc().totals.services) }}</td>
              <td class="num">{{ money(doc().totals.gross) }}</td>
              <td class="num">{{ money(doc().totals.discount) }}</td>
              <td class="num">{{ money(doc().totals.cashIncome) }}</td>
              <td class="num">{{ money(doc().totals.creditIncome) }}</td>
              <td class="num">{{ money(doc().totals.net) }}</td>
            </tr>
          </tfoot>
        </table>
        <span documentFooter>{{ i18n.t('reporting.workAmount.footnote') }}</span>
      </app-document-frame>
    </div>
  `,
  styles: `
    :host {
      display: block;
    }

    .total td,
    .total th {
      font-weight: 700;
      background: #f3f3f3;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkAmountDocumentComponent {
  protected readonly i18n = inject(I18nService);

  readonly doc = input.required<WorkAmountReport>();

  protected readonly subtitle = computed(() => {
    const doc = this.doc();
    const lang = this.i18n.lang();
    const period = doc.fromDay === doc.toDay ? formatDay(doc.fromDay, lang, 'print') : `${formatDay(doc.fromDay, lang, 'print')} – ${formatDay(doc.toDay, lang, 'print')}`;
    return `${this.i18n.t('reporting.workAmount.period')}: ${period} · ${this.i18n.t('reporting.workAmount.groupBy')}: ${this.i18n.t('reporting.workAmount.group.' + doc.groupBy)}`;
  });

  protected readonly printedAt = computed(() => {
    const date = new Date(this.doc().generatedAtUtc);
    return Number.isNaN(date.getTime()) ? '' : new Intl.DateTimeFormat(this.i18n.lang() === 'ar' ? 'ar-EG-u-nu-latn' : 'en-GB', { dateStyle: 'medium', timeStyle: 'short' }).format(date);
  });

  protected label(row: WorkAmountRow): string {
    return rowLabel(row, this.i18n.lang());
  }

  protected money(value: number): string {
    return formatPrintMoney(value, this.i18n.lang());
  }

  protected count(value: number): string {
    return formatPrintCount(value, this.i18n.lang());
  }
}
