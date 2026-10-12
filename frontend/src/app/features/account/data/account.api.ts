import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { type Observable, firstValueFrom } from 'rxjs';

import {
  ACCOUNT_API,
  type AccountLanguage,
  type AuditActorItem,
  type BranchDashboard,
  type BranchDashboardQuery,
  type DeskTicketFilter,
  type MyAccountProfile,
  type OpenSupportTicketRequest,
  type SupportTicketDetail,
  type SupportTicketFilter,
  type SupportTicketListItem,
} from '../../../core/api/account.contracts';
import { AUDIT_API, type AuditClientItem, type AuditEntriesPage, type AuditEntryFilter, type ChainVerificationResult } from '../../../core/api/audit.contracts';
import { ORGANIZATION_API, type SelectableBranch, type SelectableBranchesResponse } from '../../../core/api/organization.contracts';
import type { Paged } from '../../../core/api/paging';
import { PLATFORM_API, type CreateTopUpRequest, type WalletDetail, type WalletTransactionItem, type WalletTransactionKind } from '../../../core/api/platform.contracts';
import type { CreatedResource } from '../../../core/api/treasury.contracts';
import { toHttpParams } from '../../../shared/forms/paging';

/** The audit filter without paging, for the CSV export. */
export type AuditExportFilter = Omit<AuditEntryFilter, 'page' | 'pageSize'>;

/** A CSV file as the browser downloads it. */
export interface DownloadedFile {
  readonly blob: Blob;
  readonly fileName: string;
}

/**
 * Typed calls of the account module and of the APIs its pages read (organization's selectable branches, the
 * platform's tenant wallet, the foundation's audit log). URLs are built with `HttpParams`, never by concatenation
 * of user input (pitfall P-60); ids are encoded.
 */
@Injectable({ providedIn: 'root' })
export class AccountApi {
  private readonly http = inject(HttpClient);

  // ---- profile, preferences, password (CP-65 / SA-21) ----

  profile(): Promise<MyAccountProfile> {
    return firstValueFrom(this.http.get<MyAccountProfile>(ACCOUNT_API.profile));
  }

  updateLanguage(language: AccountLanguage): Promise<void> {
    return completed(this.http.put<void>(ACCOUNT_API.preferences, { language }));
  }

  /** Both fields are sealed by the envelope interceptor (`credential-fields.ts` must list `POST /api/v1/account/password`). */
  changePassword(currentPassword: string, newPassword: string): Promise<void> {
    return completed(this.http.post<void>(ACCOUNT_API.password, { currentPassword, newPassword }));
  }

  // ---- branch selection (CP-04) ----

  /** The organization module answers `{ currentBranchId, branches }`; an older shape (a bare array) is accepted too. */
  async selectableBranches(): Promise<readonly SelectableBranch[]> {
    const response = await firstValueFrom(this.http.get<SelectableBranchesResponse | readonly SelectableBranch[]>(ORGANIZATION_API.selectableBranches));
    return Array.isArray(response) ? response : (response as SelectableBranchesResponse).branches;
  }

  // ---- dashboard (CP-05) ----

  dashboard(query: BranchDashboardQuery): Promise<BranchDashboard> {
    const params = new HttpParams({ fromObject: { date: query.date, utcOffsetMinutes: query.utcOffsetMinutes } });
    return firstValueFrom(this.http.get<BranchDashboard>(ACCOUNT_API.dashboard, { params }));
  }

  // ---- wallet (CP-66, platform module's API) ----

  wallet(): Promise<WalletDetail> {
    return firstValueFrom(this.http.get<WalletDetail>(PLATFORM_API.myWallet));
  }

  walletTransactions(kind: WalletTransactionKind | null, page: number, pageSize: number): Promise<Paged<WalletTransactionItem>> {
    const params = toHttpParams({ kind, page, pageSize });
    return firstValueFrom(this.http.get<Paged<WalletTransactionItem>>(`${PLATFORM_API.myWallet}/transactions`, { params }));
  }

