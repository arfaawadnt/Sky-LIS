import { Injectable, inject, resource, signal } from '@angular/core';

import type { AgentLogFilter, AgentLogItem, CreateSiteAgentRequest, IntakeFilter, SiteAgentDetail, UpdateSiteAgentRequest } from '../../../core/api/integration.contracts';
import { emptyPage, type PageRequest } from '../../../core/api/paging';
import { mutation } from '../../../core/state/mutation';
import { IntegrationApi } from '../data/integration.api';

/**
 * Site agents (CP-61): the register with derived status and heartbeat age, the agent detail, the credential actions
 * (create, rotate, deactivate, reactivate — each returns the secret once where one is issued), configuration push, the
 * agent log viewer and the result intake log.
 */
@Injectable()
export class SiteAgentsFacade {
  private readonly api = inject(IntegrationApi);

  readonly filter = signal<{ search: string; isActive: boolean | null; page: number; pageSize: number }>({ search: '', isActive: null, page: 1, pageSize: 50 });

  readonly list = resource({
    params: () => this.filter(),
    loader: ({ params, abortSignal }) => this.api.listAgents(params.search, params.isActive, params.page, params.pageSize, abortSignal),
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

  readonly create = mutation((request: CreateSiteAgentRequest) => this.api.createAgent(request), { then: () => this.list.reload() });

  readonly update = mutation((input: { id: string; request: UpdateSiteAgentRequest }) => this.api.updateAgent(input.id, input.request), { then: () => this.refresh() });

  readonly rotate = mutation((id: string) => this.api.rotateAgentSecret(id), { then: () => this.refresh() });

  readonly push = mutation((id: string) => this.api.pushAgentConfig(id), { then: () => this.refresh() });

  readonly deactivate = mutation((id: string) => this.api.deactivateAgent(id), { then: () => this.refresh() });

  readonly reactivate = mutation((id: string) => this.api.reactivateAgent(id), { then: () => this.refresh() });

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

  private refresh(): void {
    this.list.reload();
    this.detail.reload();
  }
}
