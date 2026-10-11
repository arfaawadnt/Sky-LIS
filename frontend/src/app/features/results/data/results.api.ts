import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { emptyPage } from '../../../core/api/paging';
import {
  type AnalyserOption,
  type BulkResultOutcome,
  type CommentTemplateOption,
  type CultureEntryDto,
  type FormattedParagraphDto,
  type LaboratoryReportDocument,
  type LookupOption,
  type MicrobiologyLookups,
  type Paged,
  type PreviousResultItem,
  RESULTS_API,
  type ResultCaseView,
  type ResultLogItem,
  type ResultValueItem,
  type ResultVisitListItem,
  type ResultVisitQuery,
  type WorklistDocument,
  type WorklistPage,
  type WorklistQuery,
} from '../../../core/api/results.contracts';
import { toHttpParams } from '../../../shared/forms/paging';

/** A value of one result as the amend endpoint takes it (the reason travels beside it). */
export interface AmendInput {
  readonly numericValue: number | null;
  readonly textValue: string | null;
  readonly paragraphs: readonly FormattedParagraphDto[] | null;
  readonly culture: CultureEntryDto | null;
}

/** Typed calls of the Results module; URLs are built with `HttpParams`, never by string concatenation (pitfall P-60). */
@Injectable({ providedIn: 'root' })
export class ResultsApi {
  private readonly http = inject(HttpClient);

  caseByAccession(accession: string, signal?: AbortSignal): Promise<ResultCaseView> {
    const params = toHttpParams({ accession: accession.trim() });
    return firstValueFrom(this.http.get<ResultCaseView>(RESULTS_API.results, { params })).then(abortable(signal));
  }

  caseByVisit(visitId: string, signal?: AbortSignal): Promise<ResultCaseView> {
    const params = toHttpParams({ visitId });
    return firstValueFrom(this.http.get<ResultCaseView>(RESULTS_API.results, { params })).then(abortable(signal));
  }

  searchVisits(query: ResultVisitQuery, signal?: AbortSignal): Promise<Paged<ResultVisitListItem>> {
    const params = toHttpParams({
      accession: query.accession,
      patientNumber: query.patientNumber,
      patientName: query.patientName,
      status: query.status,
      sampleStatus: query.sampleStatus,
      analyserId: query.analyserId,
      branchOnly: query.branchOnly || null,
      notPrinted: query.notPrinted || null,
      notVerified: query.notVerified || null,
      delta: query.delta || null,
      from: query.from,
      to: query.to,
      page: query.page,
      pageSize: query.pageSize,
    });
    return firstValueFrom(this.http.get<Paged<ResultVisitListItem>>(RESULTS_API.visits, { params }), { defaultValue: emptyPage<ResultVisitListItem>() }).then(abortable(signal));
  }

  saveValues(values: readonly ResultValueItem[]): Promise<BulkResultOutcome> {
    return firstValueFrom(this.http.put<BulkResultOutcome>(RESULTS_API.values, { values }));
  }

  reviewMany(resultIds: readonly string[]): Promise<BulkResultOutcome> {
    return firstValueFrom(this.http.post<BulkResultOutcome>(RESULTS_API.reviewMany, { resultIds }));
  }

  verifyMany(resultIds: readonly string[]): Promise<BulkResultOutcome> {
    return firstValueFrom(this.http.post<BulkResultOutcome>(RESULTS_API.verifyMany, { resultIds }));
  }

