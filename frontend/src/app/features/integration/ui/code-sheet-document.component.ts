import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';

import type { AnalyserCodeSheetDocument } from '../../../core/api/integration.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { DocumentFrameComponent } from '../../../shared/print/document-frame.component';

/** The printable analyser code sheet (SRS documents): every active mapping and sample code of one analyser. */
@Component({
  selector: 'app-integration-code-sheet-document',
  imports: [DocumentFrameComponent],
  template: `
    <app-document-frame
      [labNameEn]="labNameEn()"
      [labNameAr]="labNameAr()"
      [title]="i18n.t('integration.codeSheet.title')"
      [subtitle]="doc().analyserCode + ' · ' + (i18n.lang() === 'ar' ? doc().analyserNameAr : doc().analyserNameEn)"
      [reference]="doc().connectionSummary"
      [printedBy]="doc().generatedBy ?? ''"
      [printedAt]="printedAt()"
    >
      <p class="small">{{ i18n.t('integration.field.mode') }}: {{ i18n.t('integration.mode.' + doc().mode) }} @if (doc().siteAgentName) { · {{ i18n.t('integration.field.siteAgent') }}: {{ doc().siteAgentName }} }</p>
      <table>
        <thead>
          <tr>
            <th>{{ i18n.t('integration.mapping.analyserCode') }}</th>
            <th>{{ i18n.t('integration.mapping.hostCode') }}</th>
            <th>{{ i18n.t('integration.mapping.test') }}</th>
            <th>{{ i18n.t('integration.mapping.unit') }}</th>
            <th class="num">{{ i18n.t('integration.mapping.resultSelection') }}</th>
            <th class="num">{{ i18n.t('integration.mapping.slope') }}</th>
            <th class="num">{{ i18n.t('integration.mapping.intercept') }}</th>
            <th>{{ i18n.t('integration.mapping.autoSend') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (line of doc().lines; track line.analyserCode) {
            <tr class="avoid-break">
              <td class="mono">{{ line.analyserCode }}</td>
              <td class="mono">{{ line.hostCode ?? '' }}</td>
              <td>{{ line.testCode ?? '' }} {{ i18n.lang() === 'ar' ? (line.testNameAr ?? '') : (line.testNameEn ?? '') }}</td>
              <td>{{ line.unitCode ?? '' }}</td>
              <td class="num">{{ line.resultSelection }}</td>
              <td class="num">{{ line.slope }}</td>
              <td class="num">{{ line.intercept }}</td>
              <td>{{ line.autoSend ? i18n.t('integration.yes') : i18n.t('integration.no') }}</td>
            </tr>
          }
        </tbody>
      </table>
      @if (doc().sampleCodes.length > 0) {
        <h3>{{ i18n.t('integration.samples.title') }}</h3>
        <table>
          <thead>
            <tr>
              <th>{{ i18n.t('integration.samples.sampleType') }}</th>
              <th>{{ i18n.t('integration.samples.code') }}</th>
            </tr>
          </thead>
          <tbody>
            @for (code of doc().sampleCodes; track code.code) {
              <tr>
                <td>{{ code.sampleTypeCode ?? '' }} {{ i18n.lang() === 'ar' ? (code.sampleTypeNameAr ?? '') : (code.sampleTypeNameEn ?? '') }}</td>
                <td class="mono">{{ code.code }}</td>
              </tr>
            }
          </tbody>
        </table>
      }
    </app-document-frame>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CodeSheetDocumentComponent {
  protected readonly i18n = inject(I18nService);

  readonly doc = input.required<AnalyserCodeSheetDocument>();
  readonly labNameEn = input('Sky LIS');
  readonly labNameAr = input('سكاي LIS');

  protected readonly printedAt = computed(() => new Date(this.doc().generatedAtUtc).toLocaleString(this.i18n.lang() === 'ar' ? 'ar-EG' : 'en-GB'));
}
