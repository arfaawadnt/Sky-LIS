import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { BillingTreasuryItem, CashClosingCreated, CashClosingPreview, CashClosingRegisterFilter, CloseCashDrawerRequest, DrawerScopeFilter } from '../../../core/api/billing.contracts';
import type { PageRequest } from '../../../core/api/paging';
import { mutation } from '../../../core/state/mutation';
import { BillingApi, type DownloadedFile } from '../data/billing.api';

export const DEFAULT_CLOSING_FILTER: CashClosingRegisterFilter = { treasuryId: null, fromUtc: null, toUtc: null, page: 1, pageSize: 20 };

/** The treasury and period of the cashier filter. */
export interface CashierQuery {
  readonly treasuryId: string;
  readonly fromUtc: string;
  readonly toUtc: string;
}

/** CP-30 Cash closing (BR-16): the session branch's treasuries, the preview of a drawer, the close, and the closings register. */
@Injectable()
export class CashClosingFacade {
  private readonly api = inject(BillingApi);

  readonly treasuries = resource({
    loader: ({ abortSignal }) => this.api.branchTreasuries(abortSignal),
  });

  readonly treasuryList = computed<readonly BillingTreasuryItem[]>(() => (this.treasuries.hasValue() ? this.treasuries.value() : []));

  /** Set by "Preview"; the preview reloads after a close (and then reports the overlap). */
  readonly scope = signal<DrawerScopeFilter | null>(null);

  readonly preview = resource({
    params: () => this.scope() ?? undefined,
    loader: ({ params, abortSignal }) => this.api.previewClosing(params, abortSignal),
  });

  readonly currentPreview = computed<CashClosingPreview | null>(() => (this.preview.hasValue() ? this.preview.value() : null));

  readonly cashierQuery = signal<CashierQuery | null>(null);

  readonly cashiers = resource({
    params: () => this.cashierQuery() ?? undefined,
    loader: ({ params, abortSignal }) => this.api.drawerCashiers(params.treasuryId, params.fromUtc, params.toUtc, abortSignal),
  });

  readonly close = mutation((request: CloseCashDrawerRequest): Promise<CashClosingCreated> => this.api.closeDrawer(request), {
    then: () => {
      this.preview.reload();
      this.closings.reload();
    },
  });

  readonly filter = signal<CashClosingRegisterFilter>(DEFAULT_CLOSING_FILTER);

  readonly closings = resource({
    params: () => this.filter(),
    loader: ({ params, abortSignal }) => this.api.listClosings(params, abortSignal),
  });

  readonly exportCsv = mutation<void, DownloadedFile>(() => this.api.exportClosings(this.filter()));

  /** Re-reads the treasuries, the preview, the cashiers and the register when the page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14, A-01). A load in flight is left alone; an idle read (nothing looked up) stays idle. */
  refresh(): void {
    this.treasuries.reload();
    this.preview.reload();
    this.cashiers.reload();
    this.closings.reload();
  }

  setFilter(change: Partial<Omit<CashClosingRegisterFilter, 'page' | 'pageSize'>>): void {
    this.filter.update((current) => ({ ...current, ...change, page: 1 }));
  }

  setPage(page: PageRequest): void {
    this.filter.update((current) => ({ ...current, page: page.page, pageSize: page.pageSize }));
  }
}
