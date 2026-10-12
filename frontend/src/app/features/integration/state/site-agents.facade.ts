import { Injectable, inject, resource, signal } from '@angular/core';

import type { AgentLogFilter, AgentLogItem, CreateSiteAgentRequest, IntakeFilter, SiteAgentDetail, UpdateSiteAgentRequest } from '../../../core/api/integration.contracts';
import { emptyPage, type PageRequest } from '../../../core/api/paging';
import { mutation } from '../../../core/state/mutation';
import { IntegrationApi } from '../data/integration.api';
import { I18nService } from '../../../core/i18n/i18n.service';
import { type SortKeyMap, type TableSort, serverSortOf } from '../../../shared/ui/data-table/data-table.types';

/** The server key of the sortable column of the site agents table (B-03, IntegrationSortKeys.SiteAgents). */
export const AGENT_SORT_KEYS: SortKeyMap = { name: 'name' };

/**
 * Site agents (CP-61): the register with derived status and heartbeat age, the agent detail, the credential actions
 * (create, rotate, deactivate, reactivate — each returns the secret once where one is issued), configuration push, the
 * agent log viewer and the result intake log.
 */
@Injectable()
export class SiteAgentsFacade {
  private readonly api = inject(IntegrationApi);

  private readonly i18n = inject(I18nService);

  readonly filter = signal<{ search: string; isActive: boolean | null; page: number; pageSize: number }>({ search: '', isActive: null, page: 1, pageSize: 50 });

  /** The column order chosen in the table header; the server sorts the whole list before paging (validation v0.5.0 B-03). */
  readonly sort = signal<TableSort | null>(null);

  readonly list = resource({
    params: () => ({ ...this.filter(), sort: serverSortOf(this.sort(), AGENT_SORT_KEYS, () => this.i18n.lang()) }),
    loader: ({ params, abortSignal }) => this.api.listAgents(params.search, params.isActive, params.page, params.pageSize, abortSignal, params.sort),
  });

  readonly branches = resource({ loader: ({ abortSignal }) => this.api.branches(abortSignal) });

  readonly analysers = resource({ loader: ({ abortSignal }) => this.api.analyserOptions(abortSignal) });

  /** The agent whose detail or logs are open. */
  readonly selectedId = signal<string | null>(null);

  readonly detail = resource({
    params: () => ({ id: this.selectedId() }),
    loader: ({ params }) => (params.id === null ? Promise.resolve(null) : this.api.getAgent(params.id)),
  });

  readonly logFilter = signal<AgentLogFilter>({ from: null, to: null, level: null, search: '', page: 1, pageSize: 50 });

  readonly logs = resource({
    params: () => ({ id: this.selectedId(), filter: this.logFilter() }),
    loader: ({ params, abortSignal }) => (params.id === null ? Promise.resolve(emptyPage<AgentLogItem>()) : this.api.agentLogs(params.id, params.filter, abortSignal)),
  });

  readonly intakeFilter = signal<IntakeFilter>({ from: null, to: null, status: null, analyserId: null, agentId: null, search: '', page: 1, pageSize: 50 });

  readonly intake = resource({
    params: () => this.intakeFilter(),
    loader: ({ params, abortSignal }) => this.api.intake(params, abortSignal),
  });

  /** Re-reads the agents, the lookups, the open agent and the logs when the page opens again (the facade outlives the page; UAT D-14). */
  refresh(): void {
    this.list.reload();
    this.branches.reload();
    this.analysers.reload();
    this.detail.reload();
    this.logs.reload();
    this.intake.reload();
  }

  readonly create = mutation((request: CreateSiteAgentRequest) => this.api.createAgent(request), { then: () => this.list.reload() });

  readonly update = mutation((input: { id: string; request: UpdateSiteAgentRequest }) => this.api.updateAgent(input.id, input.request), { then: () => this.reloadAgent() });

  readonly rotate = mutation((id: string) => this.api.rotateAgentSecret(id), { then: () => this.reloadAgent() });

  readonly push = mutation((id: string) => this.api.pushAgentConfig(id), { then: () => this.reloadAgent() });

  readonly deactivate = mutation((id: string) => this.api.deactivateAgent(id), { then: () => this.reloadAgent() });

  readonly reactivate = mutation((id: string) => this.api.reactivateAgent(id), { then: () => this.reloadAgent() });

  getAgent(id: string): Promise<SiteAgentDetail> {
    return this.api.getAgent(id);
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

  select(id: string | null): void {
    this.selectedId.set(id);
    this.logFilter.update((current) => ({ ...current, page: 1 }));
  }

  setLogFilter(change: Partial<AgentLogFilter>): void {
    this.logFilter.update((current) => ({ ...current, ...change, page: change.page ?? 1 }));
  }

  setIntakeFilter(change: Partial<IntakeFilter>): void {
    this.intakeFilter.update((current) => ({ ...current, ...change, page: change.page ?? 1 }));
  }

  private reloadAgent(): void {
    this.list.reload();
    this.detail.reload();
  }
}
