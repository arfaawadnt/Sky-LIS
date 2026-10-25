import { ChangeDetectionStrategy, Component } from '@angular/core';

import { QrCodeComponent } from '../../../../../shared/print/qr-code.component';
import { ReportDesignBase } from './report-design-base';
import { ReportRichLineComponent } from './report-rich-line.component';
import { ReportSignatureComponent } from './report-signature.component';

/**
 * Design 4 — Letterhead (LIS-CR-010): for the laboratory's pre-printed paper. No logo and no laboratory header: a blank
 * top band leaves the paper's own letterhead free — 45 mm from the sheet's top edge (10 mm page margin + 10 mm page
 * padding + the 25 mm band) — then the title, the facts, the results, the signature and the printed-by line.
 */
@Component({
  selector: 'app-report-letterhead',
  imports: [QrCodeComponent, ReportRichLineComponent, ReportSignatureComponent],
  template: `
    <div class="print-page report design-letterhead" [attr.dir]="dir()" [attr.lang]="lang()">
      <article class="print-document">
        <div class="band" aria-hidden="true"></div>
        <div class="title-row">
          <h1>{{ title() }}</h1>
          <span class="mono">{{ doc().visit.accession }}</span>
        </div>
        @if (subtitle(); as subtitle) {
          <p class="sub">{{ subtitle }}</p>
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
                <th scope="col">{{ t('results.report.test') }}</th>
                <th scope="col">{{ t('results.report.result') }}</th>
                <th scope="col">{{ t('results.report.unit') }}</th>
                <th scope="col">{{ t('results.report.range') }}</th>
                <th scope="col">{{ t('results.report.flag') }}</th>
              </tr>
            </thead>
            <tbody>
              @for (line of section.lines; track line.resultId) {
                @if (isRich(line)) {
                  <tr><td colspan="5"><app-report-rich-line [line]="line" [texts]="texts()" /></td></tr>
                } @else {
                  <tr [class.abnormal]="isAbnormal(line)">
                    <td>
                      {{ lineName(line) }}
                      @if (line.comment) {
                        <div class="comment">{{ line.comment }}</div>
                      }
                    </td>
                    <td class="value">{{ line.value ?? '—' }}</td>
                    <td>{{ line.unit ?? '' }}</td>
                    <td>{{ line.range ?? '' }}</td>
                    <td class="flag">{{ symbol(line) }}</td>
                  </tr>
                }
              }
            </tbody>
          </table>
        }

        <app-report-signature [doc]="doc()" [signatureUrl]="signatureUrl()" [texts]="texts()" />
        <footer class="foot">
          @if (doc().generatedBy; as by) {
            <span>{{ t('shared.print.printedBy') }}: {{ by }}</span>
          }
          <span>{{ t('shared.print.printedAt') }}: {{ at(doc().generatedAtUtc) }}</span>
          @if (footer(); as footer) {
            <span>{{ footer }}</span>
          }
        </footer>
      </article>
    </div>
  `,
  styles: `
    :host { display: block; }
    .report { color: #111; }
    .band { block-size: 25mm; }
    .title-row { display: flex; justify-content: space-between; align-items: baseline; gap: 12px; border-block-end: 1px solid #333; }
    h1 { margin: 0; font-size: 16px; }
    h2 { margin: 10px 0 2px; font-size: 13px; }
    .sub { margin: 2px 0 6px; color: #444; }
    .banner { margin: 4px 0; padding: 3px 8px; border: 2px dashed #111; text-align: center; font-weight: 700; }
    .header { display: flex; gap: 10px; align-items: flex-start; }
    .facts { flex: 1 1 auto; }
    .facts th { font-weight: 600; white-space: nowrap; }
    .value { font-weight: 700; font-variant-numeric: tabular-nums; }
    .flag { font-weight: 700; text-align: center; }
    tr.abnormal .value, tr.abnormal .flag { text-decoration: underline; }
    .comment { margin-top: 2px; font-style: italic; white-space: pre-wrap; }
    .foot { display: flex; flex-wrap: wrap; justify-content: space-between; gap: 4px 12px; margin-block-start: 12px; font-size: 10px; color: #444; }
    .mono { font-family: 'Cascadia Mono', Consolas, monospace; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportLetterheadComponent extends ReportDesignBase {}
