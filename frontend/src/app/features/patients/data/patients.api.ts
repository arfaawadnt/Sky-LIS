import { HttpClient, HttpParams, type HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { emptyPage, type Paged } from '../../../core/api/paging';
import {
  type MergePatientsRequest,
  PATIENTS_API,
  type PatientDetail,
  type PatientDocumentItem,
  type PatientListFilter,
  type PatientListItem,
  type PatientVisitItem,
  type UpdatePatientRequest,
} from '../../../core/api/patients.contracts';
import { toHttpParams } from '../../../shared/forms/paging';
import { abortable, itemsOf } from '../../../shared/patient/patient-lookup.api';

/** A downloaded file: its bytes and the name the server suggested (or the fallback). */
export interface DownloadedFile {
  readonly blob: Blob;
  readonly fileName: string;
}

/** Typed calls of the patients pages (CP-11); URLs are built with `HttpParams`, never by string concatenation (pitfall P-60). */
@Injectable({ providedIn: 'root' })
export class PatientsApi {
  private readonly http = inject(HttpClient);

  list(filter: PatientListFilter, signal?: AbortSignal): Promise<Paged<PatientListItem>> {
    return firstValueFrom(this.http.get<Paged<PatientListItem>>(PATIENTS_API.patients, { params: listParams(filter) }), { defaultValue: emptyPage<PatientListItem>() }).then(abortable(signal));
  }

  get(id: string, signal?: AbortSignal): Promise<PatientDetail> {
    return firstValueFrom(this.http.get<PatientDetail>(`${PATIENTS_API.patients}/${encodeURIComponent(id)}`)).then(abortable(signal));
  }

  update(id: string, request: UpdatePatientRequest): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${PATIENTS_API.patients}/${encodeURIComponent(id)}`, request), { defaultValue: undefined }).then(done);
  }

  /** T3: `survivorId` keeps its number; `request.mergedPatientId` becomes Merged. */
  merge(survivorId: string, request: MergePatientsRequest): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${PATIENTS_API.patients}/${encodeURIComponent(survivorId)}/merge`, request), { defaultValue: undefined }).then(done);
  }

  /** CSV of the register with the same filter (without paging); `patients.export`. */
  async export(filter: PatientListFilter): Promise<DownloadedFile> {
    const response = await firstValueFrom(this.http.get(`${PATIENTS_API.patients}/export`, { params: listParams(filter, false), observe: 'response', responseType: 'blob' }));
    // Named with the local date like every other export (UAT R3-05), not the server's UTC time stamp.
    return { ...downloaded(response, 'patients.csv'), fileName: localCsvName('patients') };
  }

  documents(patientId: string, includeInactive: boolean, signal?: AbortSignal): Promise<readonly PatientDocumentItem[]> {
    const params = new HttpParams({ fromObject: { includeInactive } });
    return firstValueFrom(this.http.get<readonly PatientDocumentItem[]>(`${PATIENTS_API.patients}/${encodeURIComponent(patientId)}/documents`, { params }), { defaultValue: [] }).then(abortable(signal));
  }

  /** T4: multipart `file` + optional `note`. */
  upload(patientId: string, file: File, note: string | null): Promise<{ readonly id: string }> {
    const form = new FormData();
    form.append('file', file, file.name);
    if (note !== null && note.trim() !== '') {
      form.append('note', note.trim());
    }

    return firstValueFrom(this.http.post<{ readonly id: string }>(`${PATIENTS_API.patients}/${encodeURIComponent(patientId)}/documents`, form));
  }

  async download(document: PatientDocumentItem): Promise<DownloadedFile> {
    const response = await firstValueFrom(this.http.get(`${PATIENTS_API.documents}/${encodeURIComponent(document.id)}`, { observe: 'response', responseType: 'blob' }));
    return { blob: response.body ?? new Blob([], { type: document.contentType }), fileName: document.fileName };
  }

  /** T5 (BR-15): never a DELETE. */
  deactivateDocument(documentId: string, reason: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${PATIENTS_API.documents}/${encodeURIComponent(documentId)}/deactivate`, { reason }), { defaultValue: undefined }).then(done);
  }

  /**
   * The visits of the file from the Registration module (wave 2). Until that API exists the call fails (404) and the
   * caller shows an empty state; rows are read tolerantly (`accession`/`accessionNumber`, `registeredAtUtc`/`createdAtUtc`).
   */
  visits(patientId: string, signal?: AbortSignal): Promise<readonly PatientVisitItem[]> {
    const params = new HttpParams({ fromObject: { patientId, page: 1, pageSize: 100 } });
    return firstValueFrom(this.http.get<unknown>(PATIENTS_API.visits, { params }), { defaultValue: [] })
      .then((body) => itemsOf(body).map(toVisit).filter((visit): visit is PatientVisitItem => visit !== null))
      .then(abortable(signal));
  }
}

/** A 204 arrives as a `null` body; a void call resolves to `undefined` so `mutation()` (whose failure is `null`) reports success. */
function done(): undefined {
  return undefined;
}

function listParams(filter: PatientListFilter, paged = true): HttpParams {
  return toHttpParams({
    q: filter.q.trim(),
    gender: filter.gender,
    vip: filter.vip,
    status: filter.status,
    page: paged ? filter.page : null,
    pageSize: paged ? filter.pageSize : null,
    sort: paged ? (filter.sort ?? null) : null,
    direction: paged ? (filter.direction ?? null) : null,
  });
}

function localCsvName(base: string, now: Date = new Date()): string {
  const pad = (value: number): string => String(value).padStart(2, '0');
  return `${base}-${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}.csv`;
}

function downloaded(response: HttpResponse<Blob>, fallback: string): DownloadedFile {
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(disposition)?.[1];
  const plain = /filename="?([^";]+)"?/i.exec(disposition)?.[1];
  return { blob: response.body ?? new Blob([]), fileName: encoded ? decodeURIComponent(encoded) : (plain ?? fallback) };
}

function toVisit(value: Record<string, unknown>): PatientVisitItem | null {
  const id = typeof value['id'] === 'string' ? value['id'] : null;
  const accession = typeof value['accession'] === 'string' ? value['accession'] : typeof value['accessionNumber'] === 'string' ? value['accessionNumber'] : null;
  if (id === null || accession === null) {
    return null;
  }

  const text = (key: string): string | null => (typeof value[key] === 'string' ? (value[key] as string) : null);
  const lines = value['lines'];
  return {
    id,
    accession,
    registeredAtUtc: text('registeredAtUtc') ?? text('createdAtUtc'),
    status: text('status'),
    branchName: text('branchNameEn') ?? text('branchName'),
    branchNameAr: text('branchNameAr'),
    servicesCount: typeof value['servicesCount'] === 'number' ? value['servicesCount'] : Array.isArray(lines) ? lines.length : null,
  };
}
