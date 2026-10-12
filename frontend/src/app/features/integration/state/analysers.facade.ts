import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { AnalyserDetail, AnalyserListFilter, SaveAnalyserRequest } from '../../../core/api/integration.contracts';
import type { PageRequest } from '../../../core/api/paging';
import { mutation } from '../../../core/state/mutation';
import { IntegrationApi } from '../data/integration.api';
import { I18nService } from '../../../core/i18n/i18n.service';
import { sortParams } from '../../../shared/forms/paging';
import { type SortKeyMap, type TableSort, localizedSortKey, serverSortOf } from '../../../shared/ui/data-table/data-table.types';

/** The server keys of the sortable columns of the analysers table (B-03, IntegrationSortKeys.Analysers). */
export const ANALYSER_SORT_KEYS: SortKeyMap = { code: 'code', name: localizedSortKey('name') };

/** Analysers register and editor (CP-58): reads through `resource()`, writes through `mutation()`. Provided at the route. */
@Injectable()
export class AnalysersFacade {
  private readonly api = inject(IntegrationApi);

  private readonly i18n = inject(I18nService);

  readonly filter = signal<AnalyserListFilter>({ search: '', isActive: null, siteAgentId: null, page: 1, pageSize: 50 });

  /** The column order chosen in the table header; the server sorts the whole list before paging (validation v0.5.0 B-03). */
  readonly sort = signal<TableSort | null>(null);

  private readonly query = computed<AnalyserListFilter>(() => ({ ...this.filter(), ...sortParams(serverSortOf(this.sort(), ANALYSER_SORT_KEYS, () => this.i18n.lang())) }));

  readonly list = resource({
    params: () => this.query(),
    loader: ({ params, abortSignal }) => this.api.listAnalysers(params, abortSignal),
  });

  readonly agents = resource({ loader: ({ abortSignal }) => this.api.listAgents('', null, 1, 200, abortSignal) });

  readonly clinicalGroups = resource({ loader: ({ abortSignal }) => this.api.clinicalGroups(abortSignal) });

  readonly sampleTypes = resource({ loader: ({ abortSignal }) => this.api.sampleTypes(abortSignal) });

  readonly models = resource({ loader: ({ abortSignal }) => this.api.analyserModels(null, abortSignal) });

  /** Re-reads the list and the lookups when the page opens again: the facade outlives the page (route provider), so the previous visit's answer must never stay on screen (UAT D-14). A load in flight is left alone. */
  refresh(): void {
    this.list.reload();
    this.agents.reload();
    this.clinicalGroups.reload();
    this.sampleTypes.reload();
    this.models.reload();
  }

  readonly save = mutation(
    (input: { id: string | null; request: SaveAnalyserRequest }) =>
      input.id === null ? this.api.createAnalyser(input.request).then((created) => created.id) : this.api.updateAnalyser(input.id, input.request).then(() => input.id ?? ''),
    { then: () => this.list.reload() },
  );

  readonly setActive = mutation((input: { id: string; active: boolean }) => this.api.setAnalyserActive(input.id, input.active), { then: () => this.list.reload() });

  readonly testConnection = mutation((id: string) => this.api.testConnection(id));

  load(id: string): Promise<AnalyserDetail> {
    return this.api.getAnalyser(id);
  }

  setSearch(search: string): void {
    this.filter.update((current) => ({ ...current, search, page: 1 }));
  }

  setActiveFilter(isActive: boolean | null): void {
    this.filter.update((current) => ({ ...current, isActive, page: 1 }));
  }

  setAgent(siteAgentId: string | null): void {
    this.filter.update((current) => ({ ...current, siteAgentId, page: 1 }));
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
