import { ChangeDetectionStrategy, Component } from '@angular/core';

import type { ReportLine } from '../../../../../core/api/results.contracts';
import { DocumentFrameComponent } from '../../../../../shared/print/document-frame.component';
import { QrCodeComponent } from '../../../../../shared/print/qr-code.component';
import { ReportDesignBase } from './report-design-base';
import { type RangeBar, rangeBar } from './report-design-format';
import { ReportRichLineComponent } from './report-rich-line.component';
import { ReportSignatureComponent } from './report-signature.component';

/**
 * Design 2 — Modern (LIS-CR-010): a two-column header (patient | visit, with the QR), coloured flag bars on the inline
 * start of each row (normal, out of range, critical) and a range bar per numeric result showing where the value sits
 * against its reference range. Logical properties only, so the bars mirror in Arabic.
 */
@Component({
  selector: 'app-report-modern',
  imports: [DocumentFrameComponent, QrCodeComponent, ReportRichLineComponent, ReportSignatureComponent],
  template: `
    <div class="print-page report design-modern" [attr.dir]="dir()" [attr.lang]="lang()">
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
        <div class="cards">
          <section class="card">
            <b class="who">{{ patientName() }}</b>
            <span>{{ t('results.report.patientNumber') }}: <span class="mono">{{ doc().patient.number ?? '—' }}</span></span>
            <span>{{ t('results.report.ageSex') }}: {{ age() }} · {{ gender() }}</span>
            <span>{{ t('results.report.doctor') }}: {{ name(doc().visit.doctorNameEn, doc().visit.doctorNameAr) }}</span>
          </section>
          <section class="card">
            <span>{{ t('results.report.registeredAt') }}: {{ at(doc().visit.registeredAtUtc) }}</span>
            <span>{{ t('results.report.payer') }}: {{ name(doc().visit.payerNameEn, doc().visit.payerNameAr) }}</span>
            @if (doc().samples.length > 0) {
              <span>{{ t('results.report.samples') }}: {{ samples() }}</span>
            }
          </section>
          @if (showQr()) {
            <app-qr-code [value]="doc().qrValue" [size]="72" [label]="doc().visit.accession" />
          }
        </div>

        @for (section of doc().sections; track section.serviceId) {
          @if (heading(section); as heading) {
            <h2 class="section">{{ heading }}</h2>
          }
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
              @for (line of section.lines; track line.resultId) {
                @if (isRich(line)) {
                  <tr><td colspan="4"><app-report-rich-line [line]="line" [texts]="texts()" /></td></tr>
                } @else {
                  <tr [class]="'tone-' + tone(line)">
                    <td>
                      {{ lineName(line) }}
                      @if (line.methodology) {
                        <div class="muted">{{ line.methodology }}</div>
                      }
                      @if (line.comment) {
                        <div class="comment">{{ line.comment }}</div>
                      }
                    </td>
                    <td class="value">{{ line.value ?? '—' }} <span class="unit">{{ line.unit ?? '' }}</span></td>
                    <td>
                      <span class="muted">{{ line.range ?? '' }}</span>
                      @if (bar(line); as bar) {
                        <span class="track" aria-hidden="true"><span class="zone"></span><span [class]="'marker ' + bar.position" [style.--marker]="bar.marker + '%'"></span></span>
                      }
                    </td>
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
    .report { color: #14213a; }
    .banner { margin: 4px 0 8px; padding: 4px 10px; border: 2px dashed #14213a; text-align: center; font-weight: 700; }
    .cards { display: grid; grid-template-columns: 1fr 1fr auto; gap: 10px; align-items: start; margin-block-end: 8px; }
    .card {
      display: grid;
      gap: 2px;
      padding: 6px 10px;
      border-inline-start: 3px solid var(--blue, #2a7df5);
      background: var(--blue-soft, #e8f1fe);
    }
    .who { font-size: 14px; color: var(--navy, #0b2a5b); }
    .section { margin: 12px 0 4px; font-size: 13px; color: var(--violet-2, #5b2acb); }
    .results th { background: var(--navy, #0b2a5b); color: #fff; border-color: var(--navy, #0b2a5b); }
    .results td { border-inline: 0; border-color: #dce3ee; }
    .results tr > td:first-child { border-inline-start: 4px solid transparent; }
    .results tr.tone-normal > td:first-child { border-inline-start-color: var(--ok, #13865a); }
    .results tr.tone-abnormal > td:first-child { border-inline-start-color: var(--warn, #b26b00); }
    .results tr.tone-critical > td:first-child { border-inline-start-color: var(--bad, #c8102e); }
    .results tr.tone-critical td.value, .results tr.tone-critical td.flag { color: var(--bad, #c8102e); }
    .value { font-weight: 700; font-variant-numeric: tabular-nums; white-space: nowrap; }
    .unit { font-weight: 400; color: #555; }
    .flag { font-weight: 700; text-align: center; }
    .track { position: relative; display: block; block-size: 6px; margin-block-start: 3px; border-radius: 3px; background: #eaeff6; }
    .zone { position: absolute; inset-block: 0; inset-inline: 25%; border-radius: 3px; background: var(--ok-soft, #e3f5ec); border: 1px solid var(--ok, #13865a); }
    .marker {
      position: absolute;
      inset-block: -2px;
      inset-inline-start: var(--marker);
      inline-size: 3px;
      margin-inline-start: -1px;
      background: var(--ok, #13865a);
    }
    .marker.below, .marker.above { background: var(--warn, #b26b00); }
    .comment { margin-top: 2px; font-style: italic; white-space: pre-wrap; }
    .logo { max-width: 160px; max-height: 56px; object-fit: contain; }
    .mono { font-family: 'Cascadia Mono', Consolas, monospace; }
    .muted { color: #555; font-size: 10px; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportModernComponent extends ReportDesignBase {
  protected bar(line: ReportLine): RangeBar | null {
    return rangeBar(line.value, line.range);
  }
}