  undo(resultId: string, reason: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(RESULTS_API.undo(resultId), { reason }), { defaultValue: undefined }).then(noContent);
  }

  amend(resultId: string, value: AmendInput, reason: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(RESULTS_API.amend(resultId), { ...value, reason }), { defaultValue: undefined }).then(noContent);
  }

  acknowledgeDelta(resultId: string, comment: string | null): Promise<void> {
    return firstValueFrom(this.http.post<void>(RESULTS_API.acknowledgeDelta(resultId), { comment }), { defaultValue: undefined }).then(noContent);
  }

  setComment(resultId: string, text: string | null): Promise<void> {
    return firstValueFrom(this.http.put<void>(RESULTS_API.comment(resultId), { text }), { defaultValue: undefined }).then(noContent);
  }

  setNote(resultId: string, text: string | null): Promise<void> {
    return firstValueFrom(this.http.put<void>(RESULTS_API.note(resultId), { text }), { defaultValue: undefined }).then(noContent);
  }

  log(resultId: string, signal?: AbortSignal): Promise<readonly ResultLogItem[]> {
    return firstValueFrom(this.http.get<readonly ResultLogItem[]>(RESULTS_API.log(resultId)), { defaultValue: [] }).then(abortable(signal));
  }

  previous(resultId: string, signal?: AbortSignal): Promise<readonly PreviousResultItem[]> {
    return firstValueFrom(this.http.get<readonly PreviousResultItem[]>(RESULTS_API.previous(resultId)), { defaultValue: [] }).then(abortable(signal));
  }

  report(reportUnitKey: string, preview: boolean): Promise<LaboratoryReportDocument> {
    const params = toHttpParams({ preview });
    return firstValueFrom(this.http.get<LaboratoryReportDocument>(RESULTS_API.report(reportUnitKey), { params }));
  }

  print(reportUnitKey: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(RESULTS_API.print(reportUnitKey), null), { defaultValue: undefined }).then(noContent);
  }

  worklist(query: WorklistQuery, signal?: AbortSignal): Promise<WorklistPage> {
    return firstValueFrom(this.http.get<WorklistPage>(RESULTS_API.worklist, { params: worklistParams(query, true) })).then(abortable(signal));
  }

  worklistDocument(query: WorklistQuery): Promise<WorklistDocument> {
    return firstValueFrom(this.http.get<WorklistDocument>(RESULTS_API.worklistDocument, { params: worklistParams(query, false) }));
  }

  commentTemplates(search: string | null, clinicalGroupId: string | null, signal?: AbortSignal): Promise<readonly CommentTemplateOption[]> {
    const params = toHttpParams({ search, clinicalGroupId });
    return firstValueFrom(this.http.get<readonly CommentTemplateOption[]>(RESULTS_API.commentTemplates, { params }), { defaultValue: [] }).then(abortable(signal));
  }

  microbiology(signal?: AbortSignal): Promise<MicrobiologyLookups> {
    return firstValueFrom(this.http.get<MicrobiologyLookups>(RESULTS_API.microbiology), { defaultValue: { organisms: [], antibiotics: [], sensitivities: [] } }).then(abortable(signal));
  }

  clinicalGroups(signal?: AbortSignal): Promise<readonly LookupOption[]> {
    return firstValueFrom(this.http.get<readonly LookupOption[]>(RESULTS_API.clinicalGroups), { defaultValue: [] }).then(abortable(signal));
  }

  tests(clinicalGroupId: string | null, search: string | null, signal?: AbortSignal): Promise<readonly LookupOption[]> {
    const params = toHttpParams({ clinicalGroupId, search });
    return firstValueFrom(this.http.get<readonly LookupOption[]>(RESULTS_API.tests, { params }), { defaultValue: [] }).then(abortable(signal));
  }

  payers(signal?: AbortSignal): Promise<readonly LookupOption[]> {
    return firstValueFrom(this.http.get<readonly LookupOption[]>(RESULTS_API.payers), { defaultValue: [] }).then(abortable(signal));
  }

  analysers(signal?: AbortSignal): Promise<readonly AnalyserOption[]> {
    return firstValueFrom(this.http.get<readonly AnalyserOption[]>(RESULTS_API.analysers), { defaultValue: [] }).then(abortable(signal));
  }
}

function worklistParams(query: WorklistQuery, paged: boolean): ReturnType<typeof toHttpParams> {
  let params = toHttpParams({
    from: query.from,
    to: query.to,
    clinicalGroupId: query.clinicalGroupId,
    testId: query.testId,
    analyserId: query.analyserId,
    sampleStatus: query.sampleStatus,
    payerId: query.payerId,
    delayedOnly: query.delayedOnly || null,
    search: query.search,
    branchOnly: query.branchOnly || null,
    page: paged ? query.page : null,
    pageSize: paged ? query.pageSize : null,
  });
  for (const status of query.statuses) {
    params = params.append('status', status);
  }

  return params;
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

/** A 204 arrives as a `null` body; a write without a result resolves to `undefined`, because a mutation's `null` means it failed (UAT: 204 answers). */
function noContent(): undefined {
  return undefined;
}
