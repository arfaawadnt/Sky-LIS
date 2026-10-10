import { Injectable, inject, resource, signal } from '@angular/core';

import type { ServiceSequenceChange, ServiceType } from '../../../core/api/catalogue-a.contracts';
import { mutation } from '../../../core/state/mutation';
import { CatalogueAApi } from '../data-a/catalogue-a.api';

/** The service sequence (CP-42): the rows in sequence order for a search and type, and the bulk save. Provided at the route. */
@Injectable()
export class ServiceSequenceFacade {
  private readonly api = inject(CatalogueAApi);

  readonly filter = signal<{ readonly search: string; readonly type: ServiceType | null }>({ search: '', type: null });

  readonly rows = resource({
    params: () => this.filter(),
    loader: ({ params, abortSignal }) => this.api.listServiceSequence(params.search, params.type, abortSignal),
  });

  readonly save = mutation((items: readonly ServiceSequenceChange[]) => this.api.saveServiceSequence(items), { then: () => this.rows.reload() });

  setSearch(search: string): void {
    this.filter.update((current) => ({ ...current, search }));
  }

  setType(type: ServiceType | null): void {
    this.filter.update((current) => ({ ...current, type }));
  }
}
