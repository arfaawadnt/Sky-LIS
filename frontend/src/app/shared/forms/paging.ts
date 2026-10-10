import { HttpParams } from '@angular/common/http';

import { DEFAULT_PAGE_SIZE, type PageRequest } from '../../core/api/paging';

/** A list filter that carries its page (the shape every facade's `filter` signal takes). */
export type PagedFilter<F extends object> = F & PageRequest;

/** A plain filter value that can travel as a query parameter; `null`/`undefined`/`''` are dropped. */
export type FilterValue = string | number | boolean | null | undefined;

/** `withPage(filter, { page: 2 })` → the same filter on another page; sizes default to the current one. */
export function withPage<F extends object>(filter: PagedFilter<F>, page: Partial<PageRequest>): PagedFilter<F> {
  return { ...filter, page: page.page ?? filter.page, pageSize: page.pageSize ?? filter.pageSize };
}

/** A filter change goes back to the first page (the usual `setStatus`/`setSearch` behaviour). */
export function resetPage<F extends object>(filter: PagedFilter<F>, change: Partial<F>): PagedFilter<F> {
  return { ...filter, ...change, page: 1 };
}

export function initialFilter<F extends object>(filter: F, pageSize: number = DEFAULT_PAGE_SIZE): PagedFilter<F> {
  return { ...filter, page: 1, pageSize };
}

/**
 * Builds `HttpParams` from a filter object, dropping empty values, so the URL is never concatenated by hand
 * (pitfall P-60): `toHttpParams({ status: null, mine: true, page: 1, pageSize: 50 })` → `mine=true&page=1&pageSize=50`.
 */
export function toHttpParams(filter: Readonly<Record<string, FilterValue>>): HttpParams {
  const fromObject: Record<string, string | number | boolean> = {};
  for (const [key, value] of Object.entries(filter)) {
    if (value === null || value === undefined || value === '') {
      continue;
    }

    fromObject[key] = value;
  }

  return new HttpParams({ fromObject });
}
