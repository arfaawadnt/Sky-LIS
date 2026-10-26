import { TRACKER_ATTENTION, type LabTrackerAttention, type LabTrackerStage, type LabTrackerView } from '../../../core/api/tracker.contracts';
import type { TableSort } from '../../../shared/ui/data-table/data-table.types';

import { TRACKER_SORT_KEYS, type TrackerFilter, type TrackerUrlState, defaultTrackerFilter, sortColumnFits } from './tracker.facade';

// LIS-CR-014 T5: the whole tracker filter lives in the URL query (`/tracker?day=…&stage=…`), so a page opened from a row and
// left through its "Back to Lab Tracker" chip (`returnTo`) comes back with the same criteria. Only values that differ from
// the defaults are written; anything unknown or malformed in a URL is dropped (the page then rewrites a clean URL).

/** The query keys the tracker owns. */
export const TRACKER_URL_KEYS = ['day', 'from', 'to', 'stage', 'attention', 'view', 'search', 'rows', 'page', 'size', 'sort', 'dir', 'layout'] as const;

/** A read-only view of a URL query (Angular's `ParamMap` fits). */
export interface QueryReader {
  get(name: string): string | null;
  has(name: string): boolean;
}

const STAGES: readonly LabTrackerStage[] = ['Cancelled', 'AwaitingSample', 'InLab', 'AwaitingVerification', 'Ready', 'Delivered'];
const VIEWS: readonly LabTrackerView[] = ['Reception', 'Verification', 'FrontDesk'];
const DAY = /^\d{4}-\d{2}-\d{2}$/;
const MINUTE = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/;

/** The longest search the server accepts (`GetLabTrackerValidator.MaxSearchLength`). */
export const TRACKER_SEARCH_MAX = 60;

/** The largest page the server accepts (`PageRequest.MaxPageSize`). */
export const TRACKER_PAGE_SIZE_MAX = 200;

/** The query of a state: only what differs from the defaults (an untouched tracker has an empty query). */
export function trackerUrlQuery(state: TrackerUrlState): Record<string, string> {
  const { filter, sort, layout } = state;
  const defaults = defaultTrackerFilter();
  const query: Record<string, string> = {};
  const put = (key: string, value: string | null): void => {
    if (value !== null && value !== '') {
      query[key] = value;
    }
  };

  if (filter.from !== null && filter.to !== null) {
    put('from', filter.from);
    put('to', filter.to);
  } else {
    put('day', filter.day);
  }

  put('stage', filter.stage);
  put('attention', filter.attention);
  put('view', filter.view);
  put('search', filter.search);
  put('rows', filter.rows === 'services' ? 'services' : null);
  put('page', filter.page > 1 ? String(filter.page) : null);
  put('size', filter.pageSize !== defaults.pageSize ? String(filter.pageSize) : null);
  if (sort !== null) {
    put('sort', sort.key);
    put('dir', sort.direction === 'desc' ? 'desc' : null);
  }

  put('layout', layout === 'board' ? 'board' : null);
  return query;
}

/**
 * The state a URL query names, or null when it holds none of the tracker's keys (the page then keeps what it has). A
 * malformed value is dropped: the default stands for it.
 */
export function trackerUrlState(params: QueryReader): TrackerUrlState | null {
  if (!TRACKER_URL_KEYS.some((key) => params.has(key))) {
    return null;
  }

  const defaults = defaultTrackerFilter();
  const text = (key: string): string | null => {
    const value = params.get(key)?.trim() ?? '';
    return value === '' ? null : value;
  };
  const oneOf = <T extends string>(key: string, values: readonly T[]): T | null => {
    const value = text(key);
    return value !== null && (values as readonly string[]).includes(value) ? (value as T) : null;
  };
  const whole = (key: string, max: number): number | null => {
    const value = text(key);
    const number = value !== null && /^\d{1,4}$/.test(value) ? Number(value) : NaN;
    return Number.isInteger(number) && number >= 1 && number <= max ? number : null;
  };

  const from = text('from');
  const to = text('to');
  const range = from !== null && to !== null && MINUTE.test(from) && MINUTE.test(to) && from <= to;
  const day = text('day');
  const search = text('search');
  const rows = text('rows') === 'services' ? 'services' : 'visits';

  const filter: TrackerFilter = {
    day: !range && day !== null && DAY.test(day) ? day : null,
    from: range ? from : null,
    to: range ? to : null,
    stage: oneOf<LabTrackerStage>('stage', STAGES),
    attention: oneOf<LabTrackerAttention>('attention', TRACKER_ATTENTION),
    view: oneOf<LabTrackerView>('view', VIEWS),
    search: search !== null && search.length <= TRACKER_SEARCH_MAX ? search : null,
    rows,
    page: whole('page', 9999) ?? defaults.page,
    pageSize: whole('size', TRACKER_PAGE_SIZE_MAX) ?? defaults.pageSize,
  };

  const sortKey = text('sort');
  const sortable = sortKey !== null && Object.hasOwn(TRACKER_SORT_KEYS, sortKey) && sortColumnFits(sortKey, rows);
  const sort: TableSort | null = sortable ? { key: sortKey, direction: text('dir') === 'desc' ? 'desc' : 'asc' } : null;

  return { filter, sort, layout: text('layout') === 'board' ? 'board' : 'table' };
}

/** True when two queries hold the same keys and values (order ignored). */
export function sameQuery(left: Readonly<Record<string, string>>, right: Readonly<Record<string, string>>): boolean {
  const keys = Object.keys(left);
  return keys.length === Object.keys(right).length && keys.every((key) => right[key] === left[key]);
}

/** The tracker's own keys of a URL query, as a plain record (other keys, if any, are not the tracker's). */
export function trackerQueryOf(params: QueryReader): Record<string, string> {
  const query: Record<string, string> = {};
  for (const key of TRACKER_URL_KEYS) {
    const value = params.get(key);
    if (value !== null) {
      query[key] = value;
    }
  }

  return query;
}
