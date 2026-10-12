import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { PageRequest } from '../../../core/api/paging';
import { ProblemError, type ProblemDetails } from '../../../core/api/problem-details';
import type { SampleDetail, SampleListFilter, SampleStatus, ScanMode, VisitSamplesActionResult } from '../../../core/api/samples.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { mutation } from '../../../core/state/mutation';
import { SamplesApi } from '../data/samples.api';
import { normalizeSampleNumber, todayWindow } from '../ui/sample-format';
import { SampleListExport } from './sample-list-export';
import { I18nService } from '../../../core/i18n/i18n.service';
import { sortParams } from '../../../shared/forms/paging';
import { type SortKeyMap, type TableSort, serverSortOf } from '../../../shared/ui/data-table/data-table.types';

/** The server keys of the sortable columns of today's samples (B-03, SamplesSortKeys.Samples). */
export const COLLECTION_SORT_KEYS: SortKeyMap = { sampleNumber: 'sampleNumber', accession: 'accession', status: 'status' };

/** The outcome of one scan, for the feedback strip and the session log of CP-14. */
export interface ScanOutcome {
  readonly id: number;
  readonly sampleNumber: string;
  readonly mode: ScanMode;
  readonly ok: boolean;
  readonly detail: SampleDetail | null;
  readonly problem: ProblemDetails | null;
}

export type BulkAction = 'collect' | 'receive';

export interface TodayFilter {
  readonly status: SampleStatus | null;
  readonly branchOnly: boolean;
  readonly search: string | null;
  readonly page: number;
  readonly pageSize: number;
}

const LOG_SIZE = 20;
let nextOutcomeId = 0;

/**
 * CP-14 Collection & receipt (01 §6.3): the scan box (mode, last outcome, session log), the bulk actions of an
 * accession, and today's list of the session branch. Reads through `resource()`, writes through `mutation()`.
 */
@Injectable()
export class CollectionFacade {
  private readonly i18n = inject(I18nService);
  private readonly api = inject(SamplesApi);
  private readonly auth = inject(AuthStore);
  private readonly exporter = inject(SampleListExport);

  readonly mode = signal<ScanMode>('Collect');
  readonly outcomes = signal<readonly ScanOutcome[]>([]);
  readonly lastOutcome = computed(() => this.outcomes()[0] ?? null);

  readonly filter = signal<TodayFilter>({ status: null, branchOnly: true, search: null, page: 1, pageSize: 50 });

  /** The column order chosen in the table header; the server sorts the whole list before paging (validation v0.5.0 B-03). */
  readonly sort = signal<TableSort | null>(null);

  /** The list filter as the API takes it: today (local day) and, by default, the session branch. */
  readonly listFilter = computed<SampleListFilter>(() => {
    const filter = this.filter();
    const window = todayWindow();
    return {
      visitId: null,
      accession: null,
      status: filter.status,
      from: window.from,
      to: window.to,
      branchId: filter.branchOnly ? this.auth.currentBranch() : null,
      clinicalGroupId: null,
      search: filter.search,
      page: filter.page,
      pageSize: filter.pageSize,
      ...sortParams(serverSortOf(this.sort(), COLLECTION_SORT_KEYS, () => this.i18n.lang())),
    };
  });

  readonly today = resource({
    params: () => this.listFilter(),
    loader: ({ params, abortSignal }) => this.api.list(params, abortSignal),
  });

  readonly settings = resource({
    loader: ({ abortSignal }) => this.api.settings(abortSignal),
  });

  readonly scan = mutation((input: { sampleNumber: string; mode: ScanMode }) => this.api.scan(input.sampleNumber, input.mode), { then: () => this.today.reload() });

  readonly bulk = mutation(
    async (input: { accession: string; action: BulkAction }): Promise<VisitSamplesActionResult & { readonly accession: string }> => {
      const visitId = await this.visitOf(input.accession);
      const result = input.action === 'collect' ? await this.api.collectAll(visitId) : await this.api.receiveAll(visitId);
      return { ...result, accession: input.accession };
    },
    { then: () => this.today.reload() },
  );

  readonly exporting = mutation((fileName: string) => this.exporter.exportFilter({ ...this.listFilter(), page: 1 }, fileName));

  setMode(mode: ScanMode): void {
    this.mode.set(mode);
  }

  /** Scans one barcode in the current mode and records the outcome (success or the refusal code). */
  async scanNumber(raw: string): Promise<ScanOutcome | null> {
    const sampleNumber = normalizeSampleNumber(raw);
    if (sampleNumber.length === 0 || this.scan.pending()) {
      return null;
    }

    const mode = this.mode();
    const detail = await this.scan.execute({ sampleNumber, mode });
    const outcome: ScanOutcome = { id: ++nextOutcomeId, sampleNumber, mode, ok: detail !== null, detail, problem: detail === null ? this.scan.problem() : null };
    this.outcomes.update((current) => [outcome, ...current].slice(0, LOG_SIZE));
    return outcome;
  }

  clearLog(): void {
    this.outcomes.set([]);
  }

  /** Re-reads today's list and the parameters when the page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14, A-01). A load in flight is left alone. */
  refresh(): void {
    this.scan.reset();
    this.bulk.reset();
    this.today.reload();
    this.settings.reload();
  }

  setStatus(status: SampleStatus | null): void {
    this.filter.update((current) => ({ ...current, status, page: 1 }));
  }

  setBranchOnly(branchOnly: boolean): void {
    this.filter.update((current) => ({ ...current, branchOnly, page: 1 }));
  }

  setSearch(search: string): void {
    const value = search.trim();
    this.filter.update((current) => ({ ...current, search: value.length === 0 ? null : value.toUpperCase(), page: 1 }));
  }

  setPage(page: PageRequest): void {
    this.filter.update((current) => ({ ...current, page: page.page, pageSize: page.pageSize }));
  }

  /** A header click: the new order starts at page 1. */
  setSort(sort: TableSort | null): void {
    this.sort.set(sort);
    this.filter.update((current) => ({ ...current, page: 1 }));
  }

  /** The visit of an accession, from the samples register (the accession is on every sample). */
  private async visitOf(accession: string): Promise<string> {
    const page = await this.api.list({ visitId: null, accession: accession.trim(), status: null, from: null, to: null, branchId: null, clinicalGroupId: null, search: null, page: 1, pageSize: 1 });
    const first = page.items[0];
    if (!first) {
      throw new ProblemError({ type: 'about:blank', title: 'No samples were found for this visit.', status: 404, code: 'SMP-405', traceId: null, correlationId: null, errors: null }, 404);
    }

    return first.visitId;
  }
}
