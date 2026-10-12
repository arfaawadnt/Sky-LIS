import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';

import { PAGE_SIZES, type PageRequest } from '../../../core/api/paging';
import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../icon/icon.component';

/**
 * `<app-paginator [page]="p" [pageSize]="s" [totalCount]="n" (pageChange)="facade.setPage($event)" />`: the grid
 * footer (prototype `.grid-foot`): "Showing 1–50 of 120", page buttons with first/last, a page-size select.
 * Emits a `PageRequest`; the facade puts it into its filter signal.
 */
@Component({
  selector: 'app-paginator',
  imports: [IconComponent],
  templateUrl: './paginator.component.html',
  styleUrl: './paginator.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PaginatorComponent {
  protected readonly i18n = inject(I18nService);

  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly totalCount = input.required<number>();
  readonly pageSizes = input<readonly number[]>(PAGE_SIZES);
  readonly pageChange = output<PageRequest>();

  protected readonly totalPages = computed(() => (this.pageSize() <= 0 ? 0 : Math.ceil(this.totalCount() / this.pageSize())));

  protected readonly from = computed(() => (this.totalCount() === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1));

  protected readonly to = computed(() => Math.min(this.totalCount(), this.page() * this.pageSize()));

  /** R-03: the numbers of the footer in the digits of the language on screen (Arabic-Indic in Arabic, like the amounts of the lists). */
  private readonly numberFormat = computed(() => new Intl.NumberFormat(this.i18n.lang() === 'ar' ? 'ar-EG' : 'en-GB', { maximumFractionDigits: 0 }));

  protected readonly summary = computed(() =>
    this.i18n.tf('shared.paginator.showing', { from: this.number(this.from()), to: this.number(this.to()), total: this.number(this.totalCount()) }));

  /** Up to five page numbers around the current one. */
  protected readonly window = computed<readonly number[]>(() => {
    const total = this.totalPages();
    const current = this.page();
    const start = Math.max(1, Math.min(current - 2, total - 4));
    const end = Math.min(total, start + 4);
    const pages: number[] = [];
    for (let p = start; p <= end; p++) {
      pages.push(p);
    }

    return pages;
  });

  protected readonly isRtl = computed(() => this.i18n.dir() === 'rtl');

  /** A page number, a page size or a count in the language's digits. */
  protected number(value: number): string {
    return this.numberFormat().format(value);
  }

  protected go(page: number): void {
    if (page < 1 || page > Math.max(1, this.totalPages()) || page === this.page()) {
      return;
    }

    this.pageChange.emit({ page, pageSize: this.pageSize() });
  }

  protected onSizeChange(event: Event): void {
    const pageSize = Number((event.target as HTMLSelectElement).value);
    if (Number.isFinite(pageSize) && pageSize > 0) {
      this.pageChange.emit({ page: 1, pageSize });
    }
  }
}
