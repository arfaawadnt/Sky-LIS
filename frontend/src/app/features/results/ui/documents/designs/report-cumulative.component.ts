import { ChangeDetectionStrategy, Component } from '@angular/core';

import type { ReportLine, ReportPreviousValue } from '../../../../../core/api/results.contracts';
import { flagSymbol } from '../../result-format';
import { DocumentFrameComponent } from '../../../../../shared/print/document-frame.component';
import { QrCodeComponent } from '../../../../../shared/print/qr-code.component';
import { ReportDesignBase } from './report-design-base';
import { CUMULATIVE_COLUMNS, previousColumns, printDay } from './report-design-format';
import { ReportRichLineComponent } from './report-rich-line.component';
import { ReportSignatureComponent } from './report-signature.component';

/**
 * Design 5 — Cumulative (LIS-CR-010): the classic letterhead and facts, and beside each result the patient's last three
 * signed values of the same test from earlier visits (newest first, with their day), sent by the server in
 * `ReportLine.previous`. Cultures and formatted texts print across the table without history.
 */
@Component({
  selector: 'app-report-cumulative',
  imports: [DocumentFrameComponent, QrCodeComponent, ReportRichLineComponent, ReportSignatureComponent],
  template: `
    <div class="print-page report design-cumulative" [attr.dir]="dir()" [attr.lang]="lang()">
      <app-document-frame
        [labNameEn]="doc().letterhead.labNameEn"
        [labNameAr]="doc().letterhead.labNameAr"
        [branch]="branch()"
        [branchEn]="doc().letterhead.branchNameEn"
        [branchAr]="doc().letterhead.branchNameAr"
        [title]="title()"
        [reference]="doc().visit.accession"
        [subtitle]="subtitle()"
        [printedBy]="doc().generatedBy"
        [printedAt]="at(doc().generatedAtUtc)"
        [texts]="texts()"
      >
        @if (images()?.logoUrl; as logoUrl) {
          <img documentLogo class="logo" [src]="logoUrl" alt="" />
        } @else {
          <img documentLogo src="/brand/sky-lis-logo.png" alt="" height="36" />
        }
        @if (doc().isPreview) {
          <p class="banner">{{ t('results.report.preview') }}</p>
        }
        @if (doc().isPreliminary) {
          <p class="banner">{{ t('results.report.preliminary') }}</p>
        }
        <div class="header">
          <table class="facts">
            <tbody>
              <tr>
                <th scope="row">{{ t('results.report.patient') }}</th><td>{{ patientName() }}</td>
                <th scope="row">{{ t('results.report.patientNumber') }}</th><td class="mono">{{ doc().patient.number ?? '—' }}</td>
              </tr>
              <tr>
                <th scope="row">{{ t('results.report.ageSex') }}</th><td>{{ age() }} · {{ gender() }}</td>
                <th scope="row">{{ t('results.report.registeredAt') }}</th><td>{{ at(doc().visit.registeredAtUtc) }}</td>
              </tr>
              <tr>
                <th scope="row">{{ t('results.report.doctor') }}</th><td>{{ name(doc().visit.doctorNameEn, doc().visit.doctorNameAr) }}</td>
                <th scope="row">{{ t('results.report.payer') }}</th><td>{{ name(doc().visit.payerNameEn, doc().visit.payerNameAr) }}</td>
              </tr>
            </tbody>
          </table>
          @if (showQr()) {
            <app-qr-code [value]="doc().qrValue" [size]="72" [label]="doc().visit.accession" />
          }
        </div>

        @for (section of doc().sections; track section.serviceId) {
          @if (heading(section); as heading) {
            <h2>{{ heading }}</h2>
          }
          <table class="results">
            <thead>
              <tr>
                <th scope="col" rowspan="2">{{ t('results.report.test') }}</th>
                <th scope="col" rowspan="2">{{ t('results.report.current') }}</th>
                <th scope="col" rowspan="2">{{ t('results.report.unit') }}</th>
                <th scope="col" rowspan="2">{{ t('results.report.range') }}</th>
                <th scope="colgroup" [attr.colspan]="columns">{{ t('results.report.previousResults') }}</th>
              </tr>
              <tr>
                @for (index of columnIndexes; track index) {
                  <th scope="col" class="previous">{{ index }}</th>
                }
              </tr>
            </thead>
            <tbody>
              @for (line of section.lines; track line.resultId) {
                @if (isRich(line)) {
                  <tr><td [attr.colspan]="4 + columns"><app-report-rich-line [line]="line" [texts]="texts()" /></td></tr>
                } @else {
                  <tr [class.abnormal]="isAbnormal(line)">
                    <td>
                      {{ lineName(line) }}
                      @if (line.comment) {
                        <div class="comment">{{ line.comment }}</div>
                      }
                    </td>
                    <td class="value">{{ line.value ?? '—' }} <b class="flag">{{ symbol(line) }}</b></td>
                    <td>{{ line.unit ?? '' }}</td>
                    <td>{{ line.range ?? '' }}</td>
                    @for (previous of history(line); track $index) {
                      <td class="previous">
                        @if (previous) {
                          <span class="past">{{ previous.value ?? '—' }} <b>{{ pastFlag(previous) }}</b></span>
                          <span class="day">{{ day(previous) }}</span>
                        } @else {
                          <span class="day">—</span>
                        }
                      </td>
                    }
                  </tr>
                }
              }
            </tbody>
          </table>
        }

        <app-report-signature [doc]="doc()" [signatureUrl]="signatureUrl()" [texts]="texts()" />
        @if (footer(); as footer) {
          <span documentFooter>{{ footer }}</span>
        }
      </app-document-frame>
    </div>
  `,
  styles: `
    :host { display: block; }
    .report { color: #111; }
    .banner { margin: 4px 0 8px; padding: 4px 10px; border: 2px dashed #111; text-align: center; font-weight: 700; }
    .header { display: flex; gap: 10px; align-items: flex-start; }
    .facts { flex: 1 1 auto; }
    .facts th { font-weight: 600; white-space: nowrap; }
    h2 { margin: 12px 0 4px; font-size: 13px; color: #0b2a5b; }
    .value { font-weight: 700; font-variant-numeric: tabular-nums; white-space: nowrap; }
    tr.abnormal .value { text-decoration: underline; }
    th.previous { font-size: 10px; text-align: center; }
    td.previous { text-align: center; color: #333; background: #f7f9fd; }
    .past { display: block; font-variant-numeric: tabular-nums; }
    .day { display: block; font-size: 9px; color: #666; }
    .comment { margin-top: 2px; font-style: italic; white-space: pre-wrap; }
    .logo { max-width: 160px; max-height: 56px; object-fit: contain; }
    .mono { font-family: 'Cascadia Mono', Consolas, monospace; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportCumulativeComponent extends ReportDesignBase {
  protected readonly columns = CUMULATIVE_COLUMNS;

  /** The previous-value columns are numbered 1 (most recent) to 3. */
  protected readonly columnIndexes = Array.from({ length: CUMULATIVE_COLUMNS }, (_, index) => index + 1);

  protected history(line: ReportLine): readonly (ReportPreviousValue | null)[] {
    return previousColumns(line.previous);
  }

  protected pastFlag(previous: ReportPreviousValue): string {
    return flagSymbol(previous.flag);
  }

  protected day(previous: ReportPreviousValue): string {
    return printDay(previous.registeredAtUtc, this.lang());
  }
}
