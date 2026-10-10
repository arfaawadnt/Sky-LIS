import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import {
  type CreateTreasuryRequest,
  type CreatedResource,
  type Paged,
  type RenameTreasuryRequest,
  type RequestTreasuryAccessRequest,
  TREASURY_API,
  type TreasuryAccessRequestFilter,
  type TreasuryAccessRequestListItem,
  type TreasuryListFilter,
  type TreasuryListItem,
} from '../../../core/api/treasury.contracts';
import { type BranchListItem, ORGANIZATION_API } from '../../../core/api/organization.contracts';
import { toHttpParams } from '../../../shared/forms/paging';

/** Typed calls of the module; URLs are built with `HttpParams`, never by string concatenation (pitfall P-60). */
@Injectable({ providedIn: 'root' })
export class TreasuryApi {
  private readonly http = inject(HttpClient);

  listTreasuries(isActive: boolean | null, signal?: AbortSignal): Promise<Paged<TreasuryListItem>> {
    const params = new HttpParams({ fromObject: isActive === null ? { pageSize: 200 } : { isActive, pageSize: 200 } });
    return firstValueFrom(this.http.get<Paged<TreasuryListItem>>(TREASURY_API.treasuries, { params }), { defaultValue: emptyPage<TreasuryListItem>() }).then(abortable(signal));
  }

  /** The paged catalogue (CP-28); the API has no text search, the page filters client-side. */
  listTreasuriesPage(filter: TreasuryListFilter, signal?: AbortSignal): Promise<Paged<TreasuryListItem>> {
    const params = toHttpParams({ isActive: filter.isActive, page: filter.page, pageSize: filter.pageSize });
    return firstValueFrom(this.http.get<Paged<TreasuryListItem>>(TREASURY_API.treasuries, { params }), { defaultValue: emptyPage<TreasuryListItem>() }).then(abortable(signal));
  }

  /** The active branches of the laboratory, for the branch picker of the treasury form (CP-28: a treasury belongs to one branch). */
  async activeBranches(signal?: AbortSignal): Promise<readonly BranchListItem[]> {
    const params = toHttpParams({ search: '', isActive: true, page: 1, pageSize: 200 });
    const page = await firstValueFrom(this.http.get<Paged<BranchListItem>>(ORGANIZATION_API.branches, { params }), { defaultValue: emptyPage<BranchListItem>() }).then(abortable(signal));
    return page.items;
  }

  createTreasury(request: CreateTreasuryRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(TREASURY_API.treasuries, request));
  }

  renameTreasury(id: string, request: RenameTreasuryRequest): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${TREASURY_API.treasuries}/${encodeURIComponent(id)}/name`, request), { defaultValue: undefined }).then(() => undefined);   // a 204 body is null; null means failure to mutation()
  }

  deactivateTreasury(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${TREASURY_API.treasuries}/${encodeURIComponent(id)}/deactivate`, null), { defaultValue: undefined }).then(() => undefined);   // a 204 body is null; null means failure to mutation()
  }

  reactivateTreasury(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${TREASURY_API.treasuries}/${encodeURIComponent(id)}/reactivate`, null), { defaultValue: undefined }).then(() => undefined);   // a 204 body is null; null means failure to mutation()
  }

  listRequests(filter: TreasuryAccessRequestFilter, signal?: AbortSignal): Promise<Paged<TreasuryAccessRequestListItem>> {
    const params = new HttpParams({
      fromObject: {
        mine: filter.mine,
        page: filter.page,
        pageSize: filter.pageSize,
        ...(filter.status === null ? {} : { status: filter.status }),
      },
    });
    return firstValueFrom(this.http.get<Paged<TreasuryAccessRequestListItem>>(TREASURY_API.requests, { params }), { defaultValue: emptyPage<TreasuryAccessRequestListItem>() }).then(abortable(signal));
  }

  requestAccess(request: RequestTreasuryAccessRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(TREASURY_API.requests, request));
  }

  approve(id: string, comment: string | null): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${TREASURY_API.requests}/${encodeURIComponent(id)}/approve`, { comment }), { defaultValue: undefined }).then(() => undefined);   // a 204 body is null; null means failure to mutation()
  }

  reject(id: string, reason: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${TREASURY_API.requests}/${encodeURIComponent(id)}/reject`, { reason }), { defaultValue: undefined }).then(() => undefined);   // a 204 body is null; null means failure to mutation()
  }

  revoke(id: string, reason: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${TREASURY_API.requests}/${encodeURIComponent(id)}/revoke`, { reason }), { defaultValue: undefined }).then(() => undefined);   // a 204 body is null; null means failure to mutation()
  }

  cancel(id: string, reason: string | null): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${TREASURY_API.requests}/${encodeURIComponent(id)}/cancel`, { reason }), { defaultValue: undefined }).then(() => undefined);   // a 204 body is null; null means failure to mutation()
  }
}

function emptyPage<T>(): Paged<T> {
  return { items: [], page: 1, pageSize: 0, totalCount: 0, totalPages: 0 };
}

/** Resources pass an abort signal; HttpClient has no fetch-level abort here, so a late result for an aborted load is dropped. */
function abortable<T>(signal: AbortSignal | undefined): (value: T) => T {
  return (value) => {
    if (signal?.aborted) {
      throw new DOMException('aborted', 'AbortError');
    }

    return value;
  };
}
