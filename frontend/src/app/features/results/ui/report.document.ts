import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';

import type { LaboratoryReportDocument, ReportLine, ReportSection } from '../../../core/api/results.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { DocumentFrameComponent } from '../../../shared/print/document-frame.component';
import { QrCodeComponent } from '../../../shared/print/qr-code.component';
import { ageText, flagSymbol, formatDateTime, pickName } from './result-format';

/**
 * The laboratory report of one report unit (A4; SRS CP-13/CP-19, LDM §5.7, BR-09/BR-20): letterhead, patient and
 * visit header, samples, the results by profile banner with unit, reference range and flag, culture isolates with
 * their sensitivity table, formatted paragraphs, the comments, the verifier's electronic signature, the QR code
 * with the accession, the preliminary marker (`printUnverified`), the amendment count, the print-list number and
 * the reprint count. Rendered by `PrintService` from the DTO of `GET …/report-units/{key}/documents/report`.
 * The document language follows the report (`doc.language`), not the screen.
 */
@Component({
  selector: 'app-results-report-document',
  imports: [DocumentFrameComponent, QrCodeComponent],
  template: `
    <div class="print-page report" [attr.dir]="dir()" [attr.lang]="lang()">
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
      >
        <img documentLogo src="/brand/sky-lis-logo.png" alt="" height="36" />
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
                <th scope="row">{{ t('results.report.patient') }}</th>
                <td>{{ patientName() }}</td>
              </tr>
              <tr>
                <th scope="row">{{ t('results.report.patientNumber') }}</th>
                <td class="mono">{{ doc().patient.number ?? '—' }}</td>
              </tr>
              <tr>
                <th scope="row">{{ t('results.report.ageSex') }}</th>
                <td>{{ age() }} · {{ gender() }}</td>
              </tr>
              <tr>
                <th scope="row">{{ t('results.report.registeredAt') }}</th>
                <td>{{ at(doc().visit.registeredAtUtc) }}</td>
              </tr>
              <tr>
                <th scope="row">{{ t('results.report.doctor') }}</th>
                <td>{{ name(doc().visit.doctorNameEn, doc().visit.doctorNameAr) }}</td>
              </tr>
              <tr>
                <th scope="row">{{ t('results.report.payer') }}</th>
                <td>{{ name(doc().visit.payerNameEn, doc().visit.payerNameAr) }}</td>
              </tr>
              @if (doc().samples.length > 0) {
                <tr>
                  <th scope="row">{{ t('results.report.samples') }}</th>
                  <td>{{ samples() }}</td>
                </tr>
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
                @if (line.testType === 'Culture' && line.culture; as culture) {
                  <tr>
                    <td colspan="5">
                      <b>{{ lineName(line) }}</b>
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
                      @if (line.comment) {
                        <div class="comment">{{ line.comment }}</div>
                      }
                    </td>
                  </tr>
                } @else if (line.testType === 'FormattedText' && line.paragraphs; as paragraphs) {
                  <tr>
                    <td colspan="5">
                      <b>{{ lineName(line) }}</b>
                      @for (paragraph of paragraphs; track $index) {
                        <div class="paragraph">
                          @if (paragraph.title) {
                            <b>{{ paragraph.title }}</b>
                          }
                          <div class="text">{{ paragraph.text }}</div>
                        </div>
                      }
                      @if (line.comment) {
                        <div class="comment">{{ line.comment }}</div>
                      }
                    </td>
                  </tr>
                } @else {
                  <tr [class.abnormal]="line.flag && line.flag !== 'Normal'">
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

        <div class="signature">
          <div>
            @if (doc().signature.verifiedByName; as verifier) {
              <b>{{ verifier }}</b>
              <div class="muted small">{{ t('results.report.verifiedBy') }} · {{ at(doc().signature.verifiedAtUtc) }} · {{ t('results.report.eSignature') }}</div>
            } @else {
              <div class="muted small">{{ t('results.report.notVerified') }}</div>
            }
          </div>
          <div class="muted small">
            @if (doc().amendmentCount > 0) {
              <div>{{ tf('results.report.amended', { count: doc().amendmentCount }) }}</div>
            }
            @if (doc().printCount > 1) {
              <div>{{ tf('results.report.reprint', { count: doc().printCount }) }}</div>
            }
            @if (doc().printListNumber; as list) {
              <div>{{ t('results.report.printList') }}: <span class="mono">{{ list }}</span></div>
            }
          </div>
        </div>
        @if (footer(); as footer) {
          <span documentFooter>{{ footer }}</span>
        }
      </app-document-frame>
    </div>
  `,
  styles: `
    :host {
      display: block;
    }

    .report {
      color: #111;
    }

    .banner {
      margin: 4px 0 8px;
      padding: 4px 10px;
      border: 2px dashed #111;
      text-align: center;
      font-weight: 700;
      letter-spacing: 0.06em;
    }

    .header {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: 12px;
    }

    .facts {
      flex: 1 1 auto;
    }

    .facts th {
      width: 30%;
      font-weight: 600;
    }

    .qr {
      flex: 0 0 auto;
    }

    .section-title {
      margin: 12px 0 4px;
      font-size: 13px;
      color: #0b2a5b;
    }

    .results td.value {
      font-weight: 700;
      font-variant-numeric: tabular-nums;
    }

    .results td.flag {
      font-weight: 700;
      text-align: center;
    }

    .results tr.abnormal td.value,
    .results tr.abnormal td.flag {
      text-decoration: underline;
    }

    .isolate {
      margin: 4px 0;
    }

    .sensitivities {
      margin: 4px 0 0;
      width: auto;
      min-width: 60%;
    }

    .paragraph {
      margin: 4px 0;
    }

    .paragraph .text {
      white-space: pre-wrap;
    }

    .comment {
      margin-top: 2px;
      font-style: italic;
      white-space: pre-wrap;
    }

    .signature {
      display: flex;
      justify-content: space-between;
      gap: 12px;
      margin-top: 18px;
      padding-top: 6px;
      border-top: 1px solid #999;
      page-break-inside: avoid;
    }

    .mono {
      font-family: 'Cascadia Mono', Consolas, monospace;
    }

    .muted {
      color: #555;
    }

    .small {
      font-size: 10px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportDocumentComponent {
  private readonly i18n = inject(I18nService);

  readonly doc = input.required<LaboratoryReportDocument>();

  protected readonly lang = computed(() => (this.doc().language === 'ar' ? 'ar' : 'en'));

  protected readonly dir = computed(() => (this.lang() === 'ar' ? 'rtl' : 'ltr'));

  protected readonly title = computed(() => (this.lang() === 'ar' ? this.doc().titleAr : this.doc().titleEn));

  protected readonly subtitle = computed(() => {
    const template = this.doc().template;
    return template ? (this.lang() === 'ar' ? template.headerTextAr : template.headerTextEn) : null;
  });

  protected readonly footer = computed(() => {
    const template = this.doc().template;
    return template ? (this.lang() === 'ar' ? template.footerTextAr : template.footerTextEn) : null;
  });

  protected readonly branch = computed(() => {
    const letterhead = this.doc().letterhead;
    const picked = pickName(letterhead.branchNameEn, letterhead.branchNameAr, this.lang());
    return picked === '—' ? null : picked;
  });

  protected readonly patientName = computed(() => pickName(this.doc().patient.fullName, this.doc().patient.nameAr, this.lang()));

  protected readonly age = computed(() => {
    const patient = this.doc().patient;
    return ageText(patient.ageYears, patient.ageMonths, patient.ageDays, { y: this.t('results.age.y'), m: this.t('results.age.m'), d: this.t('results.age.d') });
  });

  protected readonly gender = computed(() => {
    const gender = this.doc().patient.gender;
    return gender ? this.t(`results.gender.${gender}`) : '—';
  });

  protected readonly samples = computed(() => this.doc().samples.map((sample) => `${sample.sampleNumber ?? '—'} (${this.at(sample.collectedAtUtc)})`).join(', '));

  protected readonly showQr = computed(() => this.doc().template?.showQr ?? true);

  protected heading(section: ReportSection): string | null {
    const heading = this.lang() === 'ar' ? (section.headingAr ?? section.headingEn) : (section.headingEn ?? section.headingAr);
    return section.isProfile && heading ? heading : null;
  }

  protected lineName(line: ReportLine): string {
    return pickName(line.nameEn, line.nameAr, this.lang());
  }

  protected symbol(line: ReportLine): string {
    return flagSymbol(line.flag);
  }

  protected name(en: string | null, ar: string | null): string {
    return pickName(en, ar, this.lang());
  }

  protected at(value: string | null | undefined): string {
    return formatDateTime(value, this.lang());
  }

  /** The texts of the document follow the report language (a laboratory prints Arabic reports from an English screen). */
  protected t(key: string): string {
    return this.i18n.t(key);
  }

  protected tf(key: string, params: Readonly<Record<string, string | number>>): string {
    return this.i18n.tf(key, params);
  }
}
