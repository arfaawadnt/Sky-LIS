import { ChangeDetectionStrategy, Component, input } from '@angular/core';

import type { ReportLine } from '../../../../../core/api/results.contracts';
import { ReportTextsBase } from './report-design-base';

/**
 * `<app-report-rich-line [line]="…" [texts]="…" />`: a culture (isolates with their sensitivity table) or a formatted
 * text (titled paragraphs) printed across the results table, with the line's comment — the same content in every design.
 */
@Component({
  selector: 'app-report-rich-line',
  template: `
    <b>{{ name(line().nameEn, line().nameAr) }}</b>
    @if (line().testType === 'Culture' && line().culture; as culture) {
      @if (culture.noGrowth) {
        <div>{{ t('results.report.noGrowth') }}</div>
      }
      @for (isolate of culture.isolates; track $index) {
        <div class="isolate">
          <b>{{ name(isolate.nameEn, isolate.nameAr) }}</b>
          @if (isolate.quantity) {
            <span> · {{ isolate.quantity }}</span>
          }
          @if (isolate.comment) {
            <div class="muted">{{ isolate.comment }}</div>
          }
          @if (isolate.sensitivities.length > 0) {
            <table class="sensitivities">
              <thead>
                <tr>
                  <th scope="col">{{ t('results.report.antibiotic') }}</th>
                  <th scope="col">{{ t('results.report.interpretation') }}</th>
                  <th scope="col">{{ t('results.report.mic') }}</th>
                </tr>
              </thead>
              <tbody>
                @for (sensitivity of isolate.sensitivities; track sensitivity.antibioticId) {
                  <tr>
                    <td>{{ name(sensitivity.nameEn, sensitivity.nameAr) }} ({{ sensitivity.abbreviation }})</td>
                    <td>{{ sensitivity.interpretation }}</td>
                    <td>{{ sensitivity.mic ?? '' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          }
        </div>
      }
    } @else if (line().paragraphs; as paragraphs) {
      @for (paragraph of paragraphs; track $index) {
        <div class="paragraph">
          @if (paragraph.title) {
            <b>{{ paragraph.title }}</b>
          }
          <div class="text">{{ paragraph.text }}</div>
        </div>
      }
    }
    @if (line().comment) {
      <div class="comment">{{ line().comment }}</div>
    }
  `,
  styles: `
    :host { display: block; }
    .isolate, .paragraph { margin: 4px 0; }
    .sensitivities { margin: 4px 0 0; width: auto; min-width: 60%; }
    .paragraph .text, .comment { white-space: pre-wrap; }
    .comment { margin-top: 2px; font-style: italic; }
    .muted { color: #555; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportRichLineComponent extends ReportTextsBase {
  readonly line = input.required<ReportLine>();
}
