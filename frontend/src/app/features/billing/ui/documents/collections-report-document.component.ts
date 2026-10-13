import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';

import type { CollectionsReportDocument, CollectionsReportRow } from '../../../../core/api/billing.contracts';
import { I18nService } from '../../../../core/i18n/i18n.service';
import { DocumentFrameComponent } from '../../../../shared/print/document-frame.component';
import { formatPrintDateTime, formatPrintMoney } from '../billing-format';
import { collectionsRowLabel, collectionsSubtitle } from '../collections-report-view';
import { branchOf } from './receipt-document.component';

/**
 * The treasury (collections) report (A4, change request 2026-10-13, item 2; LDM "Total Daily Income"): one row per
 * branch, cashier or payment method with the payments in, refunds out and net per method, and the grand total.
 */
@Component({
  selector: 'app-billing-collections-report-document',
  imports: [DocumentFrameComponent],
  template: `
    <app-document-frame
      [labNameEn]="doc().letterhead.labNameEn"
      [labNameAr]="doc().letterhead.labNameAr"
      [branch]="branch()"
      [branchEn]="doc().letterhead.branchNameEn"
      [branchAr]="doc().letterhead.branchNameAr"
      [title]="i18n.t('billing.doc.collectionsTitle')"
      [subtitle]="subtitle()"
      [printedBy]="doc().printedBy"
      [printedAt]="at(doc().printedAtUtc)"
    >
      <img documentLogo src="/brand/sky-lis-logo.png" alt="" height="40" />
      <div class="split">
        <table class="facts">
          <tbody>
            <tr><th scope="row">{{ i18n.t('billing.collections.scope') }}</th><td>{{ i18n.t('billing.collections.scope' + doc().scope) }}@if (doc().cashierName; as cashier) { · {{ cashier }}}</td></tr>
            <tr><th scope="row">{{ i18n.t('billing.collections.groupBy') }}</th><td>{{ i18n.t('billing.collections.group' + doc().groupBy) }}</td></tr>
          </tbody>
        </table>
        <table class="facts">
          <tbody>
            <tr><th scope="row">{{ i18n.t('billing.collections.period') }}</th><td>{{ doc().fromDay }}@if (doc().toDay !== doc().fromDay) { – {{ doc().toDay }}} ({{ doc().timeZone }})</td></tr>
            <tr><th scope="row">{{ i18n.t('billing.collections.periodUtc') }}</th><td>{{ at(doc().fromUtc) }} – {{ at(doc().toUtc) }}</td></tr>
          </tbody>
        </table>
      </div>

      <table class="report">
        <caption>{{ i18n.t('billing.collections.amounts') }} ({{ doc().currency }})</caption>
        <thead>
          <tr>
            <th scope="col" rowspan="2">{{ i18n.t('billing.collections.group' + doc().groupBy) }}</th>
            <th scope="col" rowspan="2" class="num">{{ i18n.t('billing.collections.payments') }}</th>
            <th scope="col" rowspan="2" class="num">{{ i18n.t('billing.collections.refunds') }}</th>
            <th scope="col" class="num" colspan="4">{{ i18n.t('billing.collections.netByMethod') }}</th>
            <th scope="col" rowspan="2" class="num">{{ i18n.t('billing.collections.net') }}</th>
          </tr>
          <tr>
            <th scope="col" class="num">{{ i18n.t('billing.method.Cash') }}</th>
            <th scope="col" class="num">{{ i18n.t('billing.method.Card') }}</th>
            <th scope="col" class="num">{{ i18n.t('billing.method.Pos') }}</th>
            <th scope="col" class="num">{{ i18n.t('billing.method.Transfer') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (row of doc().rows; track row.key) {
            <tr>
              <th scope="row">{{ label(row) }}</th>
              <td class="num">{{ money(row.totalPayments) }} <span class="muted">({{ row.paymentCount }})</span></td>
              <td class="num">{{ money(row.totalRefunds) }} <span class="muted">({{ row.refundCount }})</span></td>
              <td class="num">{{ money(row.cash.net) }}</td>
              <td class="num">{{ money(row.card.net) }}</td>
              <td class="num">{{ money(row.pos.net) }}</td>
              <td class="num">{{ money(row.transfer.net) }}</td>
              <td class="num">{{ money(row.net) }}</td>
            </tr>
          } @empty {
            <tr><td colspan="8" class="muted">{{ i18n.t('billing.collections.none') }}</td></tr>
          }
        </tbody>
        <tfoot>
          <tr class="totals">
            <th scope="row">{{ i18n.t('billing.doc.total') }}</th>
            <td class="num">{{ money(doc().totals.totalPayments) }} <span class="muted">({{ doc().totals.paymentCount }})</span></td>
            <td class="num">{{ money(doc().totals.totalRefunds) }} <span class="muted">({{ doc().totals.refundCount }})</span></td>
            <td class="num">{{ money(doc().totals.cash.net) }}</td>
            <td class="num">{{ money(doc().totals.card.net) }}</td>
            <td class="num">{{ money(doc().totals.pos.net) }}</td>
            <td class="num">{{ money(doc().totals.transfer.net) }}</td>
            <td class="num">{{ money(doc().totals.net) }}</td>
          </tr>
        </tfoot>
      </table>

      <table class="avoid-break">
        <caption>{{ i18n.t('billing.collections.byMethod') }} ({{ doc().currency }})</caption>
        <thead>
          <tr>
            <th scope="col">{{ i18n.t('billing.field.method') }}</th>
            <th scope="col" class="num">{{ i18n.t('billing.collections.payments') }}</th>
            <th scope="col" class="num">{{ i18n.t('billing.collections.refunds') }}</th>
            <th scope="col" class="num">{{ i18n.t('billing.collections.net') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr><th scope="row">{{ i18n.t('billing.method.Cash') }}</th><td class="num">{{ money(doc().totals.cash.payments) }}</td><td class="num">{{ money(doc().totals.cash.refunds) }}</td><td class="num">{{ money(doc().totals.cash.net) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.method.Card') }}</th><td class="num">{{ money(doc().totals.card.payments) }}</td><td class="num">{{ money(doc().totals.card.refunds) }}</td><td class="num">{{ money(doc().totals.card.net) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.method.Pos') }}</th><td class="num">{{ money(doc().totals.pos.payments) }}</td><td class="num">{{ money(doc().totals.pos.refunds) }}</td><td class="num">{{ money(doc().totals.pos.net) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.method.Transfer') }}</th><td class="num">{{ money(doc().totals.transfer.payments) }}</td><td class="num">{{ money(doc().totals.transfer.refunds) }}</td><td class="num">{{ money(doc().totals.transfer.net) }}</td></tr>
          <tr class="totals"><th scope="row">{{ i18n.t('billing.doc.total') }}</th><td class="num">{{ money(doc().totals.totalPayments) }}</td><td class="num">{{ money(doc().totals.totalRefunds) }}</td><td class="num">{{ money(doc().totals.net) }}</td></tr>
        </tbody>
      </table>
      <p class="muted">{{ i18n.t('billing.collections.equation') }}</p>

      <div class="signatures">
        <div class="signature"><span class="line"></span><span>{{ i18n.t('billing.collections.preparedBy') }}</span></div>
        <div class="signature"><span class="line"></span><span>{{ i18n.t('billing.collections.reviewedBy') }}</span></div>
      </div>
    </app-document-frame>
  `,
  styleUrl: './billing-document.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CollectionsReportDocumentComponent {
  protected readonly i18n = inject(I18nService);

  readonly doc = input.required<CollectionsReportDocument>();

  protected readonly branch = computed(() => branchOf(this.doc().letterhead.branchNameEn, this.doc().letterhead.branchNameAr, this.i18n.lang()));

  protected readonly subtitle = computed(() => collectionsSubtitle(this.i18n, this.doc()));

  protected label(row: CollectionsReportRow): string {
    return collectionsRowLabel(this.i18n, row, this.doc().groupBy);
  }

  protected money(value: number): string {
    return formatPrintMoney(value, this.i18n.lang());
  }

  protected at(value: string): string {
    return formatPrintDateTime(value, this.i18n.lang());
  }
}
