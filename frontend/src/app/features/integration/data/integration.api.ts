import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import {
  type AgentLogFilter,
  type AgentLogItem,
  type AnalyserCodeSheetDocument,
  type AnalyserConnectionState,
  type AnalyserDetail,
  type AnalyserListFilter,
  type AnalyserListItem,
  type AnalyserModelItem,
  type ConversionDto,
  type CreateSiteAgentRequest,
  type CreatedResource,
  INTEGRATION_API,
  INTEGRATION_LOOKUP_API,
  type IntakeFilter,
  type IntakeLogItem,
  type MappingListItem,
  type Paged,
  type PcrKitDetail,
  type PcrKitListItem,
  type PcrRunView,
  type RangeOverrideDto,
  type ReferenceOption,
  type SampleCodeItem,
  type SaveAnalyserRequest,
  type SaveMappingRequest,
  type SavePcrKitRequest,
  type SaveSampleCodeRequest,
  type SimulatePcrRunRequest,
  type SiteAgentDetail,
  type SiteAgentListItem,
  type SiteAgentSecretResponse,
  type TestLookupOption,
  type UpdateSiteAgentRequest,
} from '../../../core/api/integration.contracts';
import { emptyPage } from '../../../core/api/paging';
import { toHttpParams } from '../../../shared/forms/paging';

/** Typed calls of the Integration module; URLs are built with `HttpParams` and encoded ids, never by hand (pitfall P-60). */
@Injectable({ providedIn: 'root' })
export class IntegrationApi {
  private readonly http = inject(HttpClient);

  // ---- analysers (CP-58)

  listAnalysers(filter: AnalyserListFilter, signal?: AbortSignal): Promise<Paged<AnalyserListItem>> {
    const params = toHttpParams({ search: filter.search, isActive: filter.isActive, siteAgentId: filter.siteAgentId, page: filter.page, pageSize: filter.pageSize });
    return this.get<Paged<AnalyserListItem>>(INTEGRATION_API.analysers, params, signal, emptyPage<AnalyserListItem>());
  }

  /** Every active analyser (≤ 200), for pickers. */
  analyserOptions(signal?: AbortSignal): Promise<Paged<AnalyserListItem>> {
    return this.listAnalysers({ search: '', isActive: true, siteAgentId: null, page: 1, pageSize: 200 }, signal);
  }

  getAnalyser(id: string): Promise<AnalyserDetail> {
    return firstValueFrom(this.http.get<AnalyserDetail>(`${INTEGRATION_API.analysers}/${encodeURIComponent(id)}`));
  }

