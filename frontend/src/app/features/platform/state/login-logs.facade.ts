import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { PageRequest, SortRequest } from '../../../core/api/paging';
import type { ClientListItem, ClientLoginLogFilter, ClientLoginLogItem } from '../../../core/api/platform.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { mutation } from '../../../core/state/mutation';
import { sortParams } from '../../../shared/forms/paging';
import { type SortKeyMap, type TableSort, serverSortOf } from '../../../shared/ui/data-table/data-table.types';
import { type DownloadedFile, PlatformApi } from '../data/platform.api';

/** The server keys of the sortable columns of the login-logs table (B-03, PlatformAdminSortKeys.ClientLoginLogs); the client name by language. */
export const LOGIN_LOG_SORT_KEYS: SortKeyMap = {
  client: (lang) => (lang === 'ar' ? 'clientAr' : 'client'),
  user: 'user',
  loginAtUtc: 'login',
  logoutAtUtc: 'logout',
  durationMinutes: 'duration',
  status: 'status',
};

/** What the filter card sets (the page and the sort are the table's). */
export type LoginLogCriteria = Omit<ClientLoginLogFilter, 'page' | 'pageSize' | 'sort' | 'direction'>;

export const EMPTY_LOGIN_LOG_CRITERIA: LoginLogCriteria = {
  tenantId: null,
  search: null,
  loginFromUtc: null,
  loginToUtc: null,
  logoutFromUtc: null,
  logoutToUtc: null,
  device: null,
  minMinutes: null,
  maxMinutes: null,
  status: null,
};

/** SA-08b state: the filtered, server-sorted, paged history of the client users' sessions, the client list of the filter and the CSV the server builds. */
@Injectable()
export class LoginLogsFacade {
  private readonly api = inject(PlatformApi);
  private readonly i18n = inject(I18nService);

  readonly filter = signal<ClientLoginLogFilter>({ ...EMPTY_LOGIN_LOG_CRITERIA, page: 1, pageSize: 50 });

  /** The column order chosen in the table header; the server sorts the whole history before paging (validation v0.5.0 B-03). */
  readonly sort = signal<TableSort | null>(null);

  private readonly serverSort = computed<Pick<SortRequest, 'sort' | 'direction'>>(() => sortParams(serverSortOf(this.sort(), LOGIN_LOG_SORT_KEYS, () => this.i18n.lang())));

  private readonly query = computed<ClientLoginLogFilter>(() => ({ ...this.filter(), ...this.serverSort() }));

  readonly logs = resource({
    params: () => this.query(),
    loader: ({ params, abortSignal }) => this.api.listLoginLogs(params, abortSignal),
  });

  /** The clients of the filter's select (every client, by English name). */
  readonly clients = resource({
    loader: ({ abortSignal }) => this.api.listClients({ search: null, source: null, planId: null, status: null, page: 1, pageSize: 200, sort: 'nameEn', direction: 'asc' }, abortSignal),
  });

  readonly rows = computed<readonly ClientLoginLogItem[]>(() => (this.logs.hasValue() ? this.logs.value().items : []));

  readonly totalCount = computed(() => (this.logs.hasValue() ? this.logs.value().totalCount : 0));

  readonly clientOptions = computed<readonly ClientListItem[]>(() => this.clients.value()?.items ?? []);

  /** The CSV of the current criteria and order, built by the server (UTF-8 with BOM, at most 10 000 rows). */
  readonly export = mutation<void, DownloadedFile>(() => this.api.exportLoginLogs({ ...this.criteria(), ...this.serverSort() }, this.i18n.lang()));

  /** Re-reads when a page opens again: the facade outlives the page (route provider), so the previous visit's answer must never stay on screen (UAT D-14). */
  refresh(): void {
    this.logs.reload();
    this.clients.reload();
  }

  /** The filter card's values; a change goes back to page 1. */
  applyCriteria(criteria: LoginLogCriteria): void {
    this.filter.update((current) => ({ ...current, ...criteria, page: 1 }));
  }

  criteria(): LoginLogCriteria {
    const { tenantId, search, loginFromUtc, loginToUtc, logoutFromUtc, logoutToUtc, device, minMinutes, maxMinutes, status } = this.filter();
    return { tenantId, search, loginFromUtc, loginToUtc, logoutFromUtc, logoutToUtc, device, minMinutes, maxMinutes, status };
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
