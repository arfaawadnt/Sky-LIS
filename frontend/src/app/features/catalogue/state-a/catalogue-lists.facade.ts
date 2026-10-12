import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { LabTestListFilter, LabTestType, ServiceListFilter, ServiceType } from '../../../core/api/catalogue-a.contracts';
import type { PageRequest } from '../../../core/api/paging';
import { I18nService } from '../../../core/i18n/i18n.service';
import { sortParams } from '../../../shared/forms/paging';
import { type SortKeyMap, type TableSort, localizedSortKey, serverSortOf } from '../../../shared/ui/data-table/data-table.types';
import { CatalogueAApi } from '../data-a/catalogue-a.api';

/** The server keys of the sortable columns of the services table (CatalogueSortKeys.Services on the server). */
export const SERVICE_SORT_KEYS: SortKeyMap = { sequence: 'sequence', code: 'code', name: localizedSortKey('name') };

/** The server keys of the sortable columns of the tests table (CatalogueSortKeys.LabTests on the server). */
export const TEST_SORT_KEYS: SortKeyMap = { code: 'code', name: localizedSortKey('name') };

/** The services register (CP-40): filter in a signal, a server-side sort, the page through `resource()`. Provided at the route. */
@Injectable()
export class ServicesListFacade {
  private readonly api = inject(CatalogueAApi);
  private readonly i18n = inject(I18nService);

  readonly filter = signal<ServiceListFilter>({ search: '', isActive: true, type: null, clinicalGroupId: null, page: 1, pageSize: 50 });

  /** The column order chosen in the table header; the server sorts the whole list before paging (validation v0.5.0 B-03). */
  readonly sort = signal<TableSort | null>(null);

  private readonly query = computed<ServiceListFilter>(() => ({ ...this.filter(), ...sortParams(serverSortOf(this.sort(), SERVICE_SORT_KEYS, () => this.i18n.lang())) }));

  readonly list = resource({
    params: () => this.query(),
    loader: ({ params, abortSignal }) => this.api.listServices(params, abortSignal),
  });

  /** A header click: the new order starts at page 1. */
  setSort(sort: TableSort | null): void {
    this.sort.set(sort);
    this.filter.update((current) => ({ ...current, page: 1 }));
  }

  /** Re-reads the services when a page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14). A load in flight is left alone. */
  refresh(): void {
    this.list.reload();
  }

  setSearch(search: string): void {
    this.filter.update((current) => ({ ...current, search, page: 1 }));
  }

  setType(type: ServiceType | null): void {
    this.filter.update((current) => ({ ...current, type, page: 1 }));
  }

  setGroup(clinicalGroupId: string | null): void {
    this.filter.update((current) => ({ ...current, clinicalGroupId, page: 1 }));
  }

  setActive(isActive: boolean | null): void {
    this.filter.update((current) => ({ ...current, isActive, page: 1 }));
  }

  setPage(page: PageRequest): void {
    this.filter.update((current) => ({ ...current, page: page.page, pageSize: page.pageSize }));
  }
}

/** The tests register (CP-41), sorted on the server. Provided at the route. */
@Injectable()
export class TestsListFacade {
  private readonly api = inject(CatalogueAApi);
  private readonly i18n = inject(I18nService);

  readonly filter = signal<LabTestListFilter>({ search: '', isActive: true, type: null, clinicalGroupId: null, page: 1, pageSize: 50 });

  /** The column order chosen in the table header; the server sorts the whole list before paging (validation v0.5.0 B-03). */
  readonly sort = signal<TableSort | null>(null);

  private readonly query = computed<LabTestListFilter>(() => ({ ...this.filter(), ...sortParams(serverSortOf(this.sort(), TEST_SORT_KEYS, () => this.i18n.lang())) }));

  readonly list = resource({
    params: () => this.query(),
    loader: ({ params, abortSignal }) => this.api.listTests(params, abortSignal),
  });

  /** A header click: the new order starts at page 1. */
  setSort(sort: TableSort | null): void {
    this.sort.set(sort);
    this.filter.update((current) => ({ ...current, page: 1 }));
  }

  /** Re-reads the tests when a page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14). A load in flight is left alone. */
  refresh(): void {
    this.list.reload();
  }

  setSearch(search: string): void {
    this.filter.update((current) => ({ ...current, search, page: 1 }));
  }

  setType(type: LabTestType | null): void {
    this.filter.update((current) => ({ ...current, type, page: 1 }));
  }

  setGroup(clinicalGroupId: string | null): void {
    this.filter.update((current) => ({ ...current, clinicalGroupId, page: 1 }));
  }

  setActive(isActive: boolean | null): void {
    this.filter.update((current) => ({ ...current, isActive, page: 1 }));
  }

  setPage(page: PageRequest): void {
    this.filter.update((current) => ({ ...current, page: page.page, pageSize: page.pageSize }));
  }
}
