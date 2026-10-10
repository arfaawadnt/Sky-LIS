import { Injectable, inject } from '@angular/core';

import type { LaboratoryReportDocument, WorklistDocument } from '../../../core/api/results.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PrintService } from '../../../shared/print/print.service';
import { ReportDocumentComponent } from '../ui/report.document';
import { WorklistDocumentComponent } from '../ui/worklist.document';

/**
 * The documents of the module (MODULES.md §1 "Documents"): the API returns their data (the facade fetches it, after
 * `POST …/print` for an official copy), the print kit renders the components and opens the print dialog.
 */
@Injectable({ providedIn: 'root' })
export class ResultsPrinter {
  private readonly print = inject(PrintService);
  private readonly i18n = inject(I18nService);

  /** The laboratory report of one unit, in the report's own language and direction. */
  report(doc: LaboratoryReportDocument): Promise<void> {
    const lang = doc.language === 'ar' ? 'ar' : 'en';
    const title = lang === 'ar' ? doc.titleAr : doc.titleEn;
    return this.print.print({ title: `${title} ${doc.visit.accession}`, size: 'A4', lang, dir: lang === 'ar' ? 'rtl' : 'ltr', component: { type: ReportDocumentComponent, inputs: { doc } } });
  }

  /** The bench list of the current worklist filter. */
  worklist(doc: WorklistDocument): Promise<void> {
    return this.print.print({ title: this.i18n.t('results.worklist.documentTitle'), size: 'A4', landscape: true, component: { type: WorklistDocumentComponent, inputs: { doc } } });
  }
}
