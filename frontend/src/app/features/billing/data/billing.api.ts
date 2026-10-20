import { HttpClient, HttpParams, type HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import {
  BILLING_API,
  type BatchAccountView,
  type BatchPaymentResult,
  type BillingPrintSettings,
  type BillingReasonOption,
  type CashClosingCreated,
  type CashClosingListItem,
  type CashClosingPreview,
  type CashClosingRegisterFilter,
  type CashClosingSheetDocument,
  type CloseCashDrawerRequest,
  type CollectionsReportDocument,
  type CollectionsReportPrivileges,
  type CollectionsReportRequest,
  type DrawerCashier,
  type DrawerScopeFilter,
  type InvoiceDocument,
  type Paged,
  type PayBatchRequest,
  type PaymentCreated,
  type PaymentListItem,
  type PaymentMethodDefaultsResult,
  type PaymentMethodFilter,
  type PaymentMethodListItem,
  type PaymentMethodOption,
  type PaymentMethodRequest,
  type PaymentReceiptDocument,
  type PaymentRegisterFilter,
  type RecordPaymentRequest,
  type RecordRefundRequest,
  type RefundCreated,
  type RefundListItem,
  type RefundReceiptDocument,
  type RefundRegisterFilter,
  type UpdatePaymentMethodRequest,
  type VisitAccountView,
} from '../../../core/api/billing.contracts';
import type { CreatedResource } from '../../../core/api/created-resource';
import { type MyPrivileges, ORGANIZATION_API } from '../../../core/api/organization.contracts';
import { emptyPage } from '../../../core/api/paging';
import { toHttpParams } from '../../../shared/forms/paging';

/** A file the API generated (CSV exports), with the name from `Content-Disposition`. */
export interface DownloadedFile {
  readonly blob: Blob;
  readonly fileName: string;
}

/** Typed calls of the Billing module; URLs are built with `HttpParams` and encoded segments, never by concatenating input (pitfall P-60). */
@Injectable({ providedIn: 'root' })
export class BillingApi {
  private readonly http = inject(HttpClient);

  // ---- parameters ---------------------------------------------------------------------------------------------

  /**
   * `autoPrintReceipt` (CP-57) through the module's own `GET /api/v1/billing/print-settings` (`billing.view`): a cashier holds
   * no `settings.view`, so `/api/v1/settings` was refused and every payment printed (UAT A-05). The caller decides what an
   * unknown answer means (the cash desk does not print).
   */
  async autoPrintReceipt(signal?: AbortSignal): Promise<boolean> {
    const settings = await this.require<BillingPrintSettings>(BILLING_API.printSettings, signal);
    return settings.autoPrintReceipt;
  }

  /** Catalogue B reasons of kind Refund (CP-24 "reason mandatory"); only active ones are offered. */
  async refundReasons(signal?: AbortSignal): Promise<readonly BillingReasonOption[]> {
    const page = await this.get<Paged<BillingReasonOption>>(BILLING_API.refundReasons, new HttpParams({ fromObject: { kind: 'Refund', pageSize: 200 } }), emptyPage<BillingReasonOption>(), signal);
    return page.items.filter((reason) => reason.isActive);
  }

  // ---- visit accounts ---------------------------------------------------------------------------------------------

  accountByAccession(accession: string, signal?: AbortSignal): Promise<VisitAccountView> {
    return this.require<VisitAccountView>(`${BILLING_API.visits}/by-accession/${encodeURIComponent(accession)}/account`, signal);
  }

  accountByVisit(visitId: string, signal?: AbortSignal): Promise<VisitAccountView> {
    return this.require<VisitAccountView>(`${BILLING_API.visits}/${encodeURIComponent(visitId)}/account`, signal);
  }

  invoice(visitId: string): Promise<InvoiceDocument> {
    return this.require<InvoiceDocument>(`${BILLING_API.visits}/${encodeURIComponent(visitId)}/documents/invoice`);
  }

  // ---- payments ---------------------------------------------------------------------------------------------------

  recordPayment(request: RecordPaymentRequest): Promise<PaymentCreated> {
    return firstValueFrom(this.http.post<PaymentCreated>(BILLING_API.payments, request));
  }

  voidPayment(id: string, reason: string): Promise<void> {
    return firstValueFrom(this.http.post<null>(`${BILLING_API.payments}/${encodeURIComponent(id)}/void`, { reason }), { defaultValue: null }).then(() => undefined);   // 204: no body, success is undefined (a mutation's null means failure)
  }

  paymentReceipt(id: string): Promise<PaymentReceiptDocument> {
    return this.require<PaymentReceiptDocument>(`${BILLING_API.payments}/${encodeURIComponent(id)}/documents/receipt`);
  }

  listPayments(filter: PaymentRegisterFilter, signal?: AbortSignal): Promise<Paged<PaymentListItem>> {
    return this.get<Paged<PaymentListItem>>(BILLING_API.payments, paymentParams(filter, true), emptyPage<PaymentListItem>(), signal);
  }

  exportPayments(filter: PaymentRegisterFilter): Promise<DownloadedFile> {
    return this.download(`${BILLING_API.payments}/export`, paymentParams(filter, false), 'payments.csv');
  }

  // ---- refunds ----------------------------------------------------------------------------------------------------

  recordRefund(request: RecordRefundRequest): Promise<RefundCreated> {
    return firstValueFrom(this.http.post<RefundCreated>(BILLING_API.refunds, request));
  }

  refundReceipt(id: string): Promise<RefundReceiptDocument> {
    return this.require<RefundReceiptDocument>(`${BILLING_API.refunds}/${encodeURIComponent(id)}/documents/receipt`);
  }

  listRefunds(filter: RefundRegisterFilter, signal?: AbortSignal): Promise<Paged<RefundListItem>> {
    return this.get<Paged<RefundListItem>>(BILLING_API.refunds, refundParams(filter, true), emptyPage<RefundListItem>(), signal);
  }

  exportRefunds(filter: RefundRegisterFilter): Promise<DownloadedFile> {
    return this.download(`${BILLING_API.refunds}/export`, refundParams(filter, false), 'refunds.csv');
  }

  // ---- batches ----------------------------------------------------------------------------------------------------

  batchByNumber(number: string, signal?: AbortSignal): Promise<BatchAccountView> {
    return this.require<BatchAccountView>(`${BILLING_API.batches}/by-number/${encodeURIComponent(number)}/account`, signal);
  }

  batchById(batchId: string, signal?: AbortSignal): Promise<BatchAccountView> {
    return this.require<BatchAccountView>(`${BILLING_API.batches}/${encodeURIComponent(batchId)}/account`, signal);
  }

  payBatch(batchId: string, request: PayBatchRequest): Promise<BatchPaymentResult> {
    return firstValueFrom(this.http.post<BatchPaymentResult>(`${BILLING_API.batches}/${encodeURIComponent(batchId)}/pay-all`, request));
  }

  // ---- cash closing -----------------------------------------------------------------------------------------------

  /** The drawer of one cashier (or every cashier: null) on the session branch over the period (LIS-CR-004: no treasury). */
  previewClosing(scope: DrawerScopeFilter, signal?: AbortSignal): Promise<CashClosingPreview> {
    const params = toHttpParams({ cashierUserId: scope.cashierUserId, from: scope.fromUtc, to: scope.toUtc });
    return firstValueFrom(this.http.get<CashClosingPreview>(`${BILLING_API.cashClosings}/preview`, { params })).then(abortable(signal));
  }

  /** The cashiers who moved money on the session branch over the period (the scope choice of `billing.manage`). */
  drawerCashiers(fromUtc: string, toUtc: string, signal?: AbortSignal): Promise<readonly DrawerCashier[]> {
    return this.get<readonly DrawerCashier[]>(`${BILLING_API.cashClosings}/cashiers`, toHttpParams({ from: fromUtc, to: toUtc }), [], signal);
  }

  closeDrawer(request: CloseCashDrawerRequest): Promise<CashClosingCreated> {
    return firstValueFrom(this.http.post<CashClosingCreated>(BILLING_API.cashClosings, request));
  }

  listClosings(filter: CashClosingRegisterFilter, signal?: AbortSignal): Promise<Paged<CashClosingListItem>> {
    return this.get<Paged<CashClosingListItem>>(BILLING_API.cashClosings, closingParams(filter, true), emptyPage<CashClosingListItem>(), signal);
  }

  exportClosings(filter: CashClosingRegisterFilter): Promise<DownloadedFile> {
    return this.download(`${BILLING_API.cashClosings}/export`, closingParams(filter, false), 'cash-closings.csv');
  }

  closingSheet(id: string): Promise<CashClosingSheetDocument> {
    return this.require<CashClosingSheetDocument>(`${BILLING_API.cashClosings}/${encodeURIComponent(id)}/documents/sheet`);
  }

  // ---- collections report (change request 2026-10-13, item 2) --------------------------------------------------

  /** The collections report: whole laboratory days, own or branch scope, one row per branch / cashier / method. */
  collectionsReport(request: CollectionsReportRequest, signal?: AbortSignal): Promise<CollectionsReportDocument> {
    const params = toHttpParams({ from: request.fromDay, to: request.toDay, scope: request.scope, groupBy: request.groupBy });
    return firstValueFrom(this.http.get<CollectionsReportDocument>(BILLING_API.collectionsReport, { params })).then(abortable(signal));
  }

  /**
   * The CP-37 rights that shape the report dialog, read from the member's own privileges (Organization's endpoint, called
   * here so the feature does not import another feature; the server enforces them again with BIL-044 / BIL-045).
   */
  async collectionsReportPrivileges(signal?: AbortSignal): Promise<CollectionsReportPrivileges> {
    const mine = await this.require<MyPrivileges>(ORGANIZATION_API.myPrivileges, signal);
    return { canViewBranchCollections: mine.privileges.specialRights.canViewBranchCollections, reportDaysLimit: mine.privileges.specialRights.reportDaysLimit };
  }

  // ---- payment method definitions (set-up list CP-30b, change request 2026-10-19) ------------------------------------

  /** The active methods in pick-list order (`billing.view`); an empty list when the read fails. */
  paymentMethodOptions(signal?: AbortSignal): Promise<readonly PaymentMethodOption[]> {
    return this.get<readonly PaymentMethodOption[]>(BILLING_API.paymentMethodOptions, new HttpParams(), [], signal);
  }

  listPaymentMethods(filter: PaymentMethodFilter, signal?: AbortSignal): Promise<Paged<PaymentMethodListItem>> {
    return this.get<Paged<PaymentMethodListItem>>(BILLING_API.paymentMethods, methodParams(filter, true), emptyPage<PaymentMethodListItem>(), signal);
  }

  createPaymentMethod(request: PaymentMethodRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(BILLING_API.paymentMethods, request));
  }

  updatePaymentMethod(id: string, request: UpdatePaymentMethodRequest): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${BILLING_API.paymentMethods}/${encodeURIComponent(id)}`, request));
  }

  setPaymentMethodActive(id: string, active: boolean): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${BILLING_API.paymentMethods}/${encodeURIComponent(id)}/${active ? 'reactivate' : 'deactivate'}`, null));
  }

  /** Adds the defaults the laboratory lacks (CASH, CARD, POS, TRANSFER); idempotent (`billing.manage`). */
  ensureDefaultPaymentMethods(): Promise<PaymentMethodDefaultsResult> {
    return firstValueFrom(this.http.post<PaymentMethodDefaultsResult>(`${BILLING_API.paymentMethods}/defaults`, null));
  }

  exportPaymentMethods(filter: PaymentMethodFilter): Promise<DownloadedFile> {
    return this.download(`${BILLING_API.paymentMethods}/export`, methodParams(filter, false), 'payment-methods.csv');
  }

  // ---- plumbing ---------------------------------------------------------------------------------------------------

  private get<T>(url: string, params: HttpParams, fallback: T, signal?: AbortSignal): Promise<T> {
    return firstValueFrom(this.http.get<T>(url, { params }), { defaultValue: fallback }).then(abortable(signal));
  }

  private require<T>(url: string, signal?: AbortSignal): Promise<T> {
    return firstValueFrom(this.http.get<T>(url)).then(abortable(signal));
  }

  private async download(url: string, params: HttpParams, fallbackName: string): Promise<DownloadedFile> {
    const response: HttpResponse<Blob> = await firstValueFrom(this.http.get(url, { params, responseType: 'blob', observe: 'response' }));
    return { blob: response.body ?? new Blob([]), fileName: localCsvName(fallbackName.replace(/\.csv$/i, '')) };
  }
}

