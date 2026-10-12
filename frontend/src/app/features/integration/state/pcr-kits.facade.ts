import { Injectable, inject, resource, signal } from '@angular/core';

import type { PcrKitDetail, SavePcrKitRequest, SimulatePcrRunRequest } from '../../../core/api/integration.contracts';
import type { PageRequest } from '../../../core/api/paging';
import { mutation } from '../../../core/state/mutation';
import { IntegrationApi } from '../data/integration.api';
import { I18nService } from '../../../core/i18n/i18n.service';
import { type SortKeyMap, type TableSort, serverSortOf } from '../../../shared/ui/data-table/data-table.types';

/** The server key of the sortable column of the PCR kits table (B-03, IntegrationSortKeys.PcrKits). */
export const KIT_SORT_KEYS: SortKeyMap = { name: 'name' };

/** PCR kits (CP-62): register, editor and simulation of a pasted run file (nothing is stored). */
@Injectable()
export class PcrKitsFacade {
  private readonly api = inject(IntegrationApi);

  private readonly i18n = inject(I18nService);

  readonly filter = signal<{ search: string; isActive: boolean | null; page: number; pageSize: number }>({ search: '', isActive: null, page: 1, pageSize: 50 });

  /** The column order chosen in the table header; the server sorts the whole list before paging (validation v0.5.0 B-03). */
  readonly sort = signal<TableSort | null>(null);

  readonly list = resource({
    params: () => ({ ...this.filter(), sort: serverSortOf(this.sort(), KIT_SORT_KEYS, () => this.i18n.lang()) }),
    loader: ({ params, abortSignal }) => this.api.listKits(params.search, params.isActive, params.page, params.pageSize, abortSignal, params.sort),
  });

  readonly analysers = resource({ loader: ({ abortSignal }) => this.api.analyserOptions(abortSignal) });

  /** Re-reads the kits and the analysers when the page opens again (the facade outlives the page; UAT D-14). */
  refresh(): void {
    this.list.reload();
    this.analysers.reload();
  }

  readonly save = mutation(
    (input: { id: string | null; request: SavePcrKitRequest }) =>
      input.id === null ? this.api.createKit(input.request).then((created) => created.id) : this.api.updateKit(input.id, input.request).then(() => input.id ?? ''),
    { then: () => this.list.reload() },
  );

  readonly setActive = mutation((input: { id: string; active: boolean }) => this.api.setKitActive(input.id, input.active), { then: () => this.list.reload() });

  readonly simulate = mutation((input: { id: string; request: SimulatePcrRunRequest }) => this.api.simulateKit(input.id, input.request));

  load(id: string): Promise<PcrKitDetail> {
    return this.api.getKit(id);
  }

  setSearch(search: string): void {
    this.filter.update((current) => ({ ...current, search, page: 1 }));
  }

  setActiveFilter(isActive: boolean | null): void {
    this.filter.update((current) => ({ ...current, isActive, page: 1 }));
  }

  setPage(page: PageRequest): void {
    this.filter.update((current) => ({ ...current, page: page.page, pageSize: page.pageSize }));
  }

  /** A header click: the new order starts at page 1. */
  setSort(sort: TableSort | null): void {
    this.sort.set(sort);
    this.filter.update((current) => ({ ...current, page: 1 }));
  }
}
