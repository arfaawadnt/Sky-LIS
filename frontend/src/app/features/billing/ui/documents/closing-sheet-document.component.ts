import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';

import type { CashClosingSheetDocument } from '../../../../core/api/billing.contracts';
import { I18nService } from '../../../../core/i18n/i18n.service';
import { DocumentFrameComponent } from '../../../../shared/print/document-frame.component';
import { formatPrintDateTime, formatPrintMoney } from '../billing-format';
import { branchOf } from './receipt-document.component';

/** The cash-closing sheet (A4, CP-30, BR-16): totals per method, expected and counted cash, movements and handover signatures. */
@Component({
  selector: 'app-billing-closing-sheet-document',
  imports: [DocumentFrameComponent],
  template: `
    <app-document-frame
      [labNameEn]="doc().letterhead.labNameEn"
      [labNameAr]="doc().letterhead.labNameAr"
      [branch]="branch()"
      [branchEn]="doc().letterhead.branchNameEn"
      [branchAr]="doc().letterhead.branchNameAr"
      [title]="i18n.t('billing.doc.closingTitle')"
      [reference]="doc().number"
      [printedBy]="doc().printedBy"
      [printedAt]="at(doc().printedAtUtc)"
    >
      <img documentLogo src="/brand/sky-lis-logo.png" alt="" height="40" />
      <div class="split">
        <table class="facts">
          <tbody>
            <tr><th scope="row">{{ i18n.t('billing.field.cashier') }}</th><td>{{ doc().cashierName ?? i18n.t('billing.closing.allCashiers') }}</td></tr>
          </tbody>
        </table>
        <table class="facts">
          <tbody>
            <tr><th scope="row">{{ i18n.t('billing.closing.from') }}</th><td>{{ at(doc().periodFromUtc) }}</td></tr>
            <tr><th scope="row">{{ i18n.t('billing.closing.to') }}</th><td>{{ at(doc().periodToUtc) }}</td></tr>
            <tr><th scope="row">{{ i18n.t('billing.closing.closedBy') }}</th><td>{{ doc().closedByName }} · {{ at(doc().closedAtUtc) }}</td></tr>
          </tbody>
        </table>
      </div>

      <table>
        <caption>{{ i18n.t('billing.closing.totals') }} ({{ doc().currency }})</caption>
        <thead>
          <tr>
            <th scope="col">{{ i18n.t('billing.field.method') }}</th>
            <th scope="col" class="num">{{ i18n.t('billing.doc.payments') }}</th>
            <th scope="col" class="num">{{ i18n.t('billing.doc.refunds') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr><th scope="row">{{ i18n.t('billing.method.Cash') }}</th><td class="num">{{ money(doc().totals.cashPayments) }}</td><td class="num">{{ money(doc().totals.cashRefunds) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.method.Card') }}</th><td class="num">{{ money(doc().totals.cardPayments) }}</td><td class="num">{{ money(doc().totals.cardRefunds) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.method.Pos') }}</th><td class="num">{{ money(doc().totals.posPayments) }}</td><td class="num">{{ money(doc().totals.posRefunds) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.method.Transfer') }}</th><td class="num">{{ money(doc().totals.transferPayments) }}</td><td class="num">{{ money(doc().totals.transferRefunds) }}</td></tr>
          <tr class="totals">
            <th scope="row">{{ i18n.t('billing.doc.total') }}</th>
            <td class="num">{{ money(doc().totals.totalPayments) }} ({{ doc().totals.paymentCount }})</td>
            <td class="num">{{ money(doc().totals.totalRefunds) }} ({{ doc().totals.refundCount }})</td>
          </tr>
        </tbody>
      </table>

      <table class="avoid-break">
        <tbody>
          <tr class="totals"><th scope="row">{{ i18n.t('billing.closing.expectedCash') }}</th><td class="num">{{ money(doc().totals.expectedCash) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.closing.countedCash') }}</th><td class="num">{{ money(doc().countedCash) }}</td></tr>
          <tr class="totals"><th scope="row">{{ i18n.t('billing.closing.difference') }}</th><td class="num">{{ money(doc().difference) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.closing.outstanding') }}</th><td class="num">{{ money(doc().totals.outstandingBalance) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.closing.discounts') }}</th><td class="num">{{ money(doc().totals.discountTotal) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.closing.visits') }}</th><td class="num">{{ doc().totals.visitCount }}</td></tr>
        </tbody>
      </table>
      <p class="muted">{{ i18n.t('billing.closing.equation') }}</p>
      @if (doc().note; as note) {
        <p class="note"><b>{{ i18n.t('billing.closing.note') }}:</b> {{ note }}</p>
      }

      <table>
        <caption>{{ i18n.t('billing.closing.movements') }}</caption>
        <thead>
          <tr>
            <th scope="col">{{ i18n.t('billing.closing.number') }}</th>
            <th scope="col">{{ i18n.t('billing.doc.date') }}</th>
            <th scope="col">{{ i18n.t('billing.field.accession') }}</th>
            <th scope="col">{{ i18n.t('billing.field.method') }}</th>
            <th scope="col">{{ i18n.t('billing.field.cashier') }}</th>
            <th scope="col" class="num">{{ i18n.t('billing.field.amount') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (movement of doc().movements; track movement.id) {
            <tr>
              <td class="mono">{{ movement.number }} @if (movement.status === 'Voided') {<span class="muted">({{ i18n.t('billing.status.Voided') }})</span>}</td>
              <td>{{ at(movement.atUtc) }}</td>
              <td class="mono">{{ movement.accession ?? '—' }}</td>
              <td>{{ i18n.t('billing.method.' + movement.method) }}</td>
              <td>{{ movement.cashierName }}</td>
              <td class="num">{{ movement.kind === 'Refund' ? '−' : '' }}{{ money(movement.amount) }}</td>
            </tr>
          } @empty {
            <tr><td colspan="6" class="muted">{{ i18n.t('billing.doc.none') }}</td></tr>
          }
        </tbody>
      </table>
      @if (doc().movementsTruncated) {
        <p class="muted">{{ i18n.t('billing.closing.truncated') }}</p>
      }

      <div class="signatures">
        <div class="signature"><span class="line"></span><span>{{ i18n.t('billing.closing.handedOverBy') }}</span></div>
        <div class="signature"><span class="line"></span><span>{{ i18n.t('billing.closing.receivedBy') }}</span></div>
      </div>
    </app-document-frame>
  `,
  styleUrl: './billing-document.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClosingSheetDocumentComponent {
  protected readonly i18n = inject(I18nService);

  readonly doc = input.required<CashClosingSheetDocument>();

  protected readonly branch = computed(() => branchOf(this.doc().letterhead.branchNameEn, this.doc().letterhead.branchNameAr, this.i18n.lang()));

  protected money(value: number | null): string {
    return formatPrintMoney(value, this.i18n.lang());
  }

  protected at(value: string): string {
    return formatPrintDateTime(value, this.i18n.lang());
  }
}
