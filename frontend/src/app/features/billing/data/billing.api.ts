import { HttpClient, HttpParams, type HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import {
  BILLING_API,
  type BatchAccountView,
  type BatchPaymentResult,
  type BillingReasonOption,
  type BillingTreasuryItem,
  type CashClosingCreated,
  type CashClosingListItem,
  type CashClosingPreview,
  type CashClosingRegisterFilter,
  type CashClosingSheetDocument,
  type CloseCashDrawerRequest,
  type DrawerCashier,
  type DrawerScopeFilter,
  type InvoiceDocument,
  type Paged,
  type PayBatchRequest,
  type PaymentCreated,
  type PaymentListItem,
  type PaymentReceiptDocument,
  type PaymentRegisterFilter,
  type RecordPaymentRequest,
  type RecordRefundRequest,
  type RefundCreated,
  type RefundListItem,
  type RefundReceiptDocument,
  type RefundRegisterFilter,
  type VisitAccountView,
} from '../../../core/api/billing.contracts';
import { emptyPage } from '../../../core/api/paging';
import { SETTINGS_API, SETTING_KEYS, type TenantSettingsResponse } from '../../../core/api/settings.contracts';
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

  // ---- treasuries and parameters ------------------------------------------------------------------------------

  /** The active treasuries of the session branch approved for the current user on this device (BR-27). */
  myTreasuries(deviceId: string, signal?: AbortSignal): Promise<readonly BillingTreasuryItem[]> {
    return this.get<readonly BillingTreasuryItem[]>(BILLING_API.myTreasuries, new HttpParams({ fromObject: { deviceId } }), [], signal);
  }

  /** Every treasury of the session branch (closing page, register filters). */
  branchTreasuries(signal?: AbortSignal): Promise<readonly BillingTreasuryItem[]> {
    return this.get<readonly BillingTreasuryItem[]>(BILLING_API.treasuries, new HttpParams(), [], signal);
  }

  /**
   * `autoPrintReceipt` (CP-57) through `/api/v1/settings`. A cashier without `settings.view` (403) or any failure reads the
   * code default (true), so receipts still print (intake Q18).
   */
  async autoPrintReceipt(signal?: AbortSignal): Promise<boolean> {
    try {
      const response = await this.get<TenantSettingsResponse | null>(SETTINGS_API.settings, new HttpParams(), null, signal);
      const setting = response?.settings.find((item) => item.key === SETTING_KEYS.autoPrintReceipt);
      return setting === undefined ? true : setting.value.toLowerCase() === 'true';
    } catch (error: unknown) {
      if (error instanceof DOMException && error.name === 'AbortError') {
        throw error;
      }

      return true;
    }
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

  previewClosing(scope: DrawerScopeFilter, signal?: AbortSignal): Promise<CashClosingPreview> {
    const params = toHttpParams({ treasuryId: scope.treasuryId, cashierUserId: scope.cashierUserId, from: scope.fromUtc, to: scope.toUtc });
    return firstValueFrom(this.http.get<CashClosingPreview>(`${BILLING_API.cashClosings}/preview`, { params })).then(abortable(signal));
  }

  drawerCashiers(treasuryId: string, fromUtc: string, toUtc: string, signal?: AbortSignal): Promise<readonly DrawerCashier[]> {
    return this.get<readonly DrawerCashier[]>(`${BILLING_API.cashClosings}/cashiers`, toHttpParams({ treasuryId, from: fromUtc, to: toUtc }), [], signal);
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

  // ---- plumbing ---------------------------------------------------------------------------------------------------

  private get<T>(url: string, params: HttpParams, fallback: T, signal?: AbortSignal): Promise<T> {
    return firstValueFrom(this.http.get<T>(url, { params }), { defaultValue: fallback }).then(abortable(signal));
  }

  private require<T>(url: string, signal?: AbortSignal): Promise<T> {
    return firstValueFrom(this.http.get<T>(url)).then(abortable(signal));
  }

  private async download(url: string, params: HttpParams, fallbackName: string): Promise<DownloadedFile> {
    const response: HttpResponse<Blob> = await firstValueFrom(this.http.get(url, { params, responseType: 'blob', observe: 'response' }));
    return { blob: response.body ?? new Blob([]), fileName: fileNameOf(response.headers.get('Content-Disposition')) ?? fallbackName };
  }
}

function paymentParams(filter: PaymentRegisterFilter, paged: boolean): HttpParams {
  return toHttpParams({
    accession: filter.accession,
    from: filter.fromUtc,
    to: filter.toUtc,
    treasuryId: filter.treasuryId,
    method: filter.method,
    status: filter.status,
    page: paged ? filter.page : null,
    pageSize: paged ? filter.pageSize : null,
  });
}

function refundParams(filter: RefundRegisterFilter, paged: boolean): HttpParams {
  return toHttpParams({
    accession: filter.accession,
    from: filter.fromUtc,
    to: filter.toUtc,
    treasuryId: filter.treasuryId,
    method: filter.method,
    page: paged ? filter.page : null,
    pageSize: paged ? filter.pageSize : null,
  });
}

function closingParams(filter: CashClosingRegisterFilter, paged: boolean): HttpParams {
  return toHttpParams({
    treasuryId: filter.treasuryId,
    from: filter.fromUtc,
    to: filter.toUtc,
    page: paged ? filter.page : null,
    pageSize: paged ? filter.pageSize : null,
  });
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
