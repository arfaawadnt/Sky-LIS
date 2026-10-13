import { Injectable, inject } from '@angular/core';

import type { CollectionsReportDocument } from '../../../core/api/billing.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PrintService } from '../../../shared/print/print.service';
import { BillingApi } from '../data/billing.api';
import { ClosingSheetDocumentComponent } from '../ui/documents/closing-sheet-document.component';
import { CollectionsReportDocumentComponent } from '../ui/documents/collections-report-document.component';
import { InvoiceDocumentComponent } from '../ui/documents/invoice-document.component';
import { ReceiptDocumentComponent } from '../ui/documents/receipt-document.component';
import { RefundReceiptDocumentComponent } from '../ui/documents/refund-receipt-document.component';

/**
 * The documents of the module (MODULES.md §1 "Documents"): the API returns their data, the print kit renders the
 * components and opens the print dialog. A failure reaches the caller (the page shows a toast).
 */
@Injectable({ providedIn: 'root' })
export class BillingPrinter {
  private readonly api = inject(BillingApi);
  private readonly print = inject(PrintService);
  private readonly i18n = inject(I18nService);

  async receipt(paymentId: string): Promise<void> {
    const doc = await this.api.paymentReceipt(paymentId);
    await this.print.print({ title: `${this.i18n.t('billing.doc.receiptTitle')} ${doc.receiptNumber}`, size: 'A5', component: { type: ReceiptDocumentComponent, inputs: { doc } } });
  }

  /** Several receipts in one print job (pay-all), one page each. */
  async receipts(paymentIds: readonly string[]): Promise<void> {
    const [first] = paymentIds;
    if (first === undefined) {
      return;
    }

    if (paymentIds.length === 1) {
      await this.receipt(first);
      return;
    }

    const documents = await Promise.all(paymentIds.map((id) => this.api.paymentReceipt(id)));
    const html = documents.map((doc) => `<div class="print-page">${this.print.render({ type: ReceiptDocumentComponent, inputs: { doc } })}</div>`).join('\n');
    await this.print.print({ title: this.i18n.t('billing.doc.receiptsTitle'), size: 'A5', html });
  }

  async invoice(visitId: string): Promise<void> {
    const doc = await this.api.invoice(visitId);
    await this.print.print({ title: `${this.i18n.t('billing.doc.invoiceTitle')} ${doc.accession}`, size: 'A4', component: { type: InvoiceDocumentComponent, inputs: { doc } } });
  }

  async refundReceipt(refundId: string): Promise<void> {
    const doc = await this.api.refundReceipt(refundId);
    await this.print.print({ title: `${this.i18n.t('billing.doc.refundTitle')} ${doc.number}`, size: 'A5', component: { type: RefundReceiptDocumentComponent, inputs: { doc } } });
  }

  async closingSheet(closingId: string): Promise<void> {
    const doc = await this.api.closingSheet(closingId);
    await this.print.print({ title: `${this.i18n.t('billing.doc.closingTitle')} ${doc.number}`, size: 'A4', component: { type: ClosingSheetDocumentComponent, inputs: { doc } } });
  }

  /** The collections report the dialog already loaded (A4; change request 2026-10-13, item 2). */
  async collectionsReport(doc: CollectionsReportDocument): Promise<void> {
    await this.print.print({ title: `${this.i18n.t('billing.doc.collectionsTitle')} ${doc.fromDay}${doc.toDay === doc.fromDay ? '' : ` – ${doc.toDay}`}`, size: 'A4', component: { type: CollectionsReportDocumentComponent, inputs: { doc } } });
  }
}
