import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';

import type { CashLedgerRow, CashLedgerView } from '../../../../core/api/billing.contracts';
import { I18nService } from '../../../../core/i18n/i18n.service';
import { DocumentFrameComponent } from '../../../../shared/print/document-frame.component';
import { formatPrintDateTime, formatPrintMoney } from '../billing-format';
import { ledgerDetail, ledgerScopeLabel } from '../cash-ledger-view';
import { branchOf } from './receipt-document.component';

/**
 * The A4 "Daily cash sheet" of the cash ledger (CP-70, change request LIS-CR-011): the scope and the period, the opening
 * balance, receipts and refunds per method, cash in and out, count differences, the closing balance, every line with its
 * running balance, and the handover signatures.
 */
@Component({
  selector: 'app-billing-cash-sheet-document',
  imports: [DocumentFrameComponent],
  template: `
    <app-document-frame
      [labNameEn]="doc().letterhead.labNameEn"
      [labNameAr]="doc().letterhead.labNameAr"
      [branch]="branch()"
      [branchEn]="doc().letterhead.branchNameEn"
      [branchAr]="doc().letterhead.branchNameAr"
      [title]="i18n.t('billing.doc.cashSheetTitle')"
      [reference]="period()"
      [printedBy]="doc().printedBy"
      [printedAt]="at(doc().printedAtUtc)"
    >
      <img documentLogo src="/brand/sky-lis-logo.png" alt="" height="40" />
      <div class="split">
        <table class="facts">
          <tbody>
            <tr><th scope="row">{{ i18n.t('billing.field.cashier') }}</th><td>{{ scope() }}</td></tr>
            <tr><th scope="row">{{ i18n.t('billing.collections.period') }}</th><td>{{ period() }} ({{ doc().timeZone }})</td></tr>
          </tbody>
        </table>
        <table class="facts">
          <tbody>
            <tr><th scope="row">{{ i18n.t('billing.ledger.opening') }}</th><td class="num">{{ money(doc().summary.openingBalance) }}</td></tr>
            <tr class="totals"><th scope="row">{{ i18n.t('billing.ledger.closingBalance') }}</th><td class="num">{{ money(doc().summary.closingBalance) }} {{ doc().currency }}</td></tr>
          </tbody>
        </table>
      </div>

      <table class="avoid-break">
        <caption>{{ i18n.t('billing.ledger.summary') }} ({{ doc().currency }})</caption>
        <thead>
          <tr>
            <th scope="col">{{ i18n.t('billing.field.method') }}</th>
            <th scope="col" class="num">{{ i18n.t('billing.doc.payments') }}</th>
            <th scope="col" class="num">{{ i18n.t('billing.doc.refunds') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr><th scope="row">{{ i18n.t('billing.method.Cash') }}</th><td class="num">{{ money(doc().summary.receipts.cash) }}</td><td class="num">{{ money(doc().summary.refunds.cash) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.method.Card') }}</th><td class="num">{{ money(doc().summary.receipts.card) }}</td><td class="num">{{ money(doc().summary.refunds.card) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.method.Pos') }}</th><td class="num">{{ money(doc().summary.receipts.pos) }}</td><td class="num">{{ money(doc().summary.refunds.pos) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.method.Transfer') }}</th><td class="num">{{ money(doc().summary.receipts.transfer) }}</td><td class="num">{{ money(doc().summary.refunds.transfer) }}</td></tr>
          <tr class="totals">
            <th scope="row">{{ i18n.t('billing.doc.total') }}</th>
            <td class="num">{{ money(doc().summary.receipts.total) }} ({{ doc().summary.receiptCount }})</td>
            <td class="num">{{ money(doc().summary.refunds.total) }} ({{ doc().summary.refundCount }})</td>
          </tr>
        </tbody>
      </table>

      <table class="avoid-break">
        <tbody>
          <tr><th scope="row">{{ i18n.t('billing.ledger.opening') }}</th><td class="num">{{ money(doc().summary.openingBalance) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.closing.cashPayments') }}</th><td class="num">{{ money(doc().summary.receipts.cash) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.ledger.cashRefunds') }}</th><td class="num">−{{ money(doc().summary.refunds.cash) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.ledger.cashIn') }}</th><td class="num">{{ money(doc().summary.cashIn) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.ledger.cashOut') }}</th><td class="num">−{{ money(doc().summary.cashOut) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.ledger.countDifferences') }}</th><td class="num">{{ money(doc().summary.countDifferences) }}</td></tr>
          <tr class="totals"><th scope="row">{{ i18n.t('billing.ledger.closingBalance') }}</th><td class="num">{{ money(doc().summary.closingBalance) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.ledger.voided') }}</th><td class="num">{{ money(doc().summary.voidedAmount) }} ({{ doc().summary.voidedCount }})</td></tr>
        </tbody>
      </table>
      <p class="muted">{{ i18n.t('billing.ledger.equation') }}</p>

      <table>
        <caption>{{ i18n.t('billing.ledger.lines') }}</caption>
        <thead>
          <tr>
            <th scope="col">{{ i18n.t('billing.doc.date') }}</th>
            <th scope="col">{{ i18n.t('billing.column.kind') }}</th>
            <th scope="col">{{ i18n.t('billing.column.number') }}</th>
            <th scope="col">{{ i18n.t('billing.ledger.detail') }}</th>
            <th scope="col" class="num">{{ i18n.t('billing.ledger.in') }}</th>
            <th scope="col" class="num">{{ i18n.t('billing.ledger.out') }}</th>
            <th scope="col" class="num">{{ i18n.t('billing.ledger.balance') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (row of doc().rows; track row.id) {
            <tr>
              <td>{{ at(row.atUtc) }}</td>
              <td>{{ i18n.t('billing.ledger.kind.' + row.kind) }} @if (row.status === 'Voided') {<span class="muted">({{ i18n.t('billing.status.Voided') }})</span>}</td>
              <td class="mono">{{ row.number }}</td>
              <td>{{ detail(row) }}</td>
              <td class="num">{{ row.moneyIn === 0 ? '' : money(row.moneyIn) }}</td>
              <td class="num">{{ row.moneyOut === 0 ? '' : money(row.moneyOut) }}</td>
              <td class="num">{{ money(row.balance) }}</td>
            </tr>
          } @empty {
            <tr><td colspan="7" class="muted">{{ i18n.t('billing.doc.none') }}</td></tr>
          }
        </tbody>
      </table>

      <div class="signatures">
        <div class="signature"><span class="line"></span><span>{{ i18n.t('billing.closing.handedOverBy') }}</span></div>
        <div class="signature"><span class="line"></span><span>{{ i18n.t('billing.closing.receivedBy') }}</span></div>
      </div>
    </app-document-frame>
  `,
  styleUrl: './billing-document.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CashSheetDocumentComponent {
  protected readonly i18n = inject(I18nService);

  readonly doc = input.required<CashLedgerView>();

  protected readonly branch = computed(() => branchOf(this.doc().letterhead.branchNameEn, this.doc().letterhead.branchNameAr, this.i18n.lang()));

  protected readonly period = computed(() => (this.doc().fromDay === this.doc().toDay ? this.doc().fromDay : `${this.doc().fromDay} – ${this.doc().toDay}`));

  protected readonly scope = computed(() => ledgerScopeLabel(this.i18n, this.doc()));

  protected detail(row: CashLedgerRow): string {
    return ledgerDetail(this.i18n, row);
  }

  protected money(value: number | null): string {
    return formatPrintMoney(value, this.i18n.lang());
  }

  protected at(value: string): string {
    return formatPrintDateTime(value, this.i18n.lang());
  }
}
