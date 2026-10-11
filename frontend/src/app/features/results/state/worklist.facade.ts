import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { PageRequest } from '../../../core/api/paging';
import type { ResultStatus, SampleStatusName, WorklistItem, WorklistQuery } from '../../../core/api/results.contracts';
import { mutation } from '../../../core/state/mutation';
import { ResultsApi } from '../data/results.api';
import { localDayStart, today } from '../ui/result-format';
import { WorklistExport } from './worklist-export';

/** The worklist filter as the page edits it: local calendar days for the collection window (LDM §6 "filter by collection date"). */
export interface WorklistFilter {
  readonly fromDay: string | null;
  readonly toDay: string | null;
  readonly clinicalGroupId: string | null;
  readonly testId: string | null;
  readonly analyserId: string | null;
  readonly sampleStatus: SampleStatusName | null;
  readonly payerId: string | null;
  readonly statuses: readonly ResultStatus[];
  readonly delayedOnly: boolean;
  readonly search: string | null;
  readonly branchOnly: boolean;
  readonly page: number;
  readonly pageSize: number;
}

export function defaultWorklistFilter(day: string = today()): WorklistFilter {
  return {
    fromDay: day,
    toDay: day,
    clinicalGroupId: null,
    testId: null,
    analyserId: null,
    sampleStatus: null,
    payerId: null,
    statuses: [],
    delayedOnly: false,
    search: null,
    branchOnly: true,
    page: 1,
    pageSize: 50,
  };
}

/**
 * CP-18 Worklist (01 §6.3): one row per test of the collection window with the BR-13 delay colour, the counters
 * (requests, tests, samples, delayed), the status boxes and the filter lists; the bench-list document and a CSV of the
 * current filter. Reads through `resource()`, the document through `mutation()`.
 */
@Injectable()
export class WorklistFacade {
  private readonly api = inject(ResultsApi);
  private readonly exporter = inject(WorklistExport);

  readonly filter = signal<WorklistFilter>(defaultWorklistFilter());

  /** The API query: the "to" day is inclusive, so its window ends at the next local midnight. */
  readonly query = computed<WorklistQuery>(() => {
    const filter = this.filter();
    return {
      from: localDayStart(filter.fromDay),
      to: localDayStart(filter.toDay, 1),
      clinicalGroupId: filter.clinicalGroupId,
      testId: filter.testId,
      analyserId: filter.analyserId,
      sampleStatus: filter.sampleStatus,
      payerId: filter.payerId,
      statuses: filter.statuses,
      delayedOnly: filter.delayedOnly,
      search: filter.search,
      branchOnly: filter.branchOnly,
      page: filter.page,
      pageSize: filter.pageSize,
    };
  });

  readonly list = resource({
    params: () => this.query(),
    loader: ({ params, abortSignal }) => this.api.worklist(params, abortSignal),
  });

  readonly rows = computed<readonly WorklistItem[]>(() => (this.list.hasValue() ? this.list.value().items : []));

  readonly groups = resource({ loader: ({ abortSignal }) => this.api.clinicalGroups(abortSignal) });

  /** Isolated so the test list reloads only when the group changes, not on every filter edit (a resource re-runs when any signal its params read notifies). */
  private readonly groupId = computed(() => this.filter().clinicalGroupId);

  readonly tests = resource({
    params: () => this.groupId(),
    loader: ({ params, abortSignal }) => this.api.tests(params, null, abortSignal),
  });

  readonly payers = resource({ loader: ({ abortSignal }) => this.api.payers(abortSignal) });

  readonly analysers = resource({ loader: ({ abortSignal }) => this.api.analysers(abortSignal) });

  readonly document = mutation(() => this.api.worklistDocument({ ...this.query(), page: 1 }));

  /** CSV of every row of the current filter (client side, capped), named after the collection window. */
  readonly exportCsv = mutation(() => {
    const filter = this.filter();
    return this.exporter.exportQuery(this.query(), `worklist-${filter.fromDay ?? 'all'}-${filter.toDay ?? 'all'}.csv`);
  });

  update(change: Partial<Omit<WorklistFilter, 'page' | 'pageSize'>>): void {
    this.filter.update((current) => ({ ...current, ...change, page: 1 }));
  }

  setGroup(clinicalGroupId: string | null): void {
    this.update({ clinicalGroupId, testId: null });
  }

  /** The status boxes (LDM §6): any number of statuses; none = all. */
  toggleStatus(status: ResultStatus): void {
    const current = this.filter().statuses;
    this.update({ statuses: current.includes(status) ? current.filter((item) => item !== status) : [...current, status] });
  }

  setSearch(search: string): void {
    const value = search.trim();
    this.update({ search: value.length === 0 ? null : value.toUpperCase() });
  }

  setPage(page: PageRequest): void {
    this.filter.update((current) => ({ ...current, page: page.page, pageSize: page.pageSize }));
  }

  clear(): void {
    this.filter.set(defaultWorklistFilter());
  }

  /** Re-reads the rows and the filter lists when the page opens again (the filter itself is kept): the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's, with its result rights — must never stay on screen (UAT D-14, A-01). A load in flight is left alone. */
  refresh(): void {
    this.document.reset();
    this.exportCsv.reset();
    this.list.reload();
    this.groups.reload();
    this.tests.reload();
    this.payers.reload();
    this.analysers.reload();
  }
}
