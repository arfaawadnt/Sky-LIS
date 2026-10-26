import { DOCUMENT } from '@angular/common';
import { type DestroyRef, Injectable, InjectionToken, type ResourceRef, computed, inject, resource, signal } from '@angular/core';

import type { PageRequest } from '../../../core/api/paging';
import {
  EMPTY_TRACKER_COUNTERS,
  type LabTrackerAttention,
  type LabTrackerCounters,
  type LabTrackerItem,
  type LabTrackerPage,
  type LabTrackerQuery,
  type LabTrackerStage,
  type LabTrackerView,
  TRACKER_LANES,
} from '../../../core/api/tracker.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { I18nService } from '../../../core/i18n/i18n.service';
import { sortParams } from '../../../shared/forms/paging';
import { type SortKeyMap, type TableSort, serverSortOf } from '../../../shared/ui/data-table/data-table.types';
import { TrackerApi } from '../data/tracker.api';

/** The two layouts over the same data and filters: A · Pipeline board (table) and B · Stage lanes (board). */
export type TrackerLayout = 'table' | 'board';

/** The rows of the table (LIS-CR-014 T3): one per visit, or one per live top-level service line; the board stays visit-based. */
export type TrackerRowMode = 'visits' | 'services';

/**
 * The tracker filter as the page edits it: `day` null = today in the laboratory's time zone (the server's default); a
 * `from` / `to` range (laboratory wall-clock `yyyy-MM-ddTHH:mm`, both or neither) replaces the day.
 */
export interface TrackerFilter {
  readonly day: string | null;
  readonly from: string | null;
  readonly to: string | null;
  readonly stage: LabTrackerStage | null;
  readonly attention: LabTrackerAttention | null;
  readonly view: LabTrackerView | null;
  readonly search: string | null;
  readonly rows: TrackerRowMode;
  readonly page: number;
  readonly pageSize: number;
}

export function defaultTrackerFilter(): TrackerFilter {
  return { day: null, from: null, to: null, stage: null, attention: null, view: null, search: null, rows: 'visits', page: 1, pageSize: 50 };
}

/**
 * The server keys of the sortable table columns (allow-list `timeLeft`, `registeredAt`, `accession`, `patient`, `stage`;
 * `service` with service rows only, LIS-CR-014 T3).
 */
export const TRACKER_SORT_KEYS: SortKeyMap = {
  visit: 'accession',
  service: 'service',
  registration: 'registeredAt',
  nextStep: 'stage',
  timeLeft: 'timeLeft',
};

/** The table column that sorts by the service code: only with service rows. */
export const SERVICE_SORT_COLUMN = 'service';

/** Everything the tracker keeps in its URL (LIS-CR-014 T5): the filter with its rows mode and page, the column order and the layout. */
export interface TrackerUrlState {
  readonly filter: TrackerFilter;
  readonly sort: TableSort | null;
  readonly layout: TrackerLayout;
}

/** Cards per lane of the board; the rest is reached through "+ n more" (the table filtered by that stage). */
export const LANE_PAGE_SIZE = 20;

/** The tracker re-reads every 30 s while the tab is visible. */
export const POLL_INTERVAL_MS = 30_000;

/** The clock of the live label (one tick a second). */
export const CLOCK_TICK_MS = 1_000;

/** Timers and clock of the live tracker, replaceable in specs. */
export interface TrackerTimers {
  every(ms: number, task: () => void): () => void;
  now(): number;
}

export const TRACKER_TIMERS = new InjectionToken<TrackerTimers>('TRACKER_TIMERS', {
  providedIn: 'root',
  factory: () => ({
    every: (ms, task) => {
      const id = setInterval(task, ms);
      return () => clearInterval(id);
    },
    now: () => Date.now(),
  }),
});

/** One lane of the board: its stage and the first cards of that stage. */
export interface TrackerLane {
  readonly stage: LabTrackerStage;
  readonly page: ResourceRef<LabTrackerPage | undefined>;
}

/**
 * Lab Tracker (LIS-CR-006, CP-69): every visit of the day through registration → samples → results → payment. The table
 * reads one server page (paged and sorted on the server); the board reads one request per lane (`stage=<lane>`,
 * `pageSize=20`) and only while it is shown. The counters (strip, chips, view counts) are the day's, whichever request
 * answered last. Live: the page `connect`s the facade, which re-reads every 30 s while the document is visible, pauses
 * while it is hidden and re-reads on return.
 */
@Injectable()
export class TrackerFacade {
  private readonly api = inject(TrackerApi);
  private readonly auth = inject(AuthStore);
  private readonly i18n = inject(I18nService);
  private readonly timers = inject(TRACKER_TIMERS);
  private readonly document = inject(DOCUMENT);

