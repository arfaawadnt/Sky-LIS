import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import {
  type AdminSessionItem,
  type AnalyserModelListItem,
  type AnalyserModelRequest,
  type ClientDetail,
  type ClientFilter,
  type ClientListItem,
  type ClientLoginLogFilter,
  type ClientLoginLogItem,
  type CodeSystemImportResult,
  type CodeSystemItemView,
  type CodeSystemListItem,
  type CodeSystemRequest,
  type CountryListItem,
  type CountryRequest,
  type CreatedResource,
  type DashboardPeriod,
  type GovernorateListItem,
  type GovernorateRequest,
  type LocationLevel,
  type LocationListItem,
  type LocationRequest,
  type NotificationTemplateListItem,
  type NotificationTemplateRequest,
  type Paged,
  type PcrKitListItem,
  type PcrKitRequest,
  PLATFORM_API,
  PLATFORM_LOCATIONS_API,
  type PlanListItem,
  type PlanRequest,
  type PlatformDashboard,
  type ProvisionClientRequest,
  type ProvisionedClient,
  type ReferenceListFilter,
  type RuleTemplateListItem,
  type RuleTemplateRequest,
  type SilentClientList,
  type TemplatePlaceholders,
  type TopUpWalletRequest,
  type UpdateClientRequest,
  type WalletChargeRunSummary,
  type WalletDetail,
  type WalletTransactionItem,
  type WalletTransactionKind,
} from '../../../core/api/platform.contracts';
import { MAX_PAGE_SIZE, type PageRequest } from '../../../core/api/paging';
import { type TenantSettingsResponse, SETTINGS_API, type UpdateTenantSettingsRequest } from '../../../core/api/settings.contracts';
import { toHttpParams } from '../../../shared/forms/paging';

/** Typed calls of platform administration; URLs through `HttpParams`, never concatenated query strings (pitfall P-60). */
@Injectable({ providedIn: 'root' })
export class PlatformApi {
  private readonly http = inject(HttpClient);

  // clients (SA-04)
  listClients(filter: ClientFilter, signal?: AbortSignal): Promise<Paged<ClientListItem>> {
    return this.get<Paged<ClientListItem>>(PLATFORM_API.clients, { ...filter }, signal);
  }

  getClient(id: string): Promise<ClientDetail> {
    return this.get<ClientDetail>(`${PLATFORM_API.clients}/${encodeURIComponent(id)}`, {});
  }

  provisionClient(request: ProvisionClientRequest): Promise<ProvisionedClient> {
    return firstValueFrom(this.http.post<ProvisionedClient>(PLATFORM_API.clients, request));
  }

  updateClient(id: string, request: UpdateClientRequest): Promise<void> {
    return this.send('put', `${PLATFORM_API.clients}/${encodeURIComponent(id)}`, request);
  }

  activateClient(id: string): Promise<void> {
    return this.send('post', `${PLATFORM_API.clients}/${encodeURIComponent(id)}/activate`, null);
  }

  deactivateClient(id: string, reason: string): Promise<void> {
    return this.send('post', `${PLATFORM_API.clients}/${encodeURIComponent(id)}/deactivate`, { reason });
  }

  resetClientAdminPassword(id: string, newPassword: string): Promise<void> {
    return this.send('post', `${PLATFORM_API.clients}/${encodeURIComponent(id)}/reset-admin-password`, { newPassword });
  }

  // plans and wallets (SA-05)
  listPlans(isActive: boolean | null, page: PageRequest, signal?: AbortSignal): Promise<Paged<PlanListItem>> {
    return this.get<Paged<PlanListItem>>(PLATFORM_API.plans, { isActive, page: page.page, pageSize: page.pageSize }, signal);
  }

