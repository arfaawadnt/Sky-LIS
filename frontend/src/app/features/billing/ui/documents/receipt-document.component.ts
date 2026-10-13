import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';

import type { PaymentReceiptDocument } from '../../../../core/api/billing.contracts';
import { I18nService } from '../../../../core/i18n/i18n.service';
import { localizedPick } from '../../../../shared/forms/localized-text';
import { Code128Component } from '../../../../shared/print/code128.component';
import { DocumentFrameComponent } from '../../../../shared/print/document-frame.component';
import { formatPrintDateTime, formatPrintMoney } from '../billing-format';

/** The payment receipt (A5, CP-23): one per payment, reprintable; a voided payment prints with a VOID banner. */
@Component({
  selector: 'app-billing-receipt-document',
  imports: [DocumentFrameComponent, Code128Component],
  template: `
    <app-document-frame
      [labNameEn]="doc().letterhead.labNameEn"
      [labNameAr]="doc().letterhead.labNameAr"
      [branch]="branch()"
      [branchEn]="doc().letterhead.branchNameEn"
      [branchAr]="doc().letterhead.branchNameAr"
      [title]="i18n.t('billing.doc.receiptTitle')"
      [reference]="doc().receiptNumber"
      [printedBy]="doc().printedBy"
      [printedAt]="at(doc().printedAtUtc)"
    >
      <img documentLogo src="/brand/sky-lis-logo.png" alt="" height="36" />
      @if (doc().status === 'Voided') {
        <p class="void-banner">{{ i18n.t('billing.doc.voided') }} · {{ doc().voidReason }}</p>
      }
      <table class="facts">
        <tbody>
          <tr><th scope="row">{{ i18n.t('billing.doc.date') }}</th><td>{{ at(doc().paidAtUtc) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.field.accession') }}</th><td class="mono">{{ doc().accession }}</td></tr>
          <tr>
            <th scope="row">{{ i18n.t('billing.field.patient') }}</th>
            <td>{{ doc().patientName ?? '—' }} @if (doc().patientNumber; as number) {<span class="muted mono">({{ number }})</span>}</td>
          </tr>
          <tr><th scope="row">{{ i18n.t('billing.field.method') }}</th><td>{{ i18n.t('billing.method.' + doc().method) }}</td></tr>
          @if (doc().reference; as reference) {
            <tr><th scope="row">{{ i18n.t('billing.field.reference') }}</th><td class="mono">{{ reference }}</td></tr>
          }
          <tr><th scope="row">{{ i18n.t('billing.field.treasury') }}</th><td>{{ doc().treasuryCode }} · {{ treasury() }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.field.cashier') }}</th><td>{{ doc().cashierName }}</td></tr>
        </tbody>
      </table>
      <p class="amount"><span>{{ i18n.t('billing.doc.amountPaid') }}</span><b>{{ money(doc().amount) }} {{ doc().currency }}</b></p>
      <table>
        <caption>{{ i18n.t('billing.doc.balanceAsOfPrinting') }}</caption>
        <tbody>
          <tr><th scope="row">{{ i18n.t('billing.account.patientShare') }}</th><td class="num">{{ money(doc().patientShareTotal) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.account.paid') }}</th><td class="num">{{ money(doc().paid) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.account.refunded') }}</th><td class="num">{{ money(doc().refunded) }}</td></tr>
          <tr class="totals"><th scope="row">{{ i18n.t('billing.account.remaining') }}</th><td class="num">{{ money(doc().remaining) }}</td></tr>
        </tbody>
      </table>
      <div class="code"><app-code128 [value]="doc().receiptNumber" [height]="32" /></div>
    </app-document-frame>
  `,
  styleUrl: './billing-document.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReceiptDocumentComponent {
  protected readonly i18n = inject(I18nService);

  readonly doc = input.required<PaymentReceiptDocument>();

  protected readonly branch = computed(() => branchOf(this.doc().letterhead.branchNameEn, this.doc().letterhead.branchNameAr, this.i18n.lang()));

  protected readonly treasury = computed(() => localizedPick({ en: this.doc().treasuryNameEn, ar: this.doc().treasuryNameAr }, this.i18n.lang()));

  protected money(value: number): string {
    return formatPrintMoney(value, this.i18n.lang());
  }

  protected at(value: string): string {
    return formatPrintDateTime(value, this.i18n.lang());
  }
}

/** The branch line of a letterhead in the reader's language, or null when the branch is unknown. */
export function branchOf(nameEn: string | null, nameAr: string | null, lang: string): string | null {
  const picked = localizedPick({ en: nameEn ?? '', ar: nameAr ?? '' }, lang);
  return picked === '' ? null : picked;
}
