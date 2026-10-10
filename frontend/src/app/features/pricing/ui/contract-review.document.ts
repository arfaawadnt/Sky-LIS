import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import type { ContractPricingReviewDocument } from '../../../core/api/pricing.contracts';
import { DocumentFrameComponent } from '../../../shared/print/document-frame.component';
import { formatAmount, formatFactor } from '../data/money';

/** Already translated texts of the review report (the print kit renders the component off-screen, outside the i18n of the page). */
export interface ReviewLabels {
  readonly title: string;
  readonly payer: string;
  readonly tier: string;
  readonly effective: string;
  readonly openEnded: string;
  readonly applied: string;
  readonly never: string;
  readonly currency: string;
  readonly formula: string;
  readonly priority: string;
  readonly baseList: string;
  readonly patientFactor: string;
  readonly payerFactor: string;
  readonly inactive: string;
  readonly prices: string;
  readonly code: string;
  readonly service: string;
  readonly source: string;
  readonly listPrice: string;
  readonly patientShare: string;
  readonly payerShare: string;
  readonly total: string;
  readonly excluded: string;
  readonly alert: string;
  readonly totals: string;
  readonly preparedBy: string;
  readonly reviewedBy: string;
  readonly approvedBy: string;
  readonly labNameEn: string;
  readonly labNameAr: string;
}

/** One printed line: the service code and name are resolved by the page from the catalogue. */
export interface ReviewLine {
  readonly code: string;
  readonly name: string;
  readonly source: string;
  readonly listPrice: number;
  readonly patientShare: number;
  readonly payerShare: number;
  readonly excluded: boolean;
  readonly alert: string | null;
}

/**
 * The contract pricing review report (CP-34 "review report", LDM yearly `Print`): payer and tier, effective dates,
 * formula, last apply, every derived price with exclusions and alerts, and the prepare / review / approve boxes.
 * Rendered by `PrintService` (A4 landscape).
 */
