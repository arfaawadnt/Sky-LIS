import { Injectable, inject, resource, signal } from '@angular/core';

import type { LabTestListFilter, LabTestType, ServiceListFilter, ServiceType } from '../../../core/api/catalogue-a.contracts';
import type { PageRequest } from '../../../core/api/paging';
import { CatalogueAApi } from '../data-a/catalogue-a.api';

/** The services register (CP-40): filter in a signal, the page through `resource()`. Provided at the route. */
@Injectable()
export class ServicesListFacade {
  private readonly api = inject(CatalogueAApi);

  readonly filter = signal<ServiceListFilter>({ search: '', isActive: true, type: null, clinicalGroupId: null, page: 1, pageSize: 50 });

  readonly list = resource({
    params: () => this.filter(),
    loader: ({ params, abortSignal }) => this.api.listServices(params, abortSignal),
  });

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

/** The tests register (CP-41). Provided at the route. */
@Injectable()
export class TestsListFacade {
  private readonly api = inject(CatalogueAApi);

  readonly filter = signal<LabTestListFilter>({ search: '', isActive: true, type: null, clinicalGroupId: null, page: 1, pageSize: 50 });

  readonly list = resource({
    params: () => this.filter(),
    loader: ({ params, abortSignal }) => this.api.listTests(params, abortSignal),
  });

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
