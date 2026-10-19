import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import type { BasePriceListDetail } from '../../../../core/api/pricing.contracts';
import { DocumentFrameComponent } from '../../../../shared/print/document-frame.component';
import { formatAmount } from '../../data/money';

/** Already translated texts of the printed list (the print kit renders the component off-screen, outside the page's i18n). */
export interface PriceListLabels {
  readonly title: string;
  readonly code: string;
  readonly name: string;
  readonly year: string;
  readonly status: string;
  readonly active: string;
  readonly inactive: string;
  readonly lines: string;
  readonly service: string;
  readonly price: string;
  readonly activeAtOrdering: string;
  readonly yes: string;
  readonly no: string;
  readonly count: string;
  readonly labNameEn: string;
  readonly labNameAr: string;
}

/** One printed line: code and name resolved by the page from the service names store. */
export interface PrintedPriceLine {
  readonly code: string;
  readonly name: string;
  readonly price: number;
  readonly active: boolean;
}

/**
 * The printable base price list (CR 2026-10-19 C21 "Print"): list code, name, year and status, then every line with its
 * price and active flag, and the line count. Rendered by `PrintService` (A4 portrait).
 */
@Component({
  selector: 'app-price-list-document',
  imports: [DocumentFrameComponent],
  template: `
    <app-document-frame
      [labNameEn]="labels().labNameEn"
      [labNameAr]="labels().labNameAr"
      [title]="labels().title"
      [subtitle]="detail().code + ' · ' + listName()"
      [reference]="detail().code"
      [printedBy]="printedBy()"
      [printedAt]="printedAt()"
    >
      <table class="facts avoid-break">
        <tbody>
          <tr>
            <th scope="row">{{ labels().code }}</th>
            <td>{{ detail().code }}</td>
            <th scope="row">{{ labels().year }}</th>
            <td>{{ detail().year }}</td>
          </tr>
          <tr>
            <th scope="row">{{ labels().name }}</th>
            <td>{{ listName() }}</td>
            <th scope="row">{{ labels().status }}</th>
            <td>{{ detail().isActive ? labels().active : labels().inactive }}</td>
          </tr>
        </tbody>
      </table>

      <h3>{{ labels().lines }}</h3>
      <table>
        <thead>
          <tr>
            <th scope="col">{{ labels().code }}</th>
            <th scope="col">{{ labels().service }}</th>
            <th scope="col" class="num">{{ labels().price }}</th>
            <th scope="col">{{ labels().activeAtOrdering }}</th>
          </tr>
        </thead>
        <tbody>
          @for (line of lines(); track $index) {
            <tr [class.off]="!line.active">
              <td>{{ line.code }}</td>
              <td>{{ line.name }}</td>
              <td class="num">{{ amount(line.price) }}</td>
              <td>{{ line.active ? labels().yes : labels().no }}</td>
            </tr>
          }
        </tbody>
        <tfoot>
          <tr>
            <th scope="row" colspan="4">{{ labels().count }}: {{ lines().length }}</th>
          </tr>
        </tfoot>
      </table>
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
    .num {
      text-align: end;
      font-variant-numeric: tabular-nums;
    }
    tr.off td {
      color: #666;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PriceListDocumentComponent {
  readonly detail = input.required<BasePriceListDetail>();
  readonly lines = input.required<readonly PrintedPriceLine[]>();
  readonly labels = input.required<PriceListLabels>();
  readonly lang = input<string>('en');
  readonly printedBy = input<string | null>(null);
  readonly printedAt = input<string | null>(null);

  protected readonly listName = computed(() => (this.lang() === 'ar' ? this.detail().nameAr : this.detail().nameEn));

  protected amount(value: number): string {
    return formatAmount(value, this.lang());
  }
}
