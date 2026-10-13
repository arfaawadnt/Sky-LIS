import { DOCUMENT } from '@angular/common';
import { HttpClient, type HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import {
  type AntibioticListItem,
  type AntibioticRequest,
  CATALOGUE_B_API,
  type CatalogueBCreated,
  type CommentTemplateListItem,
  type CommentTemplateRequest,
  type CommercialNameListItem,
  type CommercialNameRequest,
  type CreateDiagnosisRequest,
  type CreateDoctorRequest,
  type CreatePreparationRequest,
  type CreateShortcutSetRequest,
  type DiagnosisListItem,
  type DoctorListItem,
  type DrugTypeListItem,
  type DrugTypeRequest,
  type OrganismListItem,
  type OrganismRequest,
  type Paged,
  type PreparationListItem,
  type ReasonListItem,
  type ReasonRequest,
  type ReportTemplateImageKind,
  type ReportTemplateImageView,
  type ReportTemplateListItem,
  type ReportTemplateRequest,
  reportTemplateImageUrl,
  type SendingLabListItem,
  type SendingLabRequest,
  type SensitivityValueListItem,
  type SensitivityValueRequest,
  type ShortcutSetListItem,
  type TestRuleListItem,
  type TestRuleRequest,
  type TestRuleValidationResult,
  type UpdateDiagnosisRequest,
  type UpdateDoctorRequest,
  type UpdatePreparationRequest,
  type UpdateShortcutSetRequest,
  type ValidateTestRuleRequest,
} from '../../../core/api/catalogue-b.contracts';
import { emptyPage } from '../../../core/api/paging';
import { I18nService } from '../../../core/i18n/i18n.service';
import { type FilterValue, toHttpParams } from '../../../shared/forms/paging';

/** A register query: search, active flag, page and the list's own filters (`kind`, `type`, `branchId` …); empty values are dropped. */
export type CatalogueBQuery = Readonly<Record<string, FilterValue>>;

/** The calls every Catalogue B list answers (one REST resource per list). */
export interface CatalogueBResource<TItem, TCreate, TUpdate> {
  list(query: CatalogueBQuery, signal?: AbortSignal): Promise<Paged<TItem>>;
  get(id: string): Promise<TItem>;
  create(request: TCreate): Promise<CatalogueBCreated>;
  update(id: string, request: TUpdate): Promise<void>;
  setActive(id: string, active: boolean): Promise<void>;
  /** Downloads the register as CSV (`catalogue.export`); the server names the file. */
  exportCsv(query: CatalogueBQuery): Promise<void>;
}

/** Typed calls of Catalogue B; URLs are built with `HttpParams`, never by string concatenation of values (pitfall P-60). */
@Injectable({ providedIn: 'root' })
export class CatalogueBApi {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);
  private readonly i18n = inject(I18nService);

  readonly preparations = this.resource<PreparationListItem, CreatePreparationRequest, UpdatePreparationRequest>(CATALOGUE_B_API.preparations);
  readonly commentTemplates = this.resource<CommentTemplateListItem, CommentTemplateRequest, CommentTemplateRequest>(CATALOGUE_B_API.commentTemplates);
  readonly doctors = this.resource<DoctorListItem, CreateDoctorRequest, UpdateDoctorRequest>(CATALOGUE_B_API.doctors);
  readonly diagnoses = this.resource<DiagnosisListItem, CreateDiagnosisRequest, UpdateDiagnosisRequest>(CATALOGUE_B_API.diagnoses);
  readonly sendingLabs = this.resource<SendingLabListItem, SendingLabRequest, SendingLabRequest>(CATALOGUE_B_API.sendingLabs);
  readonly shortcutSets = this.resource<ShortcutSetListItem, CreateShortcutSetRequest, UpdateShortcutSetRequest>(CATALOGUE_B_API.shortcutSets);
  readonly testRules = this.resource<TestRuleListItem, TestRuleRequest, TestRuleRequest>(CATALOGUE_B_API.testRules);
  readonly reasons = this.resource<ReasonListItem, ReasonRequest, ReasonRequest>(CATALOGUE_B_API.reasons);
  readonly reportTemplates = this.resource<ReportTemplateListItem, ReportTemplateRequest, ReportTemplateRequest>(CATALOGUE_B_API.reportTemplates);
  readonly drugTypes = this.resource<DrugTypeListItem, DrugTypeRequest, DrugTypeRequest>(CATALOGUE_B_API.drugTypes);
  readonly commercialNames = this.resource<CommercialNameListItem, CommercialNameRequest, CommercialNameRequest>(CATALOGUE_B_API.commercialNames);
  readonly antibiotics = this.resource<AntibioticListItem, AntibioticRequest, AntibioticRequest>(CATALOGUE_B_API.antibiotics);
  readonly organisms = this.resource<OrganismListItem, OrganismRequest, OrganismRequest>(CATALOGUE_B_API.organisms);
  readonly sensitivityValues = this.resource<SensitivityValueListItem, SensitivityValueRequest, SensitivityValueRequest>(CATALOGUE_B_API.sensitivityValues);

  /**
   * Uploads or replaces the logo or the signature of a report template (`catalogue.edit`; multipart field `file`; PNG,
   * JPEG or WebP up to 512 KB — the server decides by the bytes and refuses with CAT-270…272).
   */
  uploadReportTemplateImage(templateId: string, image: ReportTemplateImageKind, file: File): Promise<ReportTemplateImageView> {
    const form = new FormData();
    form.append('file', file, file.name);
    return firstValueFrom(this.http.post<ReportTemplateImageView>(reportTemplateImageUrl(templateId, image), form));
  }

  /** Removes the logo or the signature of a report template (`catalogue.edit`; CAT-394 when none is attached). */
  removeReportTemplateImage(templateId: string, image: ReportTemplateImageKind): Promise<void> {
    return firstValueFrom(this.http.delete<void>(reportTemplateImageUrl(templateId, image)), { defaultValue: undefined }).then(() => undefined);
  }

  /** "Check rule" (CP-52 validate): errors and warnings of a candidate rule; nothing is saved. */
  validateTestRule(request: ValidateTestRuleRequest): Promise<TestRuleValidationResult> {
    return firstValueFrom(this.http.post<TestRuleValidationResult>(CATALOGUE_B_API.testRuleValidation, request));
  }

  private resource<TItem, TCreate, TUpdate>(url: string): CatalogueBResource<TItem, TCreate, TUpdate> {
    const item = (id: string): string => `${url}/${encodeURIComponent(id)}`;
    return {
      list: (query, signal) =>
        firstValueFrom(this.http.get<Paged<TItem>>(url, { params: toHttpParams(query) }), { defaultValue: emptyPage<TItem>() }).then(abortable(signal)),
      get: (id) => firstValueFrom(this.http.get<TItem>(item(id))),
      create: (request) => firstValueFrom(this.http.post<CatalogueBCreated>(url, request)),
      // a 204 emits a null body: resolve to undefined so callers can tell success (undefined) from a refusal (null from mutation())
      update: (id, request) => firstValueFrom(this.http.put<void>(item(id), request), { defaultValue: undefined }).then(() => undefined),
      setActive: (id, active) => firstValueFrom(this.http.post<void>(`${item(id)}/${active ? 'reactivate' : 'deactivate'}`, null), { defaultValue: undefined }).then(() => undefined),
      exportCsv: async (query) => {
        // the file speaks the user's language: headers, labels and Yes / No as the grid shows them (UAT B-09)
        const params = toHttpParams({ ...query, lang: this.i18n.lang() });
        const response = await firstValueFrom(this.http.get(`${url}/export`, { params, observe: 'response', responseType: 'blob' }));
        this.save(response, url);
      },
    };
  }

  /** Hands the downloaded file to the browser under the server's file name (Content-Disposition). */
  private save(response: HttpResponse<Blob>, url: string): void {
    const body = response.body;
    const view = this.document.defaultView;
    if (body === null || view === null || typeof view.URL.createObjectURL !== 'function') {
      return;
    }

    const href = view.URL.createObjectURL(body);
    const link = this.document.createElement('a');
    link.href = href;
    link.download = localFileName(fileNameOf(response.headers.get('Content-Disposition')), url.split('/').pop() ?? 'export');
    link.rel = 'noopener';
    this.document.body.appendChild(link);
    link.click();
    link.remove();
    view.setTimeout(() => view.URL.revokeObjectURL(href), 0);
  }
}

/**
 * The server names the file after its UTC clock (`doctors-20261010-2215.csv`); the download carries the user's own
 * date instead, like every other export (`doctors-2026-10-11.csv`, UAT B-09).
 */
export function localFileName(serverName: string | null, fallbackStem: string, now: Date = new Date()): string {
  const stem = (serverName ?? '').replace(/(-\d{8}-\d{4})?\.csv$/i, '') || fallbackStem;
  const pad = (value: number): string => String(value).padStart(2, '0');
  return `${stem}-${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}.csv`;
}

/** `attachment; filename=reasons-20261009-0830.csv; filename*=UTF-8''…` → the file name. */
export function fileNameOf(disposition: string | null): string | null {
  if (!disposition) {
    return null;
  }

  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(disposition);
  if (encoded?.[1]) {
    return decodeURIComponent(encoded[1].trim());
  }

  const plain = /filename="?([^";]+)"?/i.exec(disposition);
  return plain?.[1]?.trim() ?? null;
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
