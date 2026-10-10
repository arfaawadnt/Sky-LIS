import { Injectable, inject, signal } from '@angular/core';

import { I18nService } from '../../../core/i18n/i18n.service';
import { PrintService } from '../../../shared/print/print.service';
import { SamplesApi } from '../data/samples.api';
import { type LabelSize, SampleLabelsDocumentComponent } from '../ui/sample-labels.document';
import type { SampleLabelDocument } from '../../../core/api/samples.contracts';

/**
 * Prints barcode labels through the print kit (MODULES.md §1 "Documents"): the label documents come from the API
 * (`…/documents/label`, `…/visits/{id}/documents/labels`) and are rendered by {@link SampleLabelsDocumentComponent}.
 * The chosen label size is kept for the session (in memory only).
 */
@Injectable({ providedIn: 'root' })
export class SampleLabelPrinter {
  private readonly api = inject(SamplesApi);
  private readonly printer = inject(PrintService);
  private readonly i18n = inject(I18nService);

  readonly size = signal<LabelSize>('label-50x25');

  /** One sample's label(s); resolves with the sample number printed. */
  async printSample(sampleId: string): Promise<string> {
    const label = await this.api.label(sampleId);
    await this.printLabels([label], label.sampleNumber);
    return label.sampleNumber;
  }

  /** Every live sample of a visit; resolves with the number of labels (before copies). */
  async printVisit(visitId: string): Promise<number> {
    const document = await this.api.visitLabels(visitId);
    await this.printLabels(document.labels, document.accession);
    return document.labels.length;
  }

  printLabels(labels: readonly SampleLabelDocument[], reference: string): Promise<void> {
    const size = this.size();
    const lang = this.i18n.lang();
    return this.printer.print({
      title: `${this.i18n.t('samples.labels.title')} ${reference}`,
      size,
      component: { type: SampleLabelsDocumentComponent, inputs: { labels, size, lang } },
    });
  }
}
