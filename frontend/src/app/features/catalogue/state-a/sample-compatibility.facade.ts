import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { SampleConditionView } from '../../../core/api/catalogue-a.contracts';
import type { PageRequest } from '../../../core/api/paging';
import type { CreateSampleCompatibilityGroupRequest, SampleCompatibilityGroupView, UpdateSampleCompatibilityGroupRequest } from '../../../core/api/sample-compatibility.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { mutation } from '../../../core/state/mutation';
import { sortParams } from '../../../shared/forms/paging';
import { type SortKeyMap, type TableSort, localizedSortKey, serverSortOf } from '../../../shared/ui/data-table/data-table.types';
import { CatalogueAApi, OPTIONS_PAGE_SIZE } from '../data-a/catalogue-a.api';
import { SampleCompatibilityApi } from '../data-a/sample-compatibility.api';
import type { CatalogueOption } from './catalogue-a-options';

/** The register filter: search, active flag (null = both), page. */
export interface SampleCompatibilityFilter {
  readonly search: string;
  readonly isActive: boolean | null;
  readonly page: number;
  readonly pageSize: number;
}

const SORT_KEYS: SortKeyMap = { code: 'code', name: localizedSortKey('name') };

/**
 * Compatibility groups of samples (LIS-CR-015 M4): the paged register through `resource()`, the writes through
 * `mutation()` with a reload, the export of the current filter, and the sample types and their conditions the editor
 * offers (conditions read per type on demand). Provided at the route.
 */
@Injectable()
export class SampleCompatibilityFacade {
  private readonly api = inject(SampleCompatibilityApi);
  private readonly catalogue = inject(CatalogueAApi);
  private readonly i18n = inject(I18nService);

  readonly filter = signal<SampleCompatibilityFilter>({ search: '', isActive: true, page: 1, pageSize: 50 });

  readonly sort = signal<TableSort | null>(null);

  private readonly query = computed(() => {
    const filter = this.filter();
    return { search: filter.search, isActive: filter.isActive, page: filter.page, pageSize: filter.pageSize, ...sortParams(serverSortOf(this.sort(), SORT_KEYS, () => this.i18n.lang())) };
  });

  readonly list = resource({
    params: () => this.query(),
    loader: ({ params, abortSignal }) => this.api.list(params, abortSignal),
  });

  readonly rows = computed<readonly SampleCompatibilityGroupView[]>(() => (this.list.hasValue() ? this.list.value().items : []));

  readonly totalCount = computed(() => (this.list.hasValue() ? this.list.value().totalCount : 0));

  /** Every sample type (inactive ones too, so a kept member still shows its name). */
  readonly sampleTypes = resource({
    loader: async ({ abortSignal }): Promise<readonly CatalogueOption[]> =>
      (await this.catalogue.listSampleTypes({ search: '', isActive: null, page: 1, pageSize: OPTIONS_PAGE_SIZE }, abortSignal)).items.map((row) => ({
        value: row.id,
        code: row.code,
        nameEn: row.nameEn,
        nameAr: row.nameAr,
        isActive: row.isActive,
      })),
  });

  readonly sampleTypeOptions = computed<readonly CatalogueOption[]>(() => (this.sampleTypes.hasValue() ? this.sampleTypes.value() : []));

  private readonly conditionMap = signal<ReadonlyMap<string, readonly SampleConditionView[]>>(new Map());

  /** The conditions read so far, by sample type id. */
  readonly conditions = this.conditionMap.asReadonly();

  readonly create = mutation((request: CreateSampleCompatibilityGroupRequest) => this.api.create(request), { then: () => this.list.reload() });

  readonly update = mutation((input: { readonly id: string; readonly request: UpdateSampleCompatibilityGroupRequest }) => this.api.update(input.id, input.request), { then: () => this.list.reload() });

  readonly activation = mutation((input: { readonly id: string; readonly active: boolean }) => this.api.setActive(input.id, input.active), { then: () => this.list.reload() });

  /** Reads the conditions of a sample type once (an editor asks for each type it shows). */
  async loadConditions(sampleTypeId: string): Promise<void> {
    if (this.conditionMap().has(sampleTypeId)) {
      return;
    }

    try {
      const detail = await this.catalogue.getSampleType(sampleTypeId);
      this.conditionMap.update((current) => new Map(current).set(sampleTypeId, detail.conditions));
    } catch {
      this.conditionMap.update((current) => new Map(current).set(sampleTypeId, []));
    }
  }

  /** The rows of the current search and status filter. */
  exportRows(): Promise<readonly SampleCompatibilityGroupView[]> {
    const filter = this.filter();
    return this.api.export({ search: filter.search, isActive: filter.isActive });
  }

  /** Re-reads the list and the options when the page opens again: the facade outlives the page (route provider). */
  refresh(): void {
    this.conditionMap.set(new Map());   // a condition deactivated since must not stay on offer
    this.list.reload();
    this.sampleTypes.reload();
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

  setSort(sort: TableSort | null): void {
    this.sort.set(sort);
    this.filter.update((current) => ({ ...current, page: 1 }));
  }
}
