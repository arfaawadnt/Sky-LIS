import { ChangeDetectionStrategy, Component } from '@angular/core';

import { QrCodeComponent } from '../../../../../shared/print/qr-code.component';
import { ReportDesignBase } from './report-design-base';
import { ReportRichLineComponent } from './report-rich-line.component';
import { ReportSignatureComponent } from './report-signature.component';

/**
 * Design 3 — Compact (LIS-CR-010): a one-line letterhead, the patient and visit facts in a dense grid and every section
 * in one table (profile headings as rows), small type and tight cells, so a usual report fits on one page.
 */
@Component({
  selector: 'app-report-compact',
  imports: [QrCodeComponent, ReportRichLineComponent, ReportSignatureComponent],
  template: `
    <div class="print-page report design-compact" [attr.dir]="dir()" [attr.lang]="lang()">
      <article class="print-document">
        <header class="head">
          @if (images()?.logoUrl; as logoUrl) {
            <img class="logo" [src]="logoUrl" alt="" />
          }
          <b>{{ name(doc().letterhead.labNameEn, doc().letterhead.labNameAr) }}</b>
          @if (branch(); as branch) {
            <span>{{ branch }}</span>
          }
          <span class="title">{{ title() }}</span>
          <span class="mono">{{ doc().visit.accession }}</span>
        </header>
        @if (subtitle(); as subtitle) {
          <p class="sub">{{ subtitle }}</p>
        }
        @if (doc().isPreview) {
          <p class="banner">{{ t('results.report.preview') }}</p>
        }
        @if (doc().isPreliminary) {
          <p class="banner">{{ t('results.report.preliminary') }}</p>
        }
        <div class="facts">
          <dl>
            <dt>{{ t('results.report.patient') }}</dt><dd>{{ patientName() }}</dd>
            <dt>{{ t('results.report.patientNumber') }}</dt><dd class="mono">{{ doc().patient.number ?? '—' }}</dd>
            <dt>{{ t('results.report.ageSex') }}</dt><dd>{{ age() }} · {{ gender() }}</dd>
            <dt>{{ t('results.report.registeredAt') }}</dt><dd>{{ at(doc().visit.registeredAtUtc) }}</dd>
            <dt>{{ t('results.report.doctor') }}</dt><dd>{{ name(doc().visit.doctorNameEn, doc().visit.doctorNameAr) }}</dd>
            <dt>{{ t('results.report.payer') }}</dt><dd>{{ name(doc().visit.payerNameEn, doc().visit.payerNameAr) }}</dd>
          </dl>
          @if (showQr()) {
            <app-qr-code [value]="doc().qrValue" [size]="56" />
          }
        </div>

        <table class="results">
          <thead>
            <tr>
              <th scope="col">{{ t('results.report.test') }}</th>
              <th scope="col">{{ t('results.report.result') }}</th>
              <th scope="col">{{ t('results.report.range') }}</th>
              <th scope="col">{{ t('results.report.flag') }}</th>
            </tr>
          </thead>
          <tbody>
            @for (section of doc().sections; track section.serviceId) {
              @if (heading(section); as heading) {
                <tr class="group"><th colspan="4" scope="colgroup">{{ heading }}</th></tr>
              }
              @for (line of section.lines; track line.resultId) {
                @if (isRich(line)) {
                  <tr><td colspan="4"><app-report-rich-line [line]="line" [texts]="texts()" /></td></tr>
                } @else {
                  <tr [class.abnormal]="isAbnormal(line)">
                    <td>
                      {{ lineName(line) }}
                      @if (line.comment) {
                        <i class="comment">— {{ line.comment }}</i>
                      }
                    </td>
                    <td class="value">{{ line.value ?? '—' }} {{ line.unit ?? '' }}</td>
                    <td>{{ line.range ?? '' }}</td>
                    <td class="flag">{{ symbol(line) }}</td>
                  </tr>
                }
              }
            }
          </tbody>
        </table>

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
    .report { color: #111; font-size: 10px; line-height: 1.3; }
    .head {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 4px 10px;
      padding-block-end: 4px;
      border-block-end: 2px solid #0b2a5b;
    }
    .head b { font-size: 12px; color: #0b2a5b; }
    .title { margin-inline-start: auto; font-weight: 700; }
    .logo { max-height: 28px; max-width: 90px; object-fit: contain; }
    .sub { margin: 2px 0; color: #444; }
    .banner { margin: 3px 0; padding: 2px 6px; border: 1px dashed #111; text-align: center; font-weight: 700; }
    .facts { display: flex; gap: 8px; align-items: flex-start; margin-block: 4px; }
    dl { flex: 1 1 auto; display: grid; grid-template-columns: auto 1fr auto 1fr auto 1fr; gap: 1px 6px; margin: 0; }
    dt { font-weight: 600; color: #444; }
    dd { margin: 0; }
    .results { margin: 4px 0; }
    .results th, .results td { padding: 1px 4px; }
    .results tr.group th { background: #f3f6fb; color: #0b2a5b; text-align: start; }
    .value { font-weight: 700; font-variant-numeric: tabular-nums; }
    .flag { font-weight: 700; text-align: center; }
    tr.abnormal .value, tr.abnormal .flag { text-decoration: underline; }
    .comment { color: #444; }
    .foot { display: flex; flex-wrap: wrap; gap: 4px 12px; margin-block-start: 8px; color: #444; font-size: 9px; }
    .mono { font-family: 'Cascadia Mono', Consolas, monospace; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportCompactComponent extends ReportDesignBase {}
