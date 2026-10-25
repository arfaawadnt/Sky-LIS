import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { reportDesignOf } from '../../../core/api/report-designs';
import type { LaboratoryReportDocument } from '../../../core/api/results.contracts';
import type { DocumentTexts } from '../../../shared/print/document-texts';
import type { ReportTemplateImageUrls } from '../../../shared/print/report-template-images';
import { ReportClassicComponent } from './documents/designs/report-classic.component';
import { ReportCompactComponent } from './documents/designs/report-compact.component';
import { ReportCumulativeComponent } from './documents/designs/report-cumulative.component';
import { ReportLetterheadComponent } from './documents/designs/report-letterhead.component';
import { ReportModernComponent } from './documents/designs/report-modern.component';

/**
 * The laboratory report of one report unit (A4 or Letter; SRS CP-13/CP-19, LDM §5.7, BR-09/BR-20), dispatched to one of
 * five designs (LIS-CR-010, reviewer point C-14) by `doc.design`, which the server resolves contract → report template →
 * laboratory parameter `reportDesign`: Classic (the report before the change), Modern, Compact, Letterhead (pre-printed
 * paper) and Cumulative (previous values). Every design shares one view model (`ReportDesignBase`) and the same inputs, so
 * `PrintService`, the case page and the template preview (`REPORT_DOCUMENT_RENDERER`) use this component unchanged.
 * The document language follows the report (`doc.language`) through `texts`, not the screen; numbers and dates keep Latin digits.
 */
@Component({
  selector: 'app-results-report-document',
  imports: [ReportClassicComponent, ReportModernComponent, ReportCompactComponent, ReportLetterheadComponent, ReportCumulativeComponent],
  template: `
    @switch (design()) {
      @case ('Modern') {
        <app-report-modern [doc]="doc()" [images]="images()" [texts]="texts()" />
      }
      @case ('Compact') {
        <app-report-compact [doc]="doc()" [images]="images()" [texts]="texts()" />
      }
      @case ('Letterhead') {
        <app-report-letterhead [doc]="doc()" [images]="images()" [texts]="texts()" />
      }
      @case ('Cumulative') {
        <app-report-cumulative [doc]="doc()" [images]="images()" [texts]="texts()" />
      }
      @default {
        <app-report-classic [doc]="doc()" [images]="images()" [texts]="texts()" />
      }
    }
  `,
  styles: `
    :host {
      display: block;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportDocumentComponent {
  readonly doc = input.required<LaboratoryReportDocument>();

  /**
   * The template's logo and signature as object URLs (`ReportTemplateImageLoader`, validation F-02): the logo replaces
   * the Sky LIS mark in the letterhead (no logo on the Letterhead design); the signature image is printed above the
   * verifier's name on a verified, final report only (never on a preliminary one).
   */
  readonly images = input<ReportTemplateImageUrls | null>(null);

  /**
   * The report's texts in its own language (`doc.language`: the result-language parameter, or the template's language),
   * loaded by the caller (`DocumentTextsLoader`; validation v0.5.0 F-12). Captions, frame language and direction all
   * follow them; without them the whole document follows the screen, so the two never mix.
   */
  readonly texts = input<DocumentTexts | null>(null);

  /** The resolved design; a document without one (an older server, a fixture) prints Classic. */
  protected readonly design = computed(() => reportDesignOf(this.doc().design));
}
