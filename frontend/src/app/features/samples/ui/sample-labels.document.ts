import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import type { SampleLabelDocument } from '../../../core/api/samples.contracts';
import { Code128Component } from '../../../shared/print/code128.component';
import { CODE128, encodeCode128 } from '../../../shared/print/code128';
import { formatDateTime, pickName } from './sample-format';

export type LabelSize = 'label-50x25' | 'label-38x25';

export const LABEL_SIZES: readonly LabelSize[] = ['label-50x25', 'label-38x25'];

/** Usable width of a label after the `.print-page` padding of print.css (2 mm each side), and the barcode height wanted, in mm. */
const LABEL_GEOMETRY: Readonly<Record<LabelSize, { readonly widthMm: number; readonly barcodeMm: number; readonly fontPt: number }>> = {
  'label-50x25': { widthMm: 46, barcodeMm: 9.5, fontPt: 7 },
  'label-38x25': { widthMm: 34, barcodeMm: 9, fontPt: 6 },
};

const LINE = 'display:flex;gap:1.5mm;align-items:baseline;white-space:nowrap;overflow:hidden;';
const CLIP = 'overflow:hidden;text-overflow:ellipsis;white-space:nowrap;';

/**
 * Barcode labels of samples (CP-15, LDM §2.2 label content): unit letter, accession, date/time, patient number and
 * name, the sample number as Code 128, sample type · condition, branch and service names, "EXT" for a sample collected
 * outside the laboratory (LIS-CR-008 C-02); one `.print-page` per copy.
 * Printed through `PrintService` on the label page sizes; layout uses inline styles so the rendered HTML carries it
 * into the print frame unchanged.
 */
@Component({
  selector: 'app-sample-labels-document',
  imports: [Code128Component],
  template: `
    @for (label of pages(); track $index) {
      <div class="print-page sample-label" [style.font-size.pt]="geometry().fontPt" style="line-height:1.15;color:#000;">
        <div [style]="line">
          <b style="font-size:1.35em;">{{ label.unitCode ?? '' }}</b>
          <span style="font-weight:600;">{{ label.accession }}</span>
          @if (label.collectedOutside) {
            <!-- LIS-CR-008 C-02: collected outside the laboratory -->
            <b class="outside" style="border:0.25mm solid #000;padding:0 0.5mm;">EXT</b>
          }
          <span style="margin-inline-start:auto;">{{ when(label) }}</span>
        </div>
        <div [style]="clip" style="font-weight:600;">{{ patient(label) }}</div>
        <app-code128 [value]="label.sampleNumber" [height]="barcodeHeight(label)" [label]="label.sampleNumber" />
        <div [style]="clip">{{ typeAndBranch(label) }}</div>
        <div [style]="clip">{{ services(label) }}</div>
      </div>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SampleLabelsDocumentComponent {
  readonly labels = input.required<readonly SampleLabelDocument[]>();
  readonly size = input<LabelSize>('label-50x25');
  readonly lang = input('en');

  protected readonly line = LINE;
  protected readonly clip = CLIP;

  protected readonly geometry = computed(() => LABEL_GEOMETRY[this.size()]);

  /** Every label repeated by its copies (labelCopies parameter and the services' label counts). */
  protected readonly pages = computed(() => this.labels().flatMap((label) => Array.from({ length: Math.min(Math.max(1, label.copies), 10) }, () => label)));

  /** SVG units such that, stretched to the label width by print.css, the bars and text take the wanted height. */
  protected barcodeHeight(label: SampleLabelDocument): number {
    const { widthMm, barcodeMm } = this.geometry();
    try {
      const modules = encodeCode128(label.sampleNumber).modules + 2 * CODE128.quietZone;
      return Math.max(20, Math.round((modules * barcodeMm) / widthMm));
    } catch {
      return 30;
    }
  }

  protected when(label: SampleLabelDocument): string {
    return formatDateTime(label.labelAtUtc, this.lang());
  }

  protected patient(label: SampleLabelDocument): string {
    const name = pickName(label.patientName, label.patientNameAr, this.lang());
    return [label.patientNumber, name].filter((part): part is string => !!part).join(' · ');
  }

  protected typeAndBranch(label: SampleLabelDocument): string {
    const lang = this.lang();
    const type = pickName(label.sampleTypeNameEn, label.sampleTypeNameAr, lang);
    const condition = pickName(label.sampleConditionNameEn, label.sampleConditionNameAr, lang);
    const branch = pickName(label.branchNameEn, label.branchNameAr, lang);
    return [[type, condition].filter((part) => part.length > 0).join(' · '), branch].filter((part) => part.length > 0).join(' — ');
  }

  protected services(label: SampleLabelDocument): string {
    const lang = this.lang();
    return label.services.map((service) => service.code ?? pickName(service.nameEn, service.nameAr, lang)).filter((name) => name.length > 0).join(', ');
  }
}