@Component({
  selector: 'app-contract-review-document',
  imports: [DocumentFrameComponent],
  template: `
    <app-document-frame
      [labNameEn]="labels().labNameEn"
      [labNameAr]="labels().labNameAr"
      [title]="labels().title"
      [subtitle]="payerName() + ' / ' + tierName()"
      [reference]="doc().payerSearchKey"
      [printedBy]="doc().printedBy"
      [printedAt]="printedAt()"
    >
      <table class="facts avoid-break">
        <tbody>
          <tr>
            <th scope="row">{{ labels().payer }}</th>
            <td>{{ payerName() }} ({{ doc().payerSearchKey }})</td>
            <th scope="row">{{ labels().tier }}</th>
            <td>{{ tierName() }}</td>
          </tr>
          <tr>
            <th scope="row">{{ labels().effective }}</th>
            <td>{{ doc().effectiveFrom }} → {{ doc().effectiveTo ?? labels().openEnded }}</td>
            <th scope="row">{{ labels().applied }}</th>
            <td>{{ appliedAt() }}</td>
          </tr>
          <tr>
            <th scope="row">{{ labels().currency }}</th>
            <td colspan="3">{{ doc().currency }}</td>
          </tr>
        </tbody>
      </table>

      <h3>{{ labels().formula }}</h3>
      <table class="avoid-break">
        <thead>
          <tr>
            <th scope="col">{{ labels().priority }}</th>
            <th scope="col">{{ labels().baseList }}</th>
            <th scope="col" class="num">{{ labels().patientFactor }}</th>
            <th scope="col" class="num">{{ labels().payerFactor }}</th>
          </tr>
        </thead>
        <tbody>
          @for (term of doc().terms; track term.priority) {
            <tr>
              <td>{{ term.priority }}</td>
              <td>{{ term.listCode }} · {{ lang() === 'ar' ? term.listNameAr : term.listNameEn }} ({{ term.listYear }}){{ term.listIsActive ? '' : ' — ' + labels().inactive }}</td>
              <td class="num">× {{ factor(term.patientFactor) }}</td>
              <td class="num">× {{ factor(term.payerFactor) }}</td>
            </tr>
          }
        </tbody>
      </table>

      <h3>{{ labels().prices }}</h3>
      <table>
        <thead>
          <tr>
            <th scope="col">{{ labels().code }}</th>
            <th scope="col">{{ labels().service }}</th>
            <th scope="col">{{ labels().source }}</th>
            <th scope="col" class="num">{{ labels().listPrice }}</th>
            <th scope="col" class="num">{{ labels().patientShare }}</th>
            <th scope="col" class="num">{{ labels().payerShare }}</th>
            <th scope="col" class="num">{{ labels().total }}</th>
            <th scope="col">{{ labels().excluded }}</th>
            <th scope="col">{{ labels().alert }}</th>
          </tr>
        </thead>
        <tbody>
          @for (line of lines(); track $index) {
            <tr>
              <td>{{ line.code }}</td>
              <td>{{ line.name }}</td>
              <td>{{ line.source }}</td>
              <td class="num">{{ amount(line.listPrice) }}</td>
              <td class="num">{{ amount(line.patientShare) }}</td>
              <td class="num">{{ amount(line.payerShare) }}</td>
              <td class="num">{{ amount(line.patientShare + line.payerShare) }}</td>
              <td>{{ line.excluded ? '✓' : '' }}</td>
              <td>{{ line.alert ?? '' }}</td>
            </tr>
          }
        </tbody>
        <tfoot>
          <tr>
            <th scope="row" colspan="3">{{ labels().totals }} ({{ lines().length }})</th>
            <td class="num">{{ amount(sum('listPrice')) }}</td>
            <td class="num">{{ amount(sum('patientShare')) }}</td>
            <td class="num">{{ amount(sum('payerShare')) }}</td>
            <td class="num">{{ amount(sum('patientShare') + sum('payerShare')) }}</td>
            <td colspan="2"></td>
          </tr>
        </tfoot>
      </table>

      <div documentFooter class="signatures avoid-break">
        <div class="box">{{ labels().preparedBy }}</div>
        <div class="box">{{ labels().reviewedBy }}</div>
        <div class="box">{{ labels().approvedBy }}</div>
      </div>
    </app-document-frame>
  `,
  styles: `
    h3 {
      margin: 12px 0 6px;
      font-size: 13px;
    }
    table {
      width: 100%;
      border-collapse: collapse;
      font-size: 11px;
    }
    .facts th {
      width: 14%;
      text-align: start;
    }
    .signatures {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 16px;
      margin-top: 24px;
    }
    .box {
      min-height: 56px;
      padding: 6px;
      border: 1px solid #000;
      font-size: 11px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ContractReviewDocumentComponent {
  readonly doc = input.required<ContractPricingReviewDocument>();
  readonly lines = input.required<readonly ReviewLine[]>();
  readonly labels = input.required<ReviewLabels>();
  readonly lang = input<string>('en');

  protected readonly payerName = computed(() => (this.lang() === 'ar' ? this.doc().payerNameAr : this.doc().payerNameEn));
  protected readonly tierName = computed(() => (this.lang() === 'ar' ? this.doc().tierNameAr : this.doc().tierNameEn));
  protected readonly printedAt = computed(() => formatDateTime(this.doc().printedAtUtc, this.lang()));
  protected readonly appliedAt = computed(() => {
    const applied = this.doc().appliedAtUtc;
    return applied ? formatDateTime(applied, this.lang()) : this.labels().never;
  });

  protected amount(value: number): string {
    return formatAmount(value, this.lang());
  }

  protected factor(value: number): string {
    return formatFactor(value, this.lang());
  }

  protected sum(key: 'listPrice' | 'patientShare' | 'payerShare'): number {
    return this.lines().reduce((total, line) => total + line[key], 0);
  }
}

function formatDateTime(iso: string, lang: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : new Intl.DateTimeFormat(lang === 'ar' ? 'ar-EG' : 'en-GB', { dateStyle: 'medium', timeStyle: 'short' }).format(date);
}
