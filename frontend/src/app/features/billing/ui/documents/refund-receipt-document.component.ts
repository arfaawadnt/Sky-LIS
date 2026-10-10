import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';

import type { RefundReceiptDocument } from '../../../../core/api/billing.contracts';
import { I18nService } from '../../../../core/i18n/i18n.service';
import { localizedPick } from '../../../../shared/forms/localized-text';
import { Code128Component } from '../../../../shared/print/code128.component';
import { DocumentFrameComponent } from '../../../../shared/print/document-frame.component';
import { formatDateTime, formatMoney } from '../billing-format';
import { branchOf } from './receipt-document.component';

/** The refund receipt (A5, CP-24; LDM §10.8 "for patient signature"): amount, method, reason and the signature boxes. */
@Component({
  selector: 'app-billing-refund-receipt-document',
  imports: [DocumentFrameComponent, Code128Component],
  template: `
    <app-document-frame
      [labNameEn]="doc().letterhead.labNameEn"
      [labNameAr]="doc().letterhead.labNameAr"
      [branch]="branch()"
      [title]="i18n.t('billing.doc.refundTitle')"
      [reference]="doc().number"
      [printedBy]="doc().printedBy"
      [printedAt]="at(doc().printedAtUtc)"
    >
      <img documentLogo src="/brand/sky-lis-logo.png" alt="" height="36" />
      <table class="facts">
        <tbody>
          <tr><th scope="row">{{ i18n.t('billing.doc.date') }}</th><td>{{ at(doc().refundedAtUtc) }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.field.accession') }}</th><td class="mono">{{ doc().accession }}</td></tr>
          <tr>
            <th scope="row">{{ i18n.t('billing.field.patient') }}</th>
            <td>{{ doc().patientName ?? '—' }} @if (doc().patientNumber; as number) {<span class="muted mono">({{ number }})</span>}</td>
          </tr>
          <tr><th scope="row">{{ i18n.t('billing.field.method') }}</th><td>{{ i18n.t('billing.method.' + doc().method) }}</td></tr>
          @if (doc().reference; as reference) {
            <tr><th scope="row">{{ i18n.t('billing.field.reference') }}</th><td class="mono">{{ reference }}</td></tr>
          }
          <tr><th scope="row">{{ i18n.t('billing.field.reason') }}</th><td>{{ reason() }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.field.treasury') }}</th><td>{{ doc().treasuryCode }} · {{ treasury() }}</td></tr>
          <tr><th scope="row">{{ i18n.t('billing.field.cashier') }}</th><td>{{ doc().cashierName }}</td></tr>
        </tbody>
      </table>
      @if (doc().note; as note) {
        <p class="note">{{ note }}</p>
      }
      <p class="amount"><span>{{ i18n.t('billing.doc.amountRefunded') }}</span><b>{{ money(doc().amount) }} {{ doc().currency }}</b></p>
      <p class="muted">{{ i18n.t('billing.doc.refundAcknowledgement') }}</p>
      <div class="signatures">
        <div class="signature"><span class="line"></span><span>{{ i18n.t('billing.doc.patientSignature') }}</span></div>
        <div class="signature"><span class="line"></span><span>{{ i18n.t('billing.doc.cashierSignature') }}</span></div>
      </div>
      <div class="code"><app-code128 [value]="doc().number" [height]="28" /></div>
    </app-document-frame>
  `,
  styleUrl: './billing-document.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RefundReceiptDocumentComponent {
  protected readonly i18n = inject(I18nService);

  readonly doc = input.required<RefundReceiptDocument>();

  protected readonly branch = computed(() => branchOf(this.doc().letterhead.branchNameEn, this.doc().letterhead.branchNameAr, this.i18n.lang()));

  protected readonly treasury = computed(() => localizedPick({ en: this.doc().treasuryNameEn, ar: this.doc().treasuryNameAr }, this.i18n.lang()));

  protected readonly reason = computed(() => {
    const name = localizedPick({ en: this.doc().reasonNameEn ?? '', ar: this.doc().reasonNameAr ?? '' }, this.i18n.lang());
    return name === '' ? '—' : name;
  });

  protected money(value: number): string {
    return formatMoney(value, this.i18n.lang());
  }

  protected at(value: string): string {
    return formatDateTime(value, this.i18n.lang());
  }
}