  requestTopUp(request: CreateTopUpRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(`${PLATFORM_API.myWallet}/top-up-requests`, request));
  }

  // ---- support tickets (CP-67) ----

  tickets(filter: SupportTicketFilter): Promise<Paged<SupportTicketListItem>> {
    const params = toHttpParams({ status: filter.status, priority: filter.priority, search: filter.search, mine: filter.mine ? true : null, page: filter.page, pageSize: filter.pageSize });
    return firstValueFrom(this.http.get<Paged<SupportTicketListItem>>(ACCOUNT_API.tickets, { params }));
  }

  ticket(id: string): Promise<SupportTicketDetail> {
    return firstValueFrom(this.http.get<SupportTicketDetail>(`${ACCOUNT_API.tickets}/${encodeURIComponent(id)}`));
  }

  openTicket(request: OpenSupportTicketRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(ACCOUNT_API.tickets, request));
  }

  replyToTicket(id: string, text: string): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(`${ACCOUNT_API.tickets}/${encodeURIComponent(id)}/replies`, { text }));
  }

  reopenTicket(id: string): Promise<void> {
    return completed(this.http.post<void>(`${ACCOUNT_API.tickets}/${encodeURIComponent(id)}/reopen`, null));
  }

  closeTicket(id: string): Promise<void> {
    return completed(this.http.post<void>(`${ACCOUNT_API.tickets}/${encodeURIComponent(id)}/close`, null));
  }

  // ---- the platform support desk ----

  deskTickets(filter: DeskTicketFilter): Promise<Paged<SupportTicketListItem>> {
    const params = toHttpParams({ tenantId: filter.tenantId, status: filter.status, priority: filter.priority, search: filter.search, page: filter.page, pageSize: filter.pageSize });
    return firstValueFrom(this.http.get<Paged<SupportTicketListItem>>(ACCOUNT_API.deskTickets, { params }));
  }

  deskTicket(id: string): Promise<SupportTicketDetail> {
    return firstValueFrom(this.http.get<SupportTicketDetail>(`${ACCOUNT_API.deskTickets}/${encodeURIComponent(id)}`));
  }

  answerTicket(id: string, text: string): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(`${ACCOUNT_API.deskTickets}/${encodeURIComponent(id)}/replies`, { text }));
  }

  startTicket(id: string): Promise<void> {
    return completed(this.http.post<void>(`${ACCOUNT_API.deskTickets}/${encodeURIComponent(id)}/start`, null));
  }

  resolveTicket(id: string, note: string | null): Promise<void> {
    return completed(this.http.post<void>(`${ACCOUNT_API.deskTickets}/${encodeURIComponent(id)}/resolve`, { note }));
  }

  closeTicketAtDesk(id: string): Promise<void> {
    return completed(this.http.post<void>(`${ACCOUNT_API.deskTickets}/${encodeURIComponent(id)}/close`, null));
  }

  // ---- audit log (CP-68 / SA-09) ----

  auditEntries(filter: AuditEntryFilter): Promise<AuditEntriesPage> {
    const params = toHttpParams({
      from: filter.from,
      to: filter.to,
      userId: filter.userId,
      entityType: filter.entityType,
      entityId: filter.entityId,
      clientTenantId: filter.clientTenantId ?? null,
      page: filter.page,
      pageSize: filter.pageSize,
    });
    return firstValueFrom(this.http.get<AuditEntriesPage>(AUDIT_API.entries, { params }));
  }

  /** The platform's clients for the SA-09 client filter (UAT C-18); a laboratory gets none. */
  auditClients(): Promise<readonly AuditClientItem[]> {
    return firstValueFrom(this.http.get<readonly AuditClientItem[]>(AUDIT_API.clients), { defaultValue: [] });
  }

  verifyChain(): Promise<ChainVerificationResult> {
    return firstValueFrom(this.http.get<ChainVerificationResult>(AUDIT_API.verifyChain));
  }

  auditActors(): Promise<readonly AuditActorItem[]> {
    return firstValueFrom(this.http.get<readonly AuditActorItem[]>(ACCOUNT_API.auditActors), { defaultValue: [] });
  }

  async exportAudit(filter: AuditExportFilter, language: string): Promise<DownloadedFile> {
    const params = toHttpParams({ from: filter.from, to: filter.to, userId: filter.userId, entityType: filter.entityType, entityId: filter.entityId, clientTenantId: filter.clientTenantId ?? null, language });
    const response = await firstValueFrom(this.http.get(ACCOUNT_API.auditExport, { params, responseType: 'blob', observe: 'response' }));
    return { blob: response.body ?? new Blob([], { type: 'text/csv' }), fileName: fileNameOf(response.headers.get('Content-Disposition')) ?? 'audit-log.csv' };
  }
}

/** A 204 arrives as a `null` body; a write without a result resolves to `undefined`, so `mutation()` callers can tell success (anything but `null`) from failure. */
function completed(request: Observable<unknown>): Promise<void> {
  return firstValueFrom(request, { defaultValue: undefined }).then(() => undefined);
}

/** `attachment; filename=audit-log-2026-10-09-0830.csv; filename*=UTF-8''…` → the plain file name. */
export function fileNameOf(contentDisposition: string | null): string | null {
  if (!contentDisposition) {
    return null;
  }

  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(contentDisposition);
  return match?.[1] ? decodeURIComponent(match[1].trim()) : null;
}
