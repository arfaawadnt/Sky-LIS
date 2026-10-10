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
