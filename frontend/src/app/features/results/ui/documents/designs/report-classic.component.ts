import { ChangeDetectionStrategy, Component } from '@angular/core';

import { DocumentFrameComponent } from '../../../../../shared/print/document-frame.component';
import { QrCodeComponent } from '../../../../../shared/print/qr-code.component';
import { ReportDesignBase } from './report-design-base';
import { ReportRichLineComponent } from './report-rich-line.component';
import { ReportSignatureComponent } from './report-signature.component';

/**
 * Design 1 — Classic (the report before LIS-CR-010): letterhead with the laboratory names around the logo, the patient
 * and visit facts with the QR code, one table per profile (test, result, unit, range, flag), cultures and formatted
 * texts across the table, the signature and the template's footer.
 */
@Component({
  selector: 'app-report-classic',
  imports: [DocumentFrameComponent, QrCodeComponent, ReportRichLineComponent, ReportSignatureComponent],
  template: `
    <div class="print-page report design-classic" [attr.dir]="dir()" [attr.lang]="lang()">
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
          <img documentLogo class="template-logo" [src]="logoUrl" alt="" />
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
              <tr><th scope="row">{{ t('results.report.patient') }}</th><td>{{ patientName() }}</td></tr>
              <tr><th scope="row">{{ t('results.report.patientNumber') }}</th><td class="mono">{{ doc().patient.number ?? '—' }}</td></tr>
              <tr><th scope="row">{{ t('results.report.ageSex') }}</th><td>{{ age() }} · {{ gender() }}</td></tr>
              <tr><th scope="row">{{ t('results.report.registeredAt') }}</th><td>{{ at(doc().visit.registeredAtUtc) }}</td></tr>
              <tr><th scope="row">{{ t('results.report.doctor') }}</th><td>{{ name(doc().visit.doctorNameEn, doc().visit.doctorNameAr) }}</td></tr>
              <tr><th scope="row">{{ t('results.report.payer') }}</th><td>{{ name(doc().visit.payerNameEn, doc().visit.payerNameAr) }}</td></tr>
              @if (doc().samples.length > 0) {
                <tr><th scope="row">{{ t('results.report.samples') }}</th><td>{{ samples() }}</td></tr>
              }
            </tbody>
          </table>
          @if (showQr()) {
            <div class="qr"><app-qr-code [value]="doc().qrValue" [size]="84" [label]="doc().visit.accession" /></div>
          }
        </div>

        @for (section of doc().sections; track section.serviceId) {
          @if (heading(section); as heading) {
            <h2 class="section-title">{{ heading }}</h2>
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
                      @if (line.methodology) {
                        <div class="muted small">{{ line.methodology }}</div>
                      }
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
        @if (footer(); as footer) {
          <span documentFooter>{{ footer }}</span>
        }
      </app-document-frame>
    </div>
  `,
  styles: `
    :host { display: block; }
    .report { color: #111; }
    .banner {
      margin: 4px 0 8px;
      padding: 4px 10px;
      border: 2px dashed #111;
      text-align: center;
      font-weight: 700;
      letter-spacing: 0.06em;
    }
    .header { display: flex; justify-content: space-between; align-items: flex-start; gap: 12px; }
    .facts { flex: 1 1 auto; }
    .facts th { width: 30%; font-weight: 600; }
    .qr { flex: 0 0 auto; }
    .section-title { margin: 12px 0 4px; font-size: 13px; color: #0b2a5b; }
    .results td.value { font-weight: 700; font-variant-numeric: tabular-nums; }
    .results td.flag { font-weight: 700; text-align: center; }
    .results tr.abnormal td.value,
    .results tr.abnormal td.flag { text-decoration: underline; }
    .comment { margin-top: 2px; font-style: italic; white-space: pre-wrap; }
    .template-logo { max-width: 160px; max-height: 56px; object-fit: contain; }
    .mono { font-family: 'Cascadia Mono', Consolas, monospace; }
    .muted { color: #555; }
    .small { font-size: 10px; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportClassicComponent extends ReportDesignBase {}
