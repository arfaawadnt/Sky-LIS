import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';

import type { InvoiceDocument, InvoiceLine } from '../../../../core/api/billing.contracts';
import { I18nService } from '../../../../core/i18n/i18n.service';
import { localizedPick } from '../../../../shared/forms/localized-text';
import { Code128Component } from '../../../../shared/print/code128.component';
import { DocumentFrameComponent } from '../../../../shared/print/document-frame.component';
import { formatDateTime, formatMoney } from '../billing-format';
import { branchOf } from './receipt-document.component';

/** The visit invoice (A4, CP-23 "receipts and invoice"): billed lines with shares and discounts, payments, refunds and the balance. */
@Component({
  selector: 'app-billing-invoice-document',
  imports: [DocumentFrameComponent, Code128Component],
  template: `
    <app-document-frame
      [labNameEn]="doc().letterhead.labNameEn"
      [labNameAr]="doc().letterhead.labNameAr"
      [branch]="branch()"
      [branchEn]="doc().letterhead.branchNameEn"
      [branchAr]="doc().letterhead.branchNameAr"
      [title]="i18n.t('billing.doc.invoiceTitle')"
      [reference]="doc().accession"
      [printedBy]="doc().printedBy"
      [printedAt]="at(doc().printedAtUtc)"
    >
      <img documentLogo src="/brand/sky-lis-logo.png" alt="" height="40" />
      <div class="split">
        <table class="facts">
          <tbody>
            <tr><th scope="row">{{ i18n.t('billing.field.accession') }}</th><td class="mono">{{ doc().accession }}</td></tr>
            <tr><th scope="row">{{ i18n.t('billing.doc.registeredAt') }}</th><td>{{ at(doc().registeredAtUtc) }}</td></tr>
          </tbody>
        </table>
        <table class="facts">
          <tbody>
            <tr>
              <th scope="row">{{ i18n.t('billing.field.patient') }}</th>
              <td>{{ doc().patientName ?? '—' }} @if (doc().patientNumber; as number) {<span class="muted mono">({{ number }})</span>}</td>
            </tr>
            <tr><th scope="row">{{ i18n.t('billing.field.payer') }}</th><td>{{ payer() }}</td></tr>
          </tbody>
        </table>
      </div>

      <table>
        <caption>{{ i18n.t('billing.doc.services') }}</caption>
        <thead>
          <tr>
            <th scope="col">{{ i18n.t('billing.doc.service') }}</th>
            <th scope="col" class="num">{{ i18n.t('billing.account.totalListPrice') }}</th>
            <th scope="col" class="num">{{ i18n.t('billing.account.discount') }}</th>
            <th scope="col" class="num">{{ i18n.t('billing.account.patientShare') }}</th>
            <th scope="col" class="num">{{ i18n.t('billing.account.payerShare') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (line of doc().lines; track line.lineId) {
            <tr>
              <td>
                @if (line.serviceCode; as code) {<span class="mono">{{ code }}</span> · }{{ serviceName(line) }}
                @if (line.status === 'Reserved') {<span class="muted"> ({{ i18n.t('billing.doc.reserved') }})</span>}
              </td>
              <td class="num">{{ money(line.listPrice) }}</td>
              <td class="num">{{ money(line.discount) }}</td>
              <td class="num">{{ money(line.patientShare) }}</td>
              <td class="num">{{ money(line.payerShare) }}</td>
            </tr>
          }
          <tr class="totals">
            <th scope="row">{{ i18n.t('billing.doc.total') }} ({{ doc().currency }})</th>
            <td class="num">{{ money(doc().totalListPrice) }}</td>
            <td class="num">{{ money(doc().discountTotal) }}</td>
            <td class="num">{{ money(doc().patientShareTotal) }}</td>
            <td class="num">{{ money(doc().payerShareTotal) }}</td>
          </tr>
        </tbody>
      </table>

      <div class="split">
        <table>
          <caption>{{ i18n.t('billing.doc.payments') }}</caption>
          <tbody>
            @for (payment of doc().payments; track payment.number) {
              <tr><td class="mono">{{ payment.number }}</td><td>{{ at(payment.atUtc) }}</td><td>{{ i18n.t('billing.method.' + payment.method) }}</td><td class="num">{{ money(payment.amount) }}</td></tr>
            } @empty {
              <tr><td colspan="4" class="muted">{{ i18n.t('billing.doc.none') }}</td></tr>
            }
          </tbody>
        </table>
        <table>
          <caption>{{ i18n.t('billing.doc.refunds') }}</caption>
          <tbody>
            @for (refund of doc().refunds; track refund.number) {
              <tr><td class="mono">{{ refund.number }}</td><td>{{ at(refund.atUtc) }}</td><td>{{ i18n.t('billing.method.' + refund.method) }}</td><td class="num">{{ money(refund.amount) }}</td></tr>
            } @empty {
              <tr><td colspan="4" class="muted">{{ i18n.t('billing.doc.none') }}</td></tr>
            }
          </tbody>
        </table>
      </div>

      <table class="avoid-break">
        <caption>{{ i18n.t('billing.doc.balance') }} ({{ doc().currency }})</caption>
        <tbody>
          <tr><th scope="row">{{ i18n.t('billing.account.patientShare') }}</th><td class="num">{{ money(doc().patientShareTotal) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.account.paid') }}</th><td class="num">{{ money(doc().paid) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.account.refunded') }}</th><td class="num">{{ money(doc().refunded) }}</td></tr>
          <tr class="totals"><th scope="row">{{ i18n.t('billing.account.remaining') }}</th><td class="num">{{ money(doc().remaining) }}</td></tr>
        </tbody>
      </table>
      <div class="code"><app-code128 [value]="doc().accession" [height]="32" /></div>
    </app-document-frame>
  `,
  styleUrl: './billing-document.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class InvoiceDocumentComponent {
  protected readonly i18n = inject(I18nService);

  readonly doc = input.required<InvoiceDocument>();

  protected readonly branch = computed(() => branchOf(this.doc().letterhead.branchNameEn, this.doc().letterhead.branchNameAr, this.i18n.lang()));

  protected readonly payer = computed(() => {
    const name = localizedPick({ en: this.doc().payerNameEn ?? '', ar: this.doc().payerNameAr ?? '' }, this.i18n.lang());
    return name === '' ? this.i18n.t('billing.doc.cashPatient') : name;
  });

  protected serviceName(line: InvoiceLine): string {
    const name = localizedPick({ en: line.serviceNameEn ?? '', ar: line.serviceNameAr ?? '' }, this.i18n.lang());
    return name === '' ? this.i18n.t('billing.doc.service') : name;
  }

  protected money(value: number): string {
    return formatMoney(value, this.i18n.lang());
  }

  protected at(value: string): string {
    return formatDateTime(value, this.i18n.lang());
  }
}
