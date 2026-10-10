import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { PageRequest } from '../../../core/api/paging';
import type {
  ClientFilter,
  ClientListItem,
  ClientSource,
  ClientStatus,
  ProvisionClientRequest,
  UpdateClientRequest,
} from '../../../core/api/platform.contracts';
import { mutation } from '../../../core/state/mutation';
import { PlatformApi } from '../data/platform.api';

/** SA-04 state: the filtered, paged client list, the reference lists of the drawer, and the life-cycle actions. */
@Injectable()
export class ClientsFacade {
  private readonly api = inject(PlatformApi);

  readonly filter = signal<ClientFilter>({ search: null, source: null, planId: null, status: null, page: 1, pageSize: 50 });

  readonly clients = resource({
    params: () => this.filter(),
    loader: ({ params, abortSignal }) => this.api.listClients(params, abortSignal),
  });

  readonly plans = resource({
    loader: ({ abortSignal }) => this.api.listPlans(null, { page: 1, pageSize: 200 }, abortSignal),
  });

  readonly countries = resource({
    loader: ({ abortSignal }) => this.api.listCountries({ search: null, isActive: true, page: 1, pageSize: 200 }, abortSignal),
  });

  /** The country chosen in the drawer; its active governorates load on change. */
  readonly drawerCountryId = signal<string | null>(null);

  readonly governorates = resource({
    params: () => this.drawerCountryId(),
    loader: ({ params, abortSignal }) =>
      params === null ? Promise.resolve(null) : this.api.listGovernorates({ countryId: params, search: null, isActive: true, page: 1, pageSize: 200 }, abortSignal),
  });

  readonly rows = computed<readonly ClientListItem[]>(() => (this.clients.hasValue() ? this.clients.value().items : []));

  readonly totalCount = computed(() => (this.clients.hasValue() ? this.clients.value().totalCount : 0));

  readonly provision = mutation((request: ProvisionClientRequest) => this.api.provisionClient(request), { then: () => this.clients.reload() });

  readonly update = mutation((input: { id: string; request: UpdateClientRequest }) => this.api.updateClient(input.id, input.request), { then: () => this.clients.reload() });

  readonly activate = mutation((id: string) => this.api.activateClient(id), { then: () => this.clients.reload() });

  readonly deactivate = mutation((input: { id: string; reason: string }) => this.api.deactivateClient(input.id, input.reason), { then: () => this.clients.reload() });

  readonly resetPassword = mutation((input: { id: string; newPassword: string }) => this.api.resetClientAdminPassword(input.id, input.newPassword));

  /** Re-reads the clients and the lookups when a page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14). A load in flight is left alone. */
  refresh(): void {
    this.clients.reload();
    this.plans.reload();
    this.countries.reload();
    this.governorates.reload();
  }

  setSearch(search: string): void {
    this.filter.update((current) => ({ ...current, search: search === '' ? null : search, page: 1 }));
  }

  setSource(source: ClientSource | null): void {
    this.filter.update((current) => ({ ...current, source, page: 1 }));
  }

  setPlan(planId: string | null): void {
    this.filter.update((current) => ({ ...current, planId, page: 1 }));
  }

  setStatus(status: ClientStatus | null): void {
    this.filter.update((current) => ({ ...current, status, page: 1 }));
  }

  setPage(page: PageRequest): void {
    this.filter.update((current) => ({ ...current, page: page.page, pageSize: page.pageSize }));
  }

  /** Every client matching the filter, for the CSV export. */
  exportRows(): Promise<readonly ClientListItem[]> {
    const filter = this.filter();
    return this.api.all((page) => this.api.listClients({ ...filter, ...page }));
  }
}
