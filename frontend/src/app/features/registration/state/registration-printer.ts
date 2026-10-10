import { Injectable, inject } from '@angular/core';

import type { PriceQuoteDocument } from '../../../core/api/registration.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PrintService } from '../../../shared/print/print.service';
import { RegistrationApi } from '../data/registration.api';
import { JobOrderDocumentComponent, PriceQuoteDocumentComponent, RegistrationLabelsDocument, RegistrationReceiptDocument } from '../ui/registration-documents';

/**
 * Prints the module's documents through the print kit (MODULES.md §1 "Documents"): the data comes from the API
 * (`…/documents/labels`, `…/documents/job-order`, Billing's receipt), the rendering from the feature's document
 * components. Each method resolves after the print dialog was opened (or rejects with the API problem).
 */
@Injectable({ providedIn: 'root' })
export class RegistrationPrinter {
  private readonly api = inject(RegistrationApi);
  private readonly printer = inject(PrintService);
  private readonly i18n = inject(I18nService);

  /** Every live tube's label (`labelCopies` each), or one tube's; resolves with the number of labels before copies. */
  async labels(visitId: string, tubeId: string | null = null): Promise<number> {
    const document = await this.api.labels(visitId);
    const count = document.labels.filter((label) => tubeId === null || label.tubeId === tubeId).length;
    if (count > 0) {
      await this.printer.print({
        title: `${this.i18n.t('registration.labels.title')} ${document.accession}`,
        size: 'label-50x25',
        component: { type: RegistrationLabelsDocument, inputs: { document, tubeId, lang: this.i18n.lang() } },
      });
    }

    return count;
  }

  async jobOrder(visitId: string): Promise<void> {
    const document = await this.api.jobOrder(visitId);
    await this.printer.print({
      title: `${this.i18n.t('registration.jobOrder.title')} ${document.accession}`,
      size: 'A4',
      component: { type: JobOrderDocumentComponent, inputs: { document } },
    });
  }

  async receipt(paymentId: string): Promise<void> {
    const document = await this.api.receipt(paymentId);
    await this.printer.print({
      title: `${this.i18n.t('registration.receipt.title')} ${document.receiptNumber}`,
      size: 'A5',
      component: { type: RegistrationReceiptDocument, inputs: { document } },
    });
  }

  quote(document: PriceQuoteDocument): Promise<void> {
    return this.printer.print({
      title: this.i18n.t('registration.quote.documentTitle'),
      size: 'A4',
      component: { type: PriceQuoteDocumentComponent, inputs: { document } },
    });
  }
}