  readonly filter = signal<TrackerFilter>(defaultTrackerFilter());

  /** The column order chosen in the table header; absent = the server's own (urgent first, then the time left). */
  readonly sort = signal<TableSort | null>(null);

  /** Table or board, remembered for the session (a root-lived signal would outlive a sign-out; the route facade is enough). */
  readonly layout = signal<TrackerLayout>('table');

  /** The time of the last answer and the ticking clock of the live label. */
  readonly loadedAt = signal<number | null>(null);
  readonly now = signal(this.timers.now());

  /** False while the tab is hidden (polling paused). */
  readonly live = signal(true);

  /** The laboratory day the server read (its default "today" in the laboratory's time zone when no day is chosen). */
  readonly servedDay = signal<string | null>(null);

  private readonly countersState = signal<LabTrackerCounters | null>(null);
  private requestedAt = this.timers.now();

  /** The query of the table: the filter, its page and the server order. */
  readonly query = computed<LabTrackerQuery>(() => {
    const filter = this.filter();
    return {
      ...this.baseQuery(),
      stage: filter.stage,
      page: filter.page,
      pageSize: filter.pageSize,
      rows: filter.rows === 'services' ? 'Services' : null,
      ...sortParams(serverSortOf(this.sort(), TRACKER_SORT_KEYS, () => this.i18n.lang())),
    };
  });

  /** The filters a lane shares with the table (no stage, page or order of its own). */
  private readonly baseQuery = computed<Omit<LabTrackerQuery, 'stage' | 'page' | 'pageSize'>>(() => {
    const filter = this.filter();
    return { day: filter.day, from: filter.from, to: filter.to, branchId: null, attention: filter.attention, view: filter.view, search: filter.search };
  });

  readonly list = resource({
    params: () => (this.layout() === 'table' ? this.query() : undefined),
    loader: ({ params, abortSignal }) => this.load(params, abortSignal),
  });

  /** The rows of the table: visits, or (service rows) the service lines, which carry every visit field too. */
  readonly rows = computed<readonly LabTrackerItem[]>(() => {
    if (!this.list.hasValue()) {
      return [];
    }

    const page = this.list.value();
    return this.filter().rows === 'services' ? (page.services ?? []) : page.items;
  });

  /** What the URL keeps (LIS-CR-014 T5); the page writes it with replace state and restores it on open. */
  readonly urlState = computed<TrackerUrlState>(() => ({ filter: this.filter(), sort: this.sort(), layout: this.layout() }));

  readonly totalCount = computed(() => (this.list.hasValue() ? this.list.value().totalCount : 0));

  /** The lanes of the board; a stage filter keeps its own lane only. */
  readonly lanes: readonly TrackerLane[] = TRACKER_LANES.map((stage) => ({
    stage,
    page: resource({
      params: (): LabTrackerQuery | undefined => {
        const filter = this.filter();
        return this.layout() === 'board' && (filter.stage === null || filter.stage === stage)
          ? { ...this.baseQuery(), stage, page: 1, pageSize: LANE_PAGE_SIZE, sort: null, direction: null }
          : undefined;
      },
      loader: ({ params, abortSignal }) => this.load(params, abortSignal),
    }),
  }));

  readonly visibleLanes = computed(() => {
    const stage = this.filter().stage;
    return this.lanes.filter((lane) => stage === null || lane.stage === stage);
  });

  readonly counters = computed<LabTrackerCounters>(() => this.countersState() ?? EMPTY_TRACKER_COUNTERS);

  readonly loading = computed(() => (this.layout() === 'table' ? this.list.isLoading() : this.lanes.some((lane) => lane.page.isLoading())));

  readonly error = computed<unknown>(() => (this.layout() === 'table' ? this.list.error() : (this.lanes.find((lane) => lane.page.error() !== undefined)?.page.error() ?? undefined)));

  /** Whole seconds since the last answer, null before the first one. */
  readonly secondsSinceUpdate = computed(() => {
    const loadedAt = this.loadedAt();
    return loadedAt === null ? null : Math.max(0, Math.floor((this.now() - loadedAt) / 1000));
  });

  /** The session branch for the header chip. */
  readonly branch = resource({
    params: () => this.auth.currentBranch() ?? undefined,
    loader: ({ params, abortSignal }) => this.api.sessionBranch(params, abortSignal),
  });

  /** One laboratory day (null = today); drops a range. */
  setDay(day: string | null): void {
    this.update({ day: day === null || day.length === 0 ? null : day, from: null, to: null });
  }

