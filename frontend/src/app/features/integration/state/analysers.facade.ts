import { Injectable, inject, resource, signal } from '@angular/core';

import type { AnalyserDetail, AnalyserListFilter, SaveAnalyserRequest } from '../../../core/api/integration.contracts';
import type { PageRequest } from '../../../core/api/paging';
import { mutation } from '../../../core/state/mutation';
import { IntegrationApi } from '../data/integration.api';

/** Analysers register and editor (CP-58): reads through `resource()`, writes through `mutation()`. Provided at the route. */
@Injectable()
export class AnalysersFacade {
  private readonly api = inject(IntegrationApi);

  readonly filter = signal<AnalyserListFilter>({ search: '', isActive: null, siteAgentId: null, page: 1, pageSize: 50 });

  readonly list = resource({
    params: () => this.filter(),
    loader: ({ params, abortSignal }) => this.api.listAnalysers(params, abortSignal),
  });

  readonly agents = resource({ loader: ({ abortSignal }) => this.api.listAgents('', null, 1, 200, abortSignal) });

  readonly clinicalGroups = resource({ loader: ({ abortSignal }) => this.api.clinicalGroups(abortSignal) });

  readonly sampleTypes = resource({ loader: ({ abortSignal }) => this.api.sampleTypes(abortSignal) });

  readonly models = resource({ loader: ({ abortSignal }) => this.api.analyserModels(null, abortSignal) });

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
}