  createPlan(request: PlanRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(PLATFORM_API.plans, request));
  }

  updatePlan(id: string, request: PlanRequest): Promise<void> {
    return this.send('put', `${PLATFORM_API.plans}/${encodeURIComponent(id)}`, request);
  }

  setPlanActive(id: string, active: boolean): Promise<void> {
    return this.send('post', `${PLATFORM_API.plans}/${encodeURIComponent(id)}/${active ? 'reactivate' : 'deactivate'}`, null);
  }

  getWallet(tenantId: string, signal?: AbortSignal): Promise<WalletDetail> {
    return this.get<WalletDetail>(`${PLATFORM_API.wallets}/${encodeURIComponent(tenantId)}`, {}, signal);
  }

  listWalletTransactions(tenantId: string, kind: WalletTransactionKind | null, page: PageRequest, signal?: AbortSignal): Promise<Paged<WalletTransactionItem>> {
    return this.get<Paged<WalletTransactionItem>>(`${PLATFORM_API.wallets}/${encodeURIComponent(tenantId)}/transactions`, { kind, page: page.page, pageSize: page.pageSize }, signal);
  }

  topUpWallet(tenantId: string, request: TopUpWalletRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(`${PLATFORM_API.wallets}/${encodeURIComponent(tenantId)}/top-up`, request));
  }

  /** SA-05 "Run charges now": the monthly subscription charge pass; returns charged / skipped / failed. */
  runWalletCharges(): Promise<WalletChargeRunSummary> {
    return firstValueFrom(this.http.post<WalletChargeRunSummary>(PLATFORM_API.walletChargeRun, null));
  }

  declineTopUpRequest(tenantId: string, requestId: string): Promise<void> {
    return this.send('post', `${PLATFORM_API.wallets}/${encodeURIComponent(tenantId)}/top-up-requests/${encodeURIComponent(requestId)}/decline`, null);
  }

  // countries and governorates (SA-10, SA-11)
  listCountries(filter: ReferenceListFilter, signal?: AbortSignal): Promise<Paged<CountryListItem>> {
    return this.get<Paged<CountryListItem>>(PLATFORM_API.countries, { ...filter }, signal);
  }

  createCountry(request: CountryRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(PLATFORM_API.countries, request));
  }

  updateCountry(id: string, request: CountryRequest): Promise<void> {
    return this.send('put', `${PLATFORM_API.countries}/${encodeURIComponent(id)}`, request);
  }

  setCountryActive(id: string, active: boolean): Promise<void> {
    return this.send('post', `${PLATFORM_API.countries}/${encodeURIComponent(id)}/${active ? 'reactivate' : 'deactivate'}`, null);
  }

  deleteCountry(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${PLATFORM_API.countries}/${encodeURIComponent(id)}`), { defaultValue: undefined }).then(() => undefined);   // 204: the body is null, and a mutation's null means failure
  }

  listGovernorates(filter: ReferenceListFilter & { readonly countryId: string | null }, signal?: AbortSignal): Promise<Paged<GovernorateListItem>> {
    return this.get<Paged<GovernorateListItem>>(PLATFORM_API.governorates, { ...filter }, signal);
  }

  createGovernorate(request: GovernorateRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(PLATFORM_API.governorates, request));
  }

  updateGovernorate(id: string, request: GovernorateRequest): Promise<void> {
    return this.send('put', `${PLATFORM_API.governorates}/${encodeURIComponent(id)}`, request);
  }

  setGovernorateActive(id: string, active: boolean): Promise<void> {
    return this.send('post', `${PLATFORM_API.governorates}/${encodeURIComponent(id)}/${active ? 'reactivate' : 'deactivate'}`, null);
  }

  // notification templates (SA-20)
  listTemplates(filter: ReferenceListFilter, signal?: AbortSignal): Promise<Paged<NotificationTemplateListItem>> {
    return this.get<Paged<NotificationTemplateListItem>>(PLATFORM_API.templates, { ...filter }, signal);
  }

  templatePlaceholders(): Promise<TemplatePlaceholders> {
    return this.get<TemplatePlaceholders>(`${PLATFORM_API.templates}/placeholders`, {});
  }

  createTemplate(request: NotificationTemplateRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(PLATFORM_API.templates, request));
  }

  updateTemplate(id: string, request: NotificationTemplateRequest): Promise<void> {
    return this.send('put', `${PLATFORM_API.templates}/${encodeURIComponent(id)}`, request);
  }

  setTemplateActive(id: string, active: boolean): Promise<void> {
    return this.send('post', `${PLATFORM_API.templates}/${encodeURIComponent(id)}/${active ? 'reactivate' : 'deactivate'}`, null);
  }

  // integration library (SA-15…SA-18)
  listAnalyserModels(filter: ReferenceListFilter, signal?: AbortSignal): Promise<Paged<AnalyserModelListItem>> {
    return this.get<Paged<AnalyserModelListItem>>(PLATFORM_API.analyserModels, { ...filter }, signal);
  }

  saveAnalyserModel(id: string | null, request: AnalyserModelRequest): Promise<unknown> {
    return id === null
      ? firstValueFrom(this.http.post<CreatedResource>(PLATFORM_API.analyserModels, request))
      : this.send('put', `${PLATFORM_API.analyserModels}/${encodeURIComponent(id)}`, request);
  }

  listPcrKits(filter: ReferenceListFilter, signal?: AbortSignal): Promise<Paged<PcrKitListItem>> {
    return this.get<Paged<PcrKitListItem>>(PLATFORM_API.pcrKits, { ...filter }, signal);
  }

  savePcrKit(id: string | null, request: PcrKitRequest): Promise<unknown> {
    return id === null
      ? firstValueFrom(this.http.post<CreatedResource>(PLATFORM_API.pcrKits, request))
      : this.send('put', `${PLATFORM_API.pcrKits}/${encodeURIComponent(id)}`, request);
  }

  listCodeSystems(filter: ReferenceListFilter, signal?: AbortSignal): Promise<Paged<CodeSystemListItem>> {
    return this.get<Paged<CodeSystemListItem>>(PLATFORM_API.codeSystems, { ...filter }, signal);
  }

  saveCodeSystem(id: string | null, request: CodeSystemRequest): Promise<unknown> {
    return id === null
      ? firstValueFrom(this.http.post<CreatedResource>(PLATFORM_API.codeSystems, request))
      : this.send('put', `${PLATFORM_API.codeSystems}/${encodeURIComponent(id)}`, request);
  }

  listCodeSystemItems(id: string, search: string | null, page: PageRequest, signal?: AbortSignal): Promise<Paged<CodeSystemItemView>> {
    return this.get<Paged<CodeSystemItemView>>(`${PLATFORM_API.codeSystems}/${encodeURIComponent(id)}/items`, { search, page: page.page, pageSize: page.pageSize }, signal);
  }

  importCodeSystemItems(id: string, file: File): Promise<CodeSystemImportResult> {
    const form = new FormData();
    form.append('file', file, file.name);
    return firstValueFrom(this.http.post<CodeSystemImportResult>(`${PLATFORM_API.codeSystems}/${encodeURIComponent(id)}/import`, form));
  }

  listRuleTemplates(filter: ReferenceListFilter, signal?: AbortSignal): Promise<Paged<RuleTemplateListItem>> {
    return this.get<Paged<RuleTemplateListItem>>(PLATFORM_API.ruleTemplates, { ...filter }, signal);
  }

  saveRuleTemplate(id: string | null, request: RuleTemplateRequest): Promise<unknown> {
    return id === null
      ? firstValueFrom(this.http.post<CreatedResource>(PLATFORM_API.ruleTemplates, request))
      : this.send('put', `${PLATFORM_API.ruleTemplates}/${encodeURIComponent(id)}`, request);
  }

  /** Deactivate / reactivate one row of a library register (`kind` is the register's base URL). */
  setLibraryActive(baseUrl: string, id: string, active: boolean): Promise<void> {
    return this.send('post', `${baseUrl}/${encodeURIComponent(id)}/${active ? 'reactivate' : 'deactivate'}`, null);
  }

  // dashboard and sessions (SA-03, SA-08)
  dashboard(period: DashboardPeriod, signal?: AbortSignal): Promise<PlatformDashboard> {
    return this.get<PlatformDashboard>(PLATFORM_API.dashboard, { period }, signal);
  }

  // silent clients (LIS-CR-007 A-01)
  silentClients(days: number, signal?: AbortSignal): Promise<SilentClientList> {
    return this.get<SilentClientList>(PLATFORM_API.silentClients, { days }, signal);
  }

  /** The CSV the server builds for the window (UTF-8 with BOM), headers in `language`, with the file name it chose. */
  async exportSilentClients(days: number, language: string): Promise<DownloadedFile> {
    const params = toHttpParams({ days, language });
    const response = await firstValueFrom(this.http.get(`${PLATFORM_API.silentClients}/export`, { params, responseType: 'blob', observe: 'response' }));
    return { blob: response.body ?? new Blob([], { type: 'text/csv' }), fileName: fileNameOf(response.headers.get('Content-Disposition')) ?? `silent-clients-${days}d.csv` };
  }

  listSessions(search: string | null, signal?: AbortSignal): Promise<readonly AdminSessionItem[]> {
    return this.get<readonly AdminSessionItem[]>(PLATFORM_API.sessions, { search }, signal);
  }

  terminateSession(id: string): Promise<void> {
    return this.send('post', `${PLATFORM_API.sessions}/${encodeURIComponent(id)}/terminate`, null);
  }

  // platform parameters (SA-19): the settings API under the platform tenant
  settings(signal?: AbortSignal): Promise<TenantSettingsResponse> {
    return this.get<TenantSettingsResponse>(SETTINGS_API.settings, {}, signal);
  }

  saveSettings(request: UpdateTenantSettingsRequest): Promise<TenantSettingsResponse> {
    return firstValueFrom(this.http.put<TenantSettingsResponse>(SETTINGS_API.settings, request));
  }

  // client login logs (SA-08b, LIS-CR-003 §2)
  listLoginLogs(filter: ClientLoginLogFilter, signal?: AbortSignal): Promise<Paged<ClientLoginLogItem>> {
    return this.get<Paged<ClientLoginLogItem>>(PLATFORM_API.loginLogs, { ...filter }, signal);
  }

  /** The CSV the server builds for the same filter (UTF-8 with BOM, at most 10 000 rows), with the file name it chose. */
  async exportLoginLogs(filter: Omit<ClientLoginLogFilter, 'page' | 'pageSize'>, language: string): Promise<DownloadedFile> {
    const params = toHttpParams({ ...filter, language });
    const response = await firstValueFrom(this.http.get(`${PLATFORM_API.loginLogs}/export`, { params, responseType: 'blob', observe: 'response' }));
    return { blob: response.body ?? new Blob([], { type: 'text/csv' }), fileName: fileNameOf(response.headers.get('Content-Disposition')) ?? 'client-login-logs.csv' };
  }

  /** Every page of a register (for the CSV export), at most `MAX_PAGE_SIZE` rows per call and `maxRows` in all. */
  async all<T>(load: (page: PageRequest) => Promise<Paged<T>>, maxRows = 10_000): Promise<readonly T[]> {
    const rows: T[] = [];
    for (let page = 1; ; page++) {
      const result = await load({ page, pageSize: MAX_PAGE_SIZE });
      rows.push(...result.items);
      if (result.items.length < MAX_PAGE_SIZE || rows.length >= result.totalCount || rows.length >= maxRows) {
        return rows.slice(0, maxRows);
      }
    }
  }

  // location hierarchy under a governorate (SA-11b, LIS-CR-003 §3): cities of a governorate, districts of a city, areas of a district
  listLocations(level: LocationLevel, parentId: string, filter: { readonly search: string | null; readonly isActive: boolean | null }, signal?: AbortSignal): Promise<readonly LocationListItem[]> {
    return this.get<readonly LocationListItem[]>(`${PLATFORM_LOCATIONS_API[level].parents}/${encodeURIComponent(parentId)}/${level}`, { ...filter }, signal);
  }

  createLocation(level: LocationLevel, parentId: string, request: LocationRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(`${PLATFORM_LOCATIONS_API[level].parents}/${encodeURIComponent(parentId)}/${level}`, request));
  }

  renameLocation(level: LocationLevel, id: string, request: LocationRequest): Promise<void> {
    return this.send('put', `${PLATFORM_LOCATIONS_API[level].items}/${encodeURIComponent(id)}`, request);
  }

  setLocationActive(level: LocationLevel, id: string, active: boolean): Promise<void> {
    return this.send('post', `${PLATFORM_LOCATIONS_API[level].items}/${encodeURIComponent(id)}/${active ? 'reactivate' : 'deactivate'}`, null);
  }

  private get<T>(url: string, query: Readonly<Record<string, string | number | boolean | null | undefined>>, signal?: AbortSignal): Promise<T> {
    return firstValueFrom(this.http.get<T>(url, { params: toHttpParams(query) })).then(abortable<T>(signal));
  }

  private send(method: 'post' | 'put', url: string, body: unknown): Promise<void> {
    // A 204 answer has a null body and `firstValueFrom` resolves it as null, which `mutation()` reports as failure; success is undefined.
    return firstValueFrom(method === 'put' ? this.http.put<void>(url, body) : this.http.post<void>(url, body), { defaultValue: undefined }).then(() => undefined);
  }
}

/** A file the server built (a CSV export): its bytes and the name of the Content-Disposition header. */
export interface DownloadedFile {
  readonly blob: Blob;
  readonly fileName: string;
}

/** `attachment; filename=client-login-logs-2026-10-19-0830.csv; filename*=UTF-8''…` → the plain file name. */
export function fileNameOf(contentDisposition: string | null): string | null {
  if (!contentDisposition) {
    return null;
  }

  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(contentDisposition);
  return match?.[1] ? decodeURIComponent(match[1].trim()) : null;
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
