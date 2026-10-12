import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { PageRequest } from '../../../core/api/paging';
import type {
  CreateDiagnosisRequest,
  CreateDoctorRequest,
  CreatePreparationRequest,
  CreateShortcutSetRequest,
  DiagnosisListItem,
  DoctorListItem,
  PreparationListItem,
  ShortcutSetListItem,
  TestRuleListItem,
  TestRuleRequest,
  UpdateDiagnosisRequest,
  UpdateDoctorRequest,
  UpdatePreparationRequest,
  UpdateShortcutSetRequest,
  ValidateTestRuleRequest,
} from '../../../core/api/catalogue-b.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { mutation } from '../../../core/state/mutation';
import { type FilterValue, sortParams } from '../../../shared/forms/paging';
import { type SortKeyMap, type TableSort, localizedSortKey, serverSortOf } from '../../../shared/ui/data-table/data-table.types';
import { CatalogueBApi, type CatalogueBQuery, type CatalogueBResource } from '../data-b/catalogue-b.api';

/** The register filter of a hand-written Catalogue B page: search, active flag (`null` = both), page, list-specific extras. */
export interface CatalogueBListState {
  readonly search: string;
  readonly isActive: boolean | null;
  readonly page: number;
  readonly pageSize: number;
  readonly extra: Readonly<Record<string, FilterValue>>;
}

export const INITIAL_CATALOGUE_B_STATE: CatalogueBListState = { search: '', isActive: true, page: 1, pageSize: 50, extra: {} };

export function toQuery(state: CatalogueBListState, paged = true): CatalogueBQuery {
  return {
    search: state.search,
    isActive: state.isActive,
    ...(paged ? { page: state.page, pageSize: state.pageSize } : {}),
    ...state.extra,
  };
}

/**
 * Facade of 01 §6.3 for the hand-written Catalogue B registers (lists whose editor holds pickers): the paged list
 * through `resource()`, writes through `mutation()` with a reload, the CSV export with the current filter. Provided at
 * the route by each concrete facade.
 */
@Injectable()
export abstract class CatalogueBListFacade<TItem, TCreate, TUpdate> {
  private readonly i18n = inject(I18nService);

  protected abstract readonly endpoint: CatalogueBResource<TItem, TCreate, TUpdate>;

  /** The server keys of the list's sortable columns (validation v0.5.0 B-03); a column absent here keeps the list's own order. */
  protected abstract readonly sortKeys: SortKeyMap;

  readonly filter = signal<CatalogueBListState>(INITIAL_CATALOGUE_B_STATE);

  /** The column order chosen in the table header; the server sorts the whole list before paging. */
  readonly sort = signal<TableSort | null>(null);

  private readonly query = computed(() => ({ ...toQuery(this.filter()), ...sortParams(serverSortOf(this.sort(), this.sortKeys, () => this.i18n.lang())) }));

  readonly list = resource({
    params: () => this.query(),
    loader: ({ params, abortSignal }) => this.endpoint.list(params, abortSignal),
  });

  readonly rows = computed<readonly TItem[]>(() => (this.list.hasValue() ? this.list.value().items : []));

  readonly totalCount = computed(() => (this.list.hasValue() ? this.list.value().totalCount : 0));

  readonly create = mutation((request: TCreate) => this.endpoint.create(request), { then: () => this.list.reload() });

  readonly update = mutation((input: { readonly id: string; readonly request: TUpdate }) => this.endpoint.update(input.id, input.request), { then: () => this.list.reload() });

  readonly activation = mutation((input: { readonly id: string; readonly active: boolean }) => this.endpoint.setActive(input.id, input.active), { then: () => this.list.reload() });

  readonly exportCsv = mutation(() => this.endpoint.exportCsv(toQuery(this.filter(), false)));

  /** Re-reads the list when a page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14). A load in flight is left alone. */
  refresh(): void {
    this.list.reload();
  }

  setSearch(search: string): void {
    this.filter.update((current) => ({ ...current, search, page: 1 }));
  }

  setActiveFilter(isActive: boolean | null): void {
    this.filter.update((current) => ({ ...current, isActive, page: 1 }));
  }

  setExtra(name: string, value: FilterValue): void {
    this.filter.update((current) => ({ ...current, extra: { ...current.extra, [name]: value }, page: 1 }));
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

@Injectable()
export class PreparationsFacade extends CatalogueBListFacade<PreparationListItem, CreatePreparationRequest, UpdatePreparationRequest> {
  protected readonly endpoint = inject(CatalogueBApi).preparations;
  protected readonly sortKeys: SortKeyMap = { code: 'code', question: localizedSortKey('question') };
}

@Injectable()
export class DoctorsFacade extends CatalogueBListFacade<DoctorListItem, CreateDoctorRequest, UpdateDoctorRequest> {
  protected readonly endpoint = inject(CatalogueBApi).doctors;
  protected readonly sortKeys: SortKeyMap = { code: 'code', name: localizedSortKey('name'), specialty: 'specialty' };
}

@Injectable()
export class DiagnosesFacade extends CatalogueBListFacade<DiagnosisListItem, CreateDiagnosisRequest, UpdateDiagnosisRequest> {
  protected readonly endpoint = inject(CatalogueBApi).diagnoses;
  protected readonly sortKeys: SortKeyMap = { icdCode: 'icdCode', name: localizedSortKey('name') };
}

@Injectable()
export class ShortcutSetsFacade extends CatalogueBListFacade<ShortcutSetListItem, CreateShortcutSetRequest, UpdateShortcutSetRequest> {
  protected readonly endpoint = inject(CatalogueBApi).shortcutSets;
  protected readonly sortKeys: SortKeyMap = { code: 'code', name: localizedSortKey('name') };
}

@Injectable()
export class TestRulesFacade extends CatalogueBListFacade<TestRuleListItem, TestRuleRequest, TestRuleRequest> {
  private readonly api = inject(CatalogueBApi);

  protected readonly endpoint = this.api.testRules;
  protected readonly sortKeys: SortKeyMap = { name: localizedSortKey('name') };

  /** "Check rule" (CP-52 validate): errors and warnings without saving. */
  readonly validate = mutation((request: ValidateTestRuleRequest) => this.api.validateTestRule(request));
}
