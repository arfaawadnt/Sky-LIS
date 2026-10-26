import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import type { CreatedResource } from '../../../core/api/created-resource';
import { emptyPage, type Paged } from '../../../core/api/paging';
import {
  type CreateSampleCompatibilityGroupRequest,
  SAMPLE_COMPATIBILITY_API,
  type SampleCompatibilityGroupView,
  type UpdateSampleCompatibilityGroupRequest,
} from '../../../core/api/sample-compatibility.contracts';
import { type FilterValue, toHttpParams } from '../../../shared/forms/paging';

/** The query of the register: search, active flag (null = both), page and the allow-listed server order. */
export type SampleCompatibilityQuery = Readonly<Record<string, FilterValue>>;

/** Compatibility groups of samples (LIS-CR-015 M4); URLs are built with `HttpParams` and encoded ids (pitfall P-60). */
@Injectable({ providedIn: 'root' })
export class SampleCompatibilityApi {
  private readonly http = inject(HttpClient);

  list(query: SampleCompatibilityQuery, signal?: AbortSignal): Promise<Paged<SampleCompatibilityGroupView>> {
    return firstValueFrom(this.http.get<Paged<SampleCompatibilityGroupView>>(SAMPLE_COMPATIBILITY_API, { params: toHttpParams(query) }), { defaultValue: emptyPage<SampleCompatibilityGroupView>() }).then(abortable(signal));
  }

  /** The rows of the current search and status filter (the export is the grid). */
  export(query: SampleCompatibilityQuery): Promise<readonly SampleCompatibilityGroupView[]> {
    return firstValueFrom(this.http.get<readonly SampleCompatibilityGroupView[]>(`${SAMPLE_COMPATIBILITY_API}/export`, { params: toHttpParams(query) }), { defaultValue: [] });
  }

  create(request: CreateSampleCompatibilityGroupRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(SAMPLE_COMPATIBILITY_API, request));
  }

  /** A 204 arrives as a null body; it resolves to undefined. */
  update(id: string, request: UpdateSampleCompatibilityGroupRequest): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${SAMPLE_COMPATIBILITY_API}/${encodeURIComponent(id)}`, request), { defaultValue: undefined }).then(() => undefined);
  }

  setActive(id: string, active: boolean): Promise<void> {
    const action = active ? 'reactivate' : 'deactivate';
    return firstValueFrom(this.http.post<void>(`${SAMPLE_COMPATIBILITY_API}/${encodeURIComponent(id)}/${action}`, null), { defaultValue: undefined }).then(() => undefined);
  }
}

/** Resources pass an abort signal; a late result for an aborted load is dropped. */
function abortable<T>(signal: AbortSignal | undefined): (value: T) => T {
  return (value) => {
    if (signal?.aborted) {
      throw new DOMException('aborted', 'AbortError');
    }

    return value;
  };
}
