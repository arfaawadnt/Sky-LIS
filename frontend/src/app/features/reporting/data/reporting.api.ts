import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { type MyPrivileges, ORGANIZATION_API, type SelectableBranch, type SelectableBranchesResponse } from '../../../core/api/organization.contracts';
import { REPORTING_API, type WorkAmountQuery, type WorkAmountReport } from '../../../core/api/reporting.contracts';
import { toHttpParams } from '../../../shared/forms/paging';

/** A file the server generated (the CSV export), with the name of its Content-Disposition. */
export interface DownloadedFile {
  readonly fileName: string;
  readonly blob: Blob;
}

/** Typed calls of the module; URLs are built with `HttpParams`, never by string concatenation (pitfall P-60). */
@Injectable({ providedIn: 'root' })
export class ReportingApi {
  private readonly http = inject(HttpClient);

  workAmount(query: WorkAmountQuery, signal?: AbortSignal): Promise<WorkAmountReport> {
    return firstValueFrom(this.http.get<WorkAmountReport>(REPORTING_API.workAmount, { params: params(query) })).then(abortable(signal));
  }

  /** The same report as a CSV file (`reporting.export`). */
  async workAmountCsv(query: WorkAmountQuery): Promise<DownloadedFile> {
    const response = await firstValueFrom(this.http.get(REPORTING_API.workAmountExport, { params: params(query), observe: 'response', responseType: 'blob' }));
    const disposition = response.headers.get('Content-Disposition') ?? '';
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
    return { fileName: match?.[1] ?? 'work-amount.csv', blob: response.body ?? new Blob([], { type: 'text/csv' }) };
  }

  /** The branches the session may select (CP-04, any signed-in user): the branch picker when there is more than one. */
  async selectableBranches(signal?: AbortSignal): Promise<readonly SelectableBranch[]> {
    const response = await firstValueFrom(this.http.get<SelectableBranchesResponse>(ORGANIZATION_API.selectableBranches), { defaultValue: { currentBranchId: null, branches: [] } }).then(abortable(signal));
    return response.branches;
  }

  /** The member's own privileges (Organization): the report days limit bounds the period the page offers. */
  myPrivileges(signal?: AbortSignal): Promise<MyPrivileges> {
    return firstValueFrom(this.http.get<MyPrivileges>(ORGANIZATION_API.myPrivileges)).then(abortable(signal));
  }
}

function params(query: WorkAmountQuery) {
  return toHttpParams({ from: query.from, to: query.to, groupBy: query.groupBy, branchId: query.branchId });
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
