import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { emptyPage } from '../../../core/api/paging';
import {
  type CreatedResource,
  type Paged,
  type RejectionReasonOption,
  SAMPLES_API,
  type SampleDetail,
  type SampleLabelDocument,
  type SampleListFilter,
  type SampleListItem,
  type SampleSettingsView,
  type SampleTimeline,
  type ScanMode,
  type VisitLabelsDocument,
  type VisitSamplesActionResult,
} from '../../../core/api/samples.contracts';
import { toHttpParams } from '../../../shared/forms/paging';

/** Typed calls of the Samples module; URLs are built with `HttpParams`, never by string concatenation (pitfall P-60). */
@Injectable({ providedIn: 'root' })
export class SamplesApi {
  private readonly http = inject(HttpClient);

  list(filter: SampleListFilter, signal?: AbortSignal): Promise<Paged<SampleListItem>> {
    const params = toHttpParams({
      visitId: filter.visitId,
      accession: filter.accession,
      status: filter.status,
      from: filter.from,
      to: filter.to,
      branchId: filter.branchId,
      clinicalGroupId: filter.clinicalGroupId,
      search: filter.search,
      page: filter.page,
      pageSize: filter.pageSize,
    });
    return firstValueFrom(this.http.get<Paged<SampleListItem>>(SAMPLES_API.samples, { params }), { defaultValue: emptyPage<SampleListItem>() }).then(abortable(signal));
  }

  get(id: string, signal?: AbortSignal): Promise<SampleDetail> {
    return firstValueFrom(this.http.get<SampleDetail>(SAMPLES_API.sample(id))).then(abortable(signal));
  }

  byNumber(sampleNumber: string, signal?: AbortSignal): Promise<SampleDetail> {
    return firstValueFrom(this.http.get<SampleDetail>(SAMPLES_API.byNumber(sampleNumber.trim()))).then(abortable(signal));
  }

  timeline(id: string, signal?: AbortSignal): Promise<SampleTimeline> {
    return firstValueFrom(this.http.get<SampleTimeline>(SAMPLES_API.timeline(id))).then(abortable(signal));
  }

  rejectedToday(branchId: string | null, utcOffsetMinutes: number, signal?: AbortSignal): Promise<readonly SampleListItem[]> {
    const params = toHttpParams({ branchId, utcOffsetMinutes });
    return firstValueFrom(this.http.get<readonly SampleListItem[]>(SAMPLES_API.rejectedToday, { params }), { defaultValue: [] }).then(abortable(signal));
  }

  settings(signal?: AbortSignal): Promise<SampleSettingsView> {
    return firstValueFrom(this.http.get<SampleSettingsView>(SAMPLES_API.settings)).then(abortable(signal));
  }

  scan(sampleNumber: string, mode: ScanMode): Promise<SampleDetail> {
    return firstValueFrom(this.http.post<SampleDetail>(SAMPLES_API.scan, { sampleNumber: sampleNumber.trim(), mode }));
  }

  collectAll(visitId: string): Promise<VisitSamplesActionResult> {
    return firstValueFrom(this.http.post<VisitSamplesActionResult>(SAMPLES_API.collectAll(visitId), null));
  }

  receiveAll(visitId: string): Promise<VisitSamplesActionResult> {
    return firstValueFrom(this.http.post<VisitSamplesActionResult>(SAMPLES_API.receiveAll(visitId), null));
  }

  reject(id: string, reasonId: string, note: string | null): Promise<void> {
    return firstValueFrom(this.http.post<void>(SAMPLES_API.reject(id), { reasonId, note }), { defaultValue: undefined });
  }

  unreject(id: string, note: string | null): Promise<void> {
    return firstValueFrom(this.http.post<void>(SAMPLES_API.unreject(id), { note }), { defaultValue: undefined });
  }

  replace(id: string, note: string | null): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(SAMPLES_API.replace(id), { note }));
  }

  label(id: string): Promise<SampleLabelDocument> {
    return firstValueFrom(this.http.get<SampleLabelDocument>(SAMPLES_API.label(id)));
  }

  visitLabels(visitId: string): Promise<VisitLabelsDocument> {
    return firstValueFrom(this.http.get<VisitLabelsDocument>(SAMPLES_API.visitLabels(visitId)));
  }

  /** Active rejection reasons of the catalogue (CP-53, needs catalogue.view); the page shows a hint when they cannot be loaded. */
  rejectionReasons(signal?: AbortSignal): Promise<readonly RejectionReasonOption[]> {
    const params = toHttpParams({ kind: 'Rejection', isActive: true, pageSize: 200 });
    return firstValueFrom(this.http.get<Paged<RejectionReasonOption>>(SAMPLES_API.rejectionReasons, { params }), { defaultValue: emptyPage<RejectionReasonOption>() })
      .then(abortable(signal))
      .then((page) => page.items.filter((reason) => reason.isActive && reason.kind.toLowerCase() === 'rejection'));
  }
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
