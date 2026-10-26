import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { ORGANIZATION_API, type SelectableBranch, type SelectableBranchesResponse } from '../../../core/api/organization.contracts';
import { type LabTrackerPage, type LabTrackerQuery, TRACKER_API } from '../../../core/api/tracker.contracts';
import { toHttpParams } from '../../../shared/forms/paging';

/** Typed calls of the Lab Tracker; URLs are built with `HttpParams`, never by string concatenation (pitfall P-60). */
@Injectable({ providedIn: 'root' })
export class TrackerApi {
  private readonly http = inject(HttpClient);

  visits(query: LabTrackerQuery, signal?: AbortSignal): Promise<LabTrackerPage> {
    const params = toHttpParams({
      day: query.day,
      from: query.from,
      to: query.to,
      branchId: query.branchId,
      stage: query.stage,
      attention: query.attention,
      view: query.view,
      search: query.search,
      page: query.page,
      pageSize: query.pageSize,
      sort: query.sort,
      direction: query.direction,
      rows: query.rows ?? null,
    });
    return firstValueFrom(this.http.get<LabTrackerPage>(TRACKER_API.visits, { params })).then(abortable(signal));
  }

  /** The session branch for the header chip; tolerant (no chip when the list cannot be read). */
  async sessionBranch(branchId: string, signal?: AbortSignal): Promise<SelectableBranch | null> {
    try {
      const response = await firstValueFrom(this.http.get<SelectableBranchesResponse | readonly SelectableBranch[]>(ORGANIZATION_API.selectableBranches));
      abortable(signal)(response);
      const branches = Array.isArray(response) ? response : (response as SelectableBranchesResponse).branches;
      return branches.find((branch) => branch.id === branchId) ?? null;
    } catch {
      return null;
    }
  }
}

function abortable<T>(signal: AbortSignal | undefined): (value: T) => T {
  return (value) => {
    if (signal?.aborted) {
      throw new DOMException('aborted', 'AbortError');
    }

    return value;
  };
}
