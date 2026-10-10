import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';

import type { PaymentReceiptDocument } from '../../../core/api/billing.contracts';
import type { JobOrderDocument, PriceQuoteDocument, SampleLabel, VisitLabelsDocument } from '../../../core/api/registration.contracts';
import { I18nService, type Language } from '../../../core/i18n/i18n.service';
import { CODE128, encodeCode128 } from '../../../shared/print/code128';
import { Code128Component } from '../../../shared/print/code128.component';
import { DocumentFrameComponent } from '../../../shared/print/document-frame.component';
import { dateOnly, dateTime, money, pick } from '../data/registration-format';

const LINE = 'display:flex;gap:1.5mm;align-items:baseline;white-space:nowrap;overflow:hidden;';
const CLIP = 'overflow:hidden;text-overflow:ellipsis;white-space:nowrap;';

/**
 * Barcode labels of a visit (CP-06 "save prints labels", LDM §2.2 label content): unit letter, accession, date/time,
 * patient, the sample number as Code 128, sample type · condition, branch and the services; one `.print-page` per label
 * and copy, sized for 50 × 25 mm stock. Inline styles so the rendered HTML carries the layout into the print frame.
 */
@Component({
  selector: 'app-reg-labels-document',
  imports: [Code128Component],
  template: `
    @for (label of pages(); track $index) {
      <div class="print-page" style="font-size:7pt;line-height:1.15;color:#000;">
        <div [style]="line">
          <b style="font-size:1.35em;">{{ label.unitCode }}</b>
          <span style="font-weight:600;">{{ label.accession }}</span>
          @if (label.urgent) {
            <b>!</b>
          }
          <span style="margin-inline-start:auto;">{{ when(label) }}</span>
        </div>
        <div [style]="clip" style="font-weight:600;">{{ patient(label) }}</div>
        <app-code128 [value]="label.sampleNumber" [height]="barcodeHeight(label)" [label]="label.sampleNumber" />
        <div [style]="clip">{{ sample(label) }}</div>
        <div [style]="clip">{{ label.serviceCodes.join(', ') }}</div>
      </div>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegistrationLabelsDocument {
  readonly document = input.required<VisitLabelsDocument>();
  /** One tube only (the samples dialog prints a single label). */
  readonly tubeId = input<string | null>(null);
  readonly lang = input<Language>('en');

  protected readonly line = LINE;
  protected readonly clip = CLIP;

  protected readonly pages = computed(() => {
    const document = this.document();
    const copies = Math.min(Math.max(1, document.copies), 10);
    const tubeId = this.tubeId();
    return document.labels.filter((label) => tubeId === null || label.tubeId === tubeId).flatMap((label) => Array.from({ length: copies }, () => label));
  });

  protected barcodeHeight(label: SampleLabel): number {
    try {
      const modules = encodeCode128(label.sampleNumber).modules + 2 * CODE128.quietZone;
      return Math.max(20, Math.round((modules * 9.5) / 46));
    } catch {
      return 30;
    }
  }

  protected when(label: SampleLabel): string {
    return dateTime(label.registeredAtUtc, this.lang());
  }

  protected patient(label: SampleLabel): string {
    const name = pick(label.patientName, label.patientNameAr, this.lang());
    return [label.patientNumber, name, label.gender === 'Female' ? 'F' : label.gender === 'Male' ? 'M' : null, label.ageYears > 0 ? `${label.ageYears}Y` : null]
      .filter((part): part is string => !!part)
      .join(' · ');
  }

  protected sample(label: SampleLabel): string {
    const type = pick(label.sampleTypeNameEn, label.sampleTypeNameAr, this.lang());
    const condition = pick(label.sampleConditionNameEn, label.sampleConditionNameAr, this.lang());
    const branch = pick(label.branchNameEn, label.branchNameAr, this.lang());
    return [condition === '' ? type : `${type} · ${condition}`, branch].filter((part) => part !== '').join(' — ');
  }
}

/**
 * The job order (LDM §4.5 "Sampling details"; CP-06, CP-10, CP-15 link): visit, patient, contract, doctor, flags, the
 * services with unit, sample number (barcode) and expected result, the tubes, preparation answers, balance, and the
 * draw-time / drawn-by boxes; "sample collected outside" printed for lab-to-lab visits.
 */
@Component({
  selector: 'app-reg-job-order-document',
  imports: [DocumentFrameComponent, Code128Component],
  template: `
    @let doc = document();
    <app-document-frame
      [labNameEn]="doc.letterhead.labNameEn"
      [labNameAr]="doc.letterhead.labNameAr"
      [branch]="branch()"
      [title]="i18n.t('registration.jobOrder.title')"
      [reference]="doc.accession"
      [printedBy]="doc.printedBy"
      [printedAt]="printedAt()"
    >
      <table class="avoid-break">
        <tbody>
          <tr>
            <th>{{ i18n.t('registration.field.patient') }}</th>
            <td>{{ doc.patient.number }} · {{ patientName() }}</td>
            <th>{{ i18n.t('registration.field.ageGender') }}</th>
            <td>{{ doc.patient.ageYears }} · {{ doc.patient.gender ? i18n.t('registration.gender.' + doc.patient.gender) : '—' }}</td>
          </tr>
          <tr>
            <th>{{ i18n.t('registration.field.registeredAt') }}</th>
            <td>{{ registeredAt() }}</td>
            <th>{{ i18n.t('registration.field.visitOrdinal') }}</th>
            <td>{{ i18n.tf('registration.jobOrder.visitOrdinal', { n: doc.visitOrdinal }) }}</td>
          </tr>
          <tr>
            <th>{{ i18n.t('registration.field.contract') }}</th>
            <td>{{ contract() }}</td>
            <th>{{ i18n.t('registration.field.doctor') }}</th>
            <td>{{ doctor() || '—' }}</td>
          </tr>
        </tbody>
      </table>

      @if (doc.urgent || doc.biohazard || doc.collectedOutside) {
        <p style="font-weight:700;">
          @if (doc.urgent) {
            <span>{{ i18n.t('registration.flag.urgent') }} </span>
          }
          @if (doc.biohazard) {
            <span>{{ i18n.t('registration.flag.biohazard') }} </span>
          }
          @if (doc.collectedOutside) {
            <span>{{ i18n.t('registration.jobOrder.collectedOutside') }}</span>
          }
        </p>
      }

      <table>
        <thead>
          <tr>
            <th>{{ i18n.t('registration.column.code') }}</th>
            <th>{{ i18n.t('registration.column.service') }}</th>
            <th>{{ i18n.t('registration.column.unit') }}</th>
            <th>{{ i18n.t('registration.column.sample') }}</th>
            <th>{{ i18n.t('registration.column.expected') }}</th>
            <th>{{ i18n.t('registration.column.status') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (line of doc.lines; track line.lineId) {
            <tr>
              <td>{{ line.kind === 'PackageMember' ? '↳ ' : '' }}{{ line.code }}</td>
              <td>{{ name(line.nameEn, line.nameAr) }}</td>
              <td>{{ line.unitCode ?? '' }}</td>
              <td>{{ line.sampleNumber ?? '' }}</td>
              <td>{{ expected(line.expectedResultAtUtc) }}</td>
              <td>{{ i18n.t('registration.lineStatus.' + line.status) }}</td>
            </tr>
          }
        </tbody>
      </table>

      @if (doc.tubes.length > 0) {
        <div style="display:flex;flex-wrap:wrap;gap:6mm;margin-top:3mm;" class="avoid-break">
          @for (tube of doc.tubes; track tube.id) {
            <div style="border:1px solid #999;padding:2mm;min-width:55mm;">
              <div>
                <b>{{ tube.unitCode }}</b> · {{ name(tube.sampleTypeNameEn, tube.sampleTypeNameAr) }}
                @if (tube.sampleConditionNameEn) {
                  · {{ name(tube.sampleConditionNameEn, tube.sampleConditionNameAr) }}
                }
              </div>
              <app-code128 [value]="tube.sampleNumber" [height]="34" />
              <div style="font-size:0.9em;">{{ tube.serviceCodes.join(', ') }}</div>
              <div style="margin-top:2mm;font-size:0.9em;">{{ i18n.t('registration.jobOrder.drawnAt') }}: ________ {{ i18n.t('registration.jobOrder.drawnBy') }}: ________</div>
            </div>
          }
        </div>
      }

      @if (doc.preparationAnswers.length > 0) {
        <table class="avoid-break">
          <tbody>
            @for (answer of doc.preparationAnswers; track answer.preparationId) {
              <tr>
                <th>{{ name(answer.questionEn, answer.questionAr) }}</th>
                <td>{{ answer.answer }}</td>
              </tr>
            }
          </tbody>
        </table>
      }

      @if (doc.balance; as balance) {
        <table class="avoid-break">
          <tbody>
            <tr>
              <th>{{ i18n.t('registration.summary.patientShare') }}</th>
              <td class="num">{{ amount(balance.patientShareTotal) }} {{ doc.currency }}</td>
              <th>{{ i18n.t('registration.summary.paid') }}</th>
              <td class="num">{{ amount(balance.paid) }}</td>
              <th>{{ i18n.t('registration.summary.remaining') }}</th>
              <td class="num">{{ amount(balance.remaining) }}</td>
            </tr>
          </tbody>
        </table>
      }
      <app-code128 [value]="doc.accession" [height]="30" />
    </app-document-frame>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class JobOrderDocumentComponent {
  protected readonly i18n = inject(I18nService);
  readonly document = input.required<JobOrderDocument>();

  protected readonly branch = computed(() => pick(this.document().letterhead.branchNameEn, this.document().letterhead.branchNameAr, this.i18n.lang()) || null);
  protected readonly printedAt = computed(() => dateTime(this.document().printedAtUtc, this.i18n.lang()));
  protected readonly registeredAt = computed(() => dateTime(this.document().registeredAtUtc, this.i18n.lang()));
  protected readonly patientName = computed(() => pick(this.document().patient.fullName, this.document().patient.nameAr, this.i18n.lang()));
  protected readonly contract = computed(() => {
    const contract = this.document().contract;
    return `${pick(contract.payerNameEn, contract.payerNameAr, this.i18n.lang())} / ${pick(contract.tierNameEn, contract.tierNameAr, this.i18n.lang())}`;
  });
  protected readonly doctor = computed(() => pick(this.document().doctorNameEn, this.document().doctorNameAr, this.i18n.lang()));

  protected name(en: string | null, ar: string | null): string {
    return pick(en, ar, this.i18n.lang());
  }

  protected expected(utc: string | null): string {
    return dateTime(utc, this.i18n.lang());
  }

  protected amount(value: number): string {
    return money(value, this.i18n.lang());
  }
}

/** The price quote (CP-12 "print quote", LDM §7.2 Query): contract, services with shares and expected dates, preparations. */
@Component({
  selector: 'app-reg-price-quote-document',
  imports: [DocumentFrameComponent],
  template: `
    @let doc = document();
    <app-document-frame
      [labNameEn]="doc.letterhead.labNameEn"
      [labNameAr]="doc.letterhead.labNameAr"
      [branch]="branch()"
      [title]="i18n.t('registration.quote.documentTitle')"
      [subtitle]="contract()"
      [printedBy]="doc.printedBy"
      [printedAt]="printedAt()"
    >
      <table>
        <thead>
          <tr>
            <th>{{ i18n.t('registration.column.code') }}</th>
            <th>{{ i18n.t('registration.column.service') }}</th>
            <th class="num">{{ i18n.t('registration.column.listPrice') }}</th>
            <th class="num">{{ i18n.t('registration.column.patientShare') }}</th>
            <th class="num">{{ i18n.t('registration.column.payerShare') }}</th>
            <th>{{ i18n.t('registration.column.expected') }}</th>
          </tr>
        </thead>
        <tbody>
          @for (line of doc.lines; track line.serviceId) {
            <tr>
              <td>{{ line.code }}</td>
              <td>
                {{ name(line.nameEn, line.nameAr) }}
                @if (line.precaution) {
                  <div style="font-size:0.9em;">{{ line.precaution }}</div>
                }
              </td>
              @if (line.priced) {
                <td class="num">{{ amount(line.listPrice) }}</td>
                <td class="num">{{ amount(line.patientShare) }}</td>
                <td class="num">{{ amount(line.payerShare) }}</td>
              } @else {
                <td colspan="3">{{ i18n.t('registration.quote.unpriced') }}</td>
              }
              <td>{{ expected(line.expectedResultAtUtc) }}</td>
            </tr>
          }
        </tbody>
        <tfoot>
          <tr>
            <th colspan="2">{{ i18n.t('registration.summary.total') }} ({{ doc.currency }})</th>
            <th class="num">{{ amount(doc.totalListPrice) }}</th>
            <th class="num">{{ amount(doc.totalPatientShare) }}</th>
            <th class="num">{{ amount(doc.totalPayerShare) }}</th>
            <th></th>
          </tr>
        </tfoot>
      </table>
      @if (doc.preparations.length > 0) {
        <h3>{{ i18n.t('registration.preparations.title') }}</h3>
        <ul>
          @for (question of doc.preparations; track question.preparationId) {
            <li>{{ name(question.questionEn, question.questionAr) }}</li>
          }
        </ul>
      }
      <p documentFooter>{{ i18n.t('registration.quote.disclaimer') }}</p>
    </app-document-frame>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PriceQuoteDocumentComponent {
  protected readonly i18n = inject(I18nService);
  readonly document = input.required<PriceQuoteDocument>();

  protected readonly branch = computed(() => pick(this.document().letterhead.branchNameEn, this.document().letterhead.branchNameAr, this.i18n.lang()) || null);
  protected readonly printedAt = computed(() => dateTime(this.document().printedAtUtc, this.i18n.lang()));
  protected readonly contract = computed(() => {
    const contract = this.document().contract;
    return contract === null ? '' : `${pick(contract.payerNameEn, contract.payerNameAr, this.i18n.lang())} / ${pick(contract.tierNameEn, contract.tierNameAr, this.i18n.lang())}`;
  });

  protected name(en: string | null, ar: string | null): string {
    return pick(en, ar, this.i18n.lang());
  }

  protected expected(utc: string | null): string {
    return dateOnly(utc, this.i18n.lang()) === '—' ? '—' : dateTime(utc, this.i18n.lang());
  }

  protected amount(value: number): string {
    return money(value, this.i18n.lang());
  }
}

/** The receipt of the payment captured on the registration page (BR-26), from Billing's receipt document. */
@Component({
  selector: 'app-reg-receipt-document',
  imports: [DocumentFrameComponent, Code128Component],
  template: `
    @let doc = document();
    <app-document-frame
      [labNameEn]="doc.letterhead.labNameEn"
      [labNameAr]="doc.letterhead.labNameAr"
      [branch]="branch()"
      [title]="i18n.t('registration.receipt.title')"
      [reference]="doc.receiptNumber"
      [printedBy]="doc.printedBy"
      [printedAt]="printedAt()"
    >
      <table>
        <tbody>
          <tr>
            <th>{{ i18n.t('registration.field.accession') }}</th>
            <td>{{ doc.accession }}</td>
          </tr>
          <tr>
            <th>{{ i18n.t('registration.field.patient') }}</th>
            <td>{{ doc.patientNumber ?? '' }} · {{ doc.patientName ?? '' }}</td>
          </tr>
          <tr>
            <th>{{ i18n.t('registration.payment.amount') }}</th>
            <td class="num">{{ amount(doc.amount) }} {{ doc.currency }}</td>
          </tr>
          <tr>
            <th>{{ i18n.t('registration.payment.method') }}</th>
            <td>{{ i18n.t('registration.payment.methods.' + doc.method) }}{{ doc.reference ? ' · ' + doc.reference : '' }}</td>
          </tr>
          <tr>
            <th>{{ i18n.t('registration.summary.remaining') }}</th>
            <td class="num">{{ amount(doc.remaining) }}</td>
          </tr>
        </tbody>
      </table>
      <app-code128 [value]="doc.receiptNumber" [height]="30" />
    </app-document-frame>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegistrationReceiptDocument {
  protected readonly i18n = inject(I18nService);
  readonly document = input.required<PaymentReceiptDocument>();

  protected readonly branch = computed(() => pick(this.document().letterhead.branchNameEn, this.document().letterhead.branchNameAr, this.i18n.lang()) || null);
  protected readonly printedAt = computed(() => dateTime(this.document().printedAtUtc, this.i18n.lang()));

  protected amount(value: number): string {
    return money(value, this.i18n.lang());
  }
}
