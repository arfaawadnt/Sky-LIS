import { Injectable, inject } from '@angular/core';

import type { LaboratoryReportDocument, WorklistDocument } from '../../../core/api/results.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { type DocumentTexts, DocumentTextsLoader } from '../../../shared/print/document-texts';
import { PrintService } from '../../../shared/print/print.service';
import { type LoadedTemplateImages, ReportTemplateImageLoader } from '../../../shared/print/report-template-images';
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
  private readonly templateImages = inject(ReportTemplateImageLoader);
  private readonly documentTexts = inject(DocumentTextsLoader);

  /**
   * The laboratory report of one unit, in the report's own language and direction, with the logo and the signature of
   * its report template (validation F-02); the images' object URLs are released once the print dialog closes.
   */
  async report(doc: LaboratoryReportDocument): Promise<void> {
    const texts = await this.reportTexts(doc);
    const title = texts.lang === 'ar' ? doc.titleAr : doc.titleEn;
    const images = await this.reportImages(doc);
    try {
      await this.print.print({ title: `${title} ${doc.visit.accession}`, size: 'A4', lang: texts.lang, dir: texts.dir, component: { type: ReportDocumentComponent, inputs: { doc, images, texts } } });
    } finally {
      images.release();
    }
  }

  /**
   * The captions of a report in the report's language (validation v0.5.0 F-12): the frame, its direction and every caption
   * follow `doc.language`, whatever the screen's language (for the print and the on-screen preview).
   */
  reportTexts(doc: LaboratoryReportDocument): Promise<DocumentTexts> {
    return this.documentTexts.load(doc.language === 'ar' ? 'ar' : 'en', ['results', 'shared']);
  }

  /** The template images of a report (for the print and the on-screen preview); the caller releases them. */
  reportImages(doc: LaboratoryReportDocument): Promise<LoadedTemplateImages> {
    return this.templateImages.load(doc.template);
  }

  /** The bench list of the current worklist filter. */
  worklist(doc: WorklistDocument): Promise<void> {
    return this.print.print({ title: this.i18n.t('results.worklist.documentTitle'), size: 'A4', landscape: true, component: { type: WorklistDocumentComponent, inputs: { doc } } });
  }
}