function methodParams(filter: PaymentMethodFilter, paged: boolean): HttpParams {
  return toHttpParams({
    search: filter.search,
    isActive: filter.isActive,
    sort: paged ? filter.sort : undefined,
    direction: paged ? filter.direction : undefined,
    page: paged ? filter.page : undefined,
    pageSize: paged ? filter.pageSize : undefined,
  });
}

function paymentParams(filter: PaymentRegisterFilter, paged: boolean): HttpParams {
  return toHttpParams({
    accession: filter.accession,
    from: filter.fromUtc,
    to: filter.toUtc,
    method: filter.method,
    status: filter.status,
    page: paged ? filter.page : null,
    pageSize: paged ? filter.pageSize : null,
    timeZone: paged ? null : browserTimeZone(),
  });
}

function refundParams(filter: RefundRegisterFilter, paged: boolean): HttpParams {
  return toHttpParams({
    accession: filter.accession,
    from: filter.fromUtc,
    to: filter.toUtc,
    method: filter.method,
    page: paged ? filter.page : null,
    pageSize: paged ? filter.pageSize : null,
    timeZone: paged ? null : browserTimeZone(),
  });
}

function closingParams(filter: CashClosingRegisterFilter, paged: boolean): HttpParams {
  return toHttpParams({
    from: filter.fromUtc,
    to: filter.toUtc,
    page: paged ? filter.page : null,
    pageSize: paged ? filter.pageSize : null,
    timeZone: paged ? null : browserTimeZone(),
  });
}

/** The user's IANA time zone: the CSV exports write the registers' times as the screen shows them (UAT R1-06). */
export function browserTimeZone(): string | null {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || null;
  } catch {
    return null;
  }
}

/**
 * The file name of an export in the user's local date, the same pattern as the exports built in the browser (UAT R3-05:
 * the server names its files with a UTC time, e.g. `payments-20261011-1449.csv` at 17:49 local).
 */
export function localCsvName(base: string, now: Date = new Date()): string {
  const pad = (value: number): string => String(value).padStart(2, '0');
  return `${base}-${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}.csv`;
}

/** `attachment; filename=payments-20261009-1400.csv; filename*=UTF-8''…` → the plain name. */
export function fileNameOf(disposition: string | null): string | null {
  if (disposition === null) {
    return null;
  }

  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(disposition)?.[1];
  if (encoded !== undefined) {
    return decodeURIComponent(encoded);
  }

  return /filename="?([^";]+)"?/i.exec(disposition)?.[1] ?? null;
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
