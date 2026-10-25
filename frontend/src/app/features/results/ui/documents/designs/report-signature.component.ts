import { ChangeDetectionStrategy, Component, input } from '@angular/core';

import type { LaboratoryReportDocument } from '../../../../../core/api/results.contracts';
import { ReportTextsBase } from './report-design-base';

/**
 * `<app-report-signature [doc]="…" [signatureUrl]="…" [texts]="…" />`: the verifier's electronic signature (with the
 * template's signature image on a final report), and the amendment, reprint and print-list notes — shared by the designs.
 */
@Component({
  selector: 'app-report-signature',
  template: `
    <div class="signature">
      <div>
        @if (doc().signature.verifiedByName; as verifier) {
          @if (signatureUrl(); as url) {
            <img class="template-signature" [src]="url" alt="" />
          }
          <b>{{ verifier }}</b>
          <div class="muted small">{{ t('results.report.verifiedBy') }} · {{ at(doc().signature.verifiedAtUtc) }} · {{ t('results.report.eSignature') }}</div>
        } @else {
          <div class="muted small">{{ t('results.report.notVerified') }}</div>
        }
      </div>
      <div class="muted small">
        @if (doc().amendmentCount > 0) {
          <div>{{ tc('results.report.amended', doc().amendmentCount) }}</div>
        }
        @if (doc().printCount > 1) {
          <div>{{ tc('results.report.reprint', doc().printCount) }}</div>
        }
        @if (doc().printListNumber; as list) {
          <div>{{ t('results.report.printList') }}: <span class="mono">{{ list }}</span></div>
        }
      </div>
    </div>
  `,
  styles: `
    :host { display: block; }
    .signature {
      display: flex;
      justify-content: space-between;
      gap: 12px;
      margin-top: 18px;
      padding-top: 6px;
      border-top: 1px solid #999;
      page-break-inside: avoid;
    }
    .template-signature { display: block; max-width: 180px; max-height: 60px; margin-bottom: 2px; object-fit: contain; }
    .mono { font-family: 'Cascadia Mono', Consolas, monospace; }
    .muted { color: #555; }
    .small { font-size: 10px; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportSignatureComponent extends ReportTextsBase {
  readonly doc = input.required<LaboratoryReportDocument>();

  readonly signatureUrl = input<string | null>(null);
}
