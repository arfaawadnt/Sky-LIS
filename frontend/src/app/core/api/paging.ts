/** Paging contracts mirrored from `Platform.Contracts.Common.Paging` (camelCase as the API serialises them). */

export interface Paged<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages: number;
}

/** The page a client asks for; the server normalises it (`PageRequest.Normalized`: page ≥ 1, size 1…200). */
export interface PageRequest {
  readonly page: number;
  readonly pageSize: number;
}

export const DEFAULT_PAGE_SIZE = 50;
export const MAX_PAGE_SIZE = 200;
export const PAGE_SIZES: readonly number[] = [20, 50, 100, 200];

export function firstPage(pageSize: number = DEFAULT_PAGE_SIZE): PageRequest {
  return { page: 1, pageSize };
}

export function emptyPage<T>(page: PageRequest = firstPage()): Paged<T> {
  return { items: [], page: page.page, pageSize: page.pageSize, totalCount: 0, totalPages: 0 };
}

export type SortDirection = 'asc' | 'desc';

/**
 * The server-side order of a paged list (validation v0.5.0 B-03): `sort` is a column key of the list's allow-list on the
 * server (`SortKeys`), `direction` `asc` or `desc`. The server sorts before it pages; absent = the list's own order.
 */
export interface SortRequest {
  readonly sort?: string | null;
  readonly direction?: SortDirection | null;
}