  createAnalyser(request: SaveAnalyserRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(INTEGRATION_API.analysers, request));
  }

  updateAnalyser(id: string, request: SaveAnalyserRequest): Promise<void> {
    return this.put(`${INTEGRATION_API.analysers}/${encodeURIComponent(id)}`, request);
  }

  setAnalyserActive(id: string, active: boolean): Promise<void> {
    return this.post(`${INTEGRATION_API.analysers}/${encodeURIComponent(id)}/${active ? 'reactivate' : 'deactivate'}`, null);
  }

  testConnection(id: string): Promise<AnalyserConnectionState> {
    return firstValueFrom(this.http.get<AnalyserConnectionState>(`${INTEGRATION_API.analysers}/${encodeURIComponent(id)}/connection`));
  }

  analyserModels(search: string | null, signal?: AbortSignal): Promise<readonly AnalyserModelItem[]> {
    return this.get<readonly AnalyserModelItem[]>(INTEGRATION_API.analyserModels, toHttpParams({ search }), signal, []);
  }

  // ---- mappings (CP-59)

  listMappings(analyserId: string, search: string, page: number, pageSize: number, signal?: AbortSignal): Promise<Paged<MappingListItem>> {
    return this.get<Paged<MappingListItem>>(`${INTEGRATION_API.analysers}/${encodeURIComponent(analyserId)}/mappings`, toHttpParams({ search, page, pageSize }), signal, emptyPage<MappingListItem>());
  }

  mapTest(analyserId: string, request: SaveMappingRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(`${INTEGRATION_API.analysers}/${encodeURIComponent(analyserId)}/mappings`, request));
  }

  updateMapping(analyserId: string, mappingId: string, request: SaveMappingRequest): Promise<void> {
    return this.put(this.mappingUrl(analyserId, mappingId), request);
  }

  setAutoSend(analyserId: string, mappingId: string, enabled: boolean): Promise<void> {
    return this.put(`${this.mappingUrl(analyserId, mappingId)}/auto-send`, { enabled });
  }

  replaceConversions(analyserId: string, mappingId: string, conversions: readonly ConversionDto[]): Promise<void> {
    return this.put(`${this.mappingUrl(analyserId, mappingId)}/conversions`, { conversions });
  }

  setRangeOverride(analyserId: string, mappingId: string, range: RangeOverrideDto | null): Promise<void> {
    return this.put(`${this.mappingUrl(analyserId, mappingId)}/range-override`, range ?? { low: null, high: null, text: null });
  }

  setMappingActive(analyserId: string, mappingId: string, active: boolean): Promise<void> {
    return this.post(`${this.mappingUrl(analyserId, mappingId)}/${active ? 'reactivate' : 'deactivate'}`, null);
  }

  codeSheet(analyserId: string): Promise<AnalyserCodeSheetDocument> {
    return firstValueFrom(this.http.get<AnalyserCodeSheetDocument>(`${INTEGRATION_API.analysers}/${encodeURIComponent(analyserId)}/documents/code-sheet`));
  }

  // ---- sample codes (CP-60)

  listSampleCodes(analyserId: string, signal?: AbortSignal): Promise<readonly SampleCodeItem[]> {
    return this.get<readonly SampleCodeItem[]>(`${INTEGRATION_API.analysers}/${encodeURIComponent(analyserId)}/sample-codes`, toHttpParams({}), signal, []);
  }

  addSampleCode(analyserId: string, request: SaveSampleCodeRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(`${INTEGRATION_API.analysers}/${encodeURIComponent(analyserId)}/sample-codes`, request));
  }

  changeSampleCode(analyserId: string, sampleCodeId: string, request: SaveSampleCodeRequest): Promise<void> {
    return this.put(`${INTEGRATION_API.analysers}/${encodeURIComponent(analyserId)}/sample-codes/${encodeURIComponent(sampleCodeId)}`, request);
  }

  setSampleCodeActive(analyserId: string, sampleCodeId: string, active: boolean): Promise<void> {
    return this.post(`${INTEGRATION_API.analysers}/${encodeURIComponent(analyserId)}/sample-codes/${encodeURIComponent(sampleCodeId)}/${active ? 'reactivate' : 'deactivate'}`, null);
  }

  // ---- site agents (CP-61)

  listAgents(search: string, isActive: boolean | null, page: number, pageSize: number, signal?: AbortSignal): Promise<Paged<SiteAgentListItem>> {
    return this.get<Paged<SiteAgentListItem>>(INTEGRATION_API.siteAgents, toHttpParams({ search, isActive, page, pageSize }), signal, emptyPage<SiteAgentListItem>());
  }

  getAgent(id: string): Promise<SiteAgentDetail> {
    return firstValueFrom(this.http.get<SiteAgentDetail>(`${INTEGRATION_API.siteAgents}/${encodeURIComponent(id)}`));
  }

  createAgent(request: CreateSiteAgentRequest): Promise<SiteAgentSecretResponse> {
    return firstValueFrom(this.http.post<SiteAgentSecretResponse>(INTEGRATION_API.siteAgents, request));
  }

  updateAgent(id: string, request: UpdateSiteAgentRequest): Promise<void> {
    return this.put(`${INTEGRATION_API.siteAgents}/${encodeURIComponent(id)}`, request);
  }

  rotateAgentSecret(id: string): Promise<SiteAgentSecretResponse> {
    return firstValueFrom(this.http.post<SiteAgentSecretResponse>(`${INTEGRATION_API.siteAgents}/${encodeURIComponent(id)}/rotate-secret`, null));
  }

  pushAgentConfig(id: string): Promise<void> {
    return this.post(`${INTEGRATION_API.siteAgents}/${encodeURIComponent(id)}/push-config`, null);
  }

  deactivateAgent(id: string): Promise<void> {
    return this.post(`${INTEGRATION_API.siteAgents}/${encodeURIComponent(id)}/deactivate`, null);
  }

  reactivateAgent(id: string): Promise<SiteAgentSecretResponse> {
    return firstValueFrom(this.http.post<SiteAgentSecretResponse>(`${INTEGRATION_API.siteAgents}/${encodeURIComponent(id)}/reactivate`, null));
  }

  agentLogs(agentId: string, filter: AgentLogFilter, signal?: AbortSignal): Promise<Paged<AgentLogItem>> {
    const params = toHttpParams({ from: filter.from, to: filter.to, level: filter.level, search: filter.search, page: filter.page, pageSize: filter.pageSize });
    return this.get<Paged<AgentLogItem>>(`${INTEGRATION_API.siteAgents}/${encodeURIComponent(agentId)}/logs`, params, signal, emptyPage<AgentLogItem>());
  }

  intake(filter: IntakeFilter, signal?: AbortSignal): Promise<Paged<IntakeLogItem>> {
    const params = toHttpParams({ from: filter.from, to: filter.to, status: filter.status, analyserId: filter.analyserId, agentId: filter.agentId, search: filter.search, page: filter.page, pageSize: filter.pageSize });
    return this.get<Paged<IntakeLogItem>>(INTEGRATION_API.intake, params, signal, emptyPage<IntakeLogItem>());
  }

  // ---- PCR kits (CP-62)

  listKits(search: string, isActive: boolean | null, page: number, pageSize: number, signal?: AbortSignal): Promise<Paged<PcrKitListItem>> {
    return this.get<Paged<PcrKitListItem>>(INTEGRATION_API.pcrKits, toHttpParams({ search, isActive, page, pageSize }), signal, emptyPage<PcrKitListItem>());
  }

  getKit(id: string): Promise<PcrKitDetail> {
    return firstValueFrom(this.http.get<PcrKitDetail>(`${INTEGRATION_API.pcrKits}/${encodeURIComponent(id)}`));
  }

  createKit(request: SavePcrKitRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(INTEGRATION_API.pcrKits, request));
  }

  updateKit(id: string, request: SavePcrKitRequest): Promise<void> {
    return this.put(`${INTEGRATION_API.pcrKits}/${encodeURIComponent(id)}`, request);
  }

  setKitActive(id: string, active: boolean): Promise<void> {
    return this.post(`${INTEGRATION_API.pcrKits}/${encodeURIComponent(id)}/${active ? 'reactivate' : 'deactivate'}`, null);
  }

  simulateKit(id: string, request: SimulatePcrRunRequest): Promise<PcrRunView> {
    return firstValueFrom(this.http.post<PcrRunView>(`${INTEGRATION_API.pcrKits}/${encodeURIComponent(id)}/simulate`, request));
  }

  // ---- lookups of other modules (documented paths)

  searchTests(search: string, signal?: AbortSignal): Promise<readonly TestLookupOption[]> {
    return this.get<Paged<TestLookupOption>>(INTEGRATION_LOOKUP_API.tests, toHttpParams({ search, pageSize: 50 }), signal, emptyPage<TestLookupOption>()).then((page) => page.items);
  }

  clinicalGroups(signal?: AbortSignal): Promise<readonly ReferenceOption[]> {
    return this.options(INTEGRATION_LOOKUP_API.clinicalGroups, signal);
  }

  sampleTypes(signal?: AbortSignal): Promise<readonly ReferenceOption[]> {
    return this.options(INTEGRATION_LOOKUP_API.sampleTypes, signal);
  }

  units(signal?: AbortSignal): Promise<readonly ReferenceOption[]> {
    return this.options(INTEGRATION_LOOKUP_API.units, signal);
  }

  branches(signal?: AbortSignal): Promise<readonly ReferenceOption[]> {
    return this.options(INTEGRATION_LOOKUP_API.branches, signal);
  }

  private options(url: string, signal?: AbortSignal): Promise<readonly ReferenceOption[]> {
    return this.get<Paged<ReferenceOption>>(url, toHttpParams({ isActive: true, pageSize: 200 }), signal, emptyPage<ReferenceOption>()).then((page) => page.items);
  }

  private mappingUrl(analyserId: string, mappingId: string): string {
    return `${INTEGRATION_API.analysers}/${encodeURIComponent(analyserId)}/mappings/${encodeURIComponent(mappingId)}`;
  }

  private get<T>(url: string, params: ReturnType<typeof toHttpParams>, signal: AbortSignal | undefined, empty: T): Promise<T> {
    return firstValueFrom(this.http.get<T>(url, { params }), { defaultValue: empty }).then(abortable(signal));
  }

  // A 204 answer has a null body and `firstValueFrom` resolves it as null, which `mutation()` reports as failure; success is
  // undefined (validation 2026-10-11, the C-02 pattern).
  private put(url: string, body: unknown): Promise<void> {
    return firstValueFrom(this.http.put<void>(url, body), { defaultValue: undefined }).then(() => undefined);
  }

  private post(url: string, body: unknown): Promise<void> {
    return firstValueFrom(this.http.post<void>(url, body), { defaultValue: undefined }).then(() => undefined);
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