  /** A from / to range of laboratory wall-clock minutes; it replaces the day. */
  setRange(from: string, to: string): void {
    this.update({ day: null, from, to });
  }

  /** A stage tile: presses filter by that stage, a second press (or Registered) shows every stage again. */
  toggleStage(stage: LabTrackerStage | null): void {
    this.update({ stage: stage === null || this.filter().stage === stage ? null : stage });
  }

  toggleAttention(attention: LabTrackerAttention): void {
    this.update({ attention: this.filter().attention === attention ? null : attention });
  }

  /** A view tab: the preset replaces the stage filter (a preset and a stage seldom meet). */
  setView(view: LabTrackerView | null): void {
    this.update({ view, stage: null });
  }

  setSearch(search: string): void {
    const value = search.trim();
    this.update({ search: value.length === 0 ? null : value });
  }

  setPage(page: PageRequest): void {
    this.filter.update((current) => ({ ...current, page: page.page, pageSize: page.pageSize }));
  }

  /** A header click: the new order starts at page 1. */
  setSort(sort: TableSort | null): void {
    this.sort.set(sort);
    this.filter.update((current) => ({ ...current, page: 1 }));
  }

  setLayout(layout: TrackerLayout): void {
    this.layout.set(layout);
  }

  /** *Visits | Services* (LIS-CR-014 T3): the other rows start at page 1; the service-code order has no meaning for visits. */
  setRows(rows: TrackerRowMode): void {
    if (rows === this.filter().rows) {
      return;
    }

    if (rows === 'visits' && this.sort()?.key === SERVICE_SORT_COLUMN) {
      this.sort.set(null);
    }

    this.update({ rows });
  }

  /** The state read from the URL on open, on a return or on the browser's back / forward (LIS-CR-014 T5). */
  restore(state: TrackerUrlState): void {
    this.filter.set(state.filter);
    this.sort.set(state.sort);
    this.layout.set(state.layout);
  }

  /** "+ n more" on a lane: the table filtered by that stage. */
  showStage(stage: LabTrackerStage): void {
    this.update({ stage });
    this.layout.set('table');
  }

  clear(): void {
    this.filter.set(defaultTrackerFilter());
    this.sort.set(null);
  }

  /** Re-reads what is shown (the filter is kept): the facade outlives the page (route provider), so the previous visit's answer never stays on screen. */
  refresh(): void {
    this.reloadShown();
    this.branch.reload();
  }

  /**
   * Starts the live updates for the lifetime of the page: a one-second clock for the label, a re-read every 30 s while
   * the document is visible, nothing while it is hidden, an immediate re-read on return.
   */
  connect(destroyRef: DestroyRef): void {
    let stop: (() => void) | null = null;
    const start = (): void => {
      stop ??= this.timers.every(CLOCK_TICK_MS, () => this.tick());
    };
    const pause = (): void => {
      stop?.();
      stop = null;
    };
    const onVisibility = (): void => {
      if (this.document.visibilityState === 'hidden') {
        this.live.set(false);
        pause();
        return;
      }

      this.live.set(true);
      this.now.set(this.timers.now());
      this.reloadShown();
      start();
    };

    this.live.set(this.document.visibilityState !== 'hidden');
    if (this.live()) {
      start();
    }

    this.document.addEventListener('visibilitychange', onVisibility);
    destroyRef.onDestroy(() => {
      pause();
      this.document.removeEventListener('visibilitychange', onVisibility);
    });
  }

  private tick(): void {
    const now = this.timers.now();
    this.now.set(now);
    if (now - this.requestedAt >= POLL_INTERVAL_MS) {
      this.reloadShown();
    }
  }

  private reloadShown(): void {
    this.requestedAt = this.timers.now();
    if (this.layout() === 'table') {
      this.list.reload();
    } else {
      for (const lane of this.lanes) {
        lane.page.reload();
      }
    }
  }

  private async load(query: LabTrackerQuery, abortSignal: AbortSignal): Promise<LabTrackerPage> {
    this.requestedAt = this.timers.now();
    const page = await this.api.visits(query, abortSignal);
    const now = this.timers.now();
    this.countersState.set(page.counters);
    if (query.day === null && query.from === null) {
      this.servedDay.set(page.day); // the server's default day = the laboratory's today; a chosen day or range echoes its own first day
    }
    this.loadedAt.set(now);
    this.now.set(now);
    return page;
  }

  private update(change: Partial<Omit<TrackerFilter, 'page' | 'pageSize'>>): void {
    this.filter.update((current) => ({ ...current, ...change, page: 1 }));
  }
}
