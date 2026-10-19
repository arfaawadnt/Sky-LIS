import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { type Observable, firstValueFrom } from 'rxjs';

import { BILLING_API, type BillingTreasuryItem, type PaymentCreated, type PaymentReceiptDocument, type RecordPaymentRequest, type VisitAccountView } from '../../../core/api/billing.contracts';
import { CATALOGUE_A_API, type ServiceLookupItem } from '../../../core/api/catalogue-a.contracts';
import { CATALOGUE_B_API, type DiagnosisListItem, type DoctorListItem, type ReasonKind, type ReasonListItem } from '../../../core/api/catalogue-b.contracts';
import { emptyPage, type Paged } from '../../../core/api/paging';
import { PRICING_API, type TierOption } from '../../../core/api/pricing.contracts';
import {
  type AddVisitLinesRequest,
  type CancelLinesRequest,
  type CheckOrderRequest,
  type JobOrderDocument,
  type OrderCheckResult,
  type PreparationQuestionItem,
  type PriceQuoteDocument,
  type PriceQuoteRequest,
  REGISTRATION_API,
  type RegisterVisitRequest,
  type RegistrationPageSettings,
  type ServiceEntryResult,
  type SetVisitDiscountRequest,
  type UpdateVisitDetailsRequest,
  type VisitDetail,
  type VisitLabelsDocument,
  type VisitLinesAddedResponse,
  type VisitListItem,
  type VisitRegisteredResponse,
  type VisitSearchFilter,
} from '../../../core/api/registration.contracts';
import { RESULTS_API, type ResultCaseView } from '../../../core/api/results.contracts';
import { SAMPLES_API, type SampleDetail, type SampleListItem, type ScanSampleRequest } from '../../../core/api/samples.contracts';
import { classifySearch } from './registration-format';

/** The default the page uses when the parameters cannot be read (a role without the view): print nothing automatically. */
export const FALLBACK_SETTINGS: RegistrationPageSettings = {
  autoPrintLabels: false,
  autoPrintJobOrder: false,
  autoPrintReceipt: false,
  labelCopies: 1,
  mandatoryMobile: false,
  mandatoryDoctor: false,
  mandatoryNationalId: false,
  discountCapPercent: 0,
  canCancel: false,
  canTransfer: false,
  canPay: false,
  canSeeVip: false,
  currency: 'EGP',
};

/**
 * Typed calls of the Registration pages (01 §6.3): the module's own endpoints, and the reference lists of other modules
 * read tolerantly (a list the role may not read, or a module not deployed yet, is an empty list so the page degrades
 * instead of breaking). URLs are built with `HttpParams`, never by string concatenation (pitfall P-60).
 */
@Injectable({ providedIn: 'root' })
export class RegistrationApi {
  private readonly http = inject(HttpClient);

  // ---- visits ----------------------------------------------------------------------------------------------------------

  search(filter: VisitSearchFilter, signal?: AbortSignal): Promise<Paged<VisitListItem>> {
    return firstValueFrom(this.http.get<Paged<VisitListItem>>(REGISTRATION_API.visits, { params: searchParams(filter, true) }), { defaultValue: emptyPage<VisitListItem>() }).then(abortable(signal));
  }

  export(filter: VisitSearchFilter): Promise<readonly VisitListItem[]> {
    return firstValueFrom(this.http.get<readonly VisitListItem[]>(REGISTRATION_API.exportVisits, { params: searchParams(filter, false) }), { defaultValue: [] });
  }

  get(id: string, signal?: AbortSignal): Promise<VisitDetail> {
    return firstValueFrom(this.http.get<VisitDetail>(`${REGISTRATION_API.visits}/${encodeURIComponent(id)}`)).then(abortable(signal));
  }

  check(request: CheckOrderRequest, signal?: AbortSignal): Promise<OrderCheckResult> {
    return firstValueFrom(this.http.post<OrderCheckResult>(REGISTRATION_API.check, request)).then(abortable(signal));
  }

  register(request: RegisterVisitRequest): Promise<VisitRegisteredResponse> {
    return firstValueFrom(this.http.post<VisitRegisteredResponse>(REGISTRATION_API.visits, request));
  }

  addLines(id: string, request: AddVisitLinesRequest): Promise<VisitLinesAddedResponse> {
    return firstValueFrom(this.http.post<VisitLinesAddedResponse>(`${REGISTRATION_API.visits}/${encodeURIComponent(id)}/lines`, request));
  }

  // 204 answers carry no body: these resolve to `undefined`, because a mutation's `null` means failure (core/state/mutation.ts).

  release(id: string, lineIds: readonly string[]): Promise<void> {
    return firstValueFrom(this.http.post<null>(`${REGISTRATION_API.visits}/${encodeURIComponent(id)}/lines/release`, { lineIds }), { defaultValue: null }).then(() => undefined);
  }

  cancel(id: string, request: CancelLinesRequest): Promise<void> {
    return firstValueFrom(this.http.post<null>(`${REGISTRATION_API.visits}/${encodeURIComponent(id)}/lines/cancel`, request), { defaultValue: null }).then(() => undefined);
  }

  updateDetails(id: string, request: UpdateVisitDetailsRequest): Promise<void> {
    return firstValueFrom(this.http.put<null>(`${REGISTRATION_API.visits}/${encodeURIComponent(id)}/details`, request), { defaultValue: null }).then(() => undefined);
  }

  setDiscount(id: string, request: SetVisitDiscountRequest): Promise<void> {
    return firstValueFrom(this.http.put<null>(`${REGISTRATION_API.visits}/${encodeURIComponent(id)}/discount`, request), { defaultValue: null }).then(() => undefined);
  }

  labels(id: string): Promise<VisitLabelsDocument> {
    return firstValueFrom(this.http.get<VisitLabelsDocument>(`${REGISTRATION_API.visits}/${encodeURIComponent(id)}/documents/labels`));
  }

  jobOrder(id: string, signal?: AbortSignal): Promise<JobOrderDocument> {
    return firstValueFrom(this.http.get<JobOrderDocument>(`${REGISTRATION_API.visits}/${encodeURIComponent(id)}/documents/job-order`)).then(abortable(signal));
  }

  // ---- order aids --------------------------------------------------------------------------------------------------------

  settings(signal?: AbortSignal): Promise<RegistrationPageSettings> {
    return firstValueFrom(this.http.get<RegistrationPageSettings>(REGISTRATION_API.settings), { defaultValue: FALLBACK_SETTINGS })
      .catch(() => FALLBACK_SETTINGS)
      .then(abortable(signal));
  }

  serviceEntry(text: string): Promise<ServiceEntryResult> {
    return firstValueFrom(this.http.get<ServiceEntryResult>(REGISTRATION_API.serviceEntry, { params: new HttpParams({ fromObject: { text } }) }));
  }

  preparations(serviceIds: readonly string[], gender: string | null, signal?: AbortSignal): Promise<readonly PreparationQuestionItem[]> {
    let params = new HttpParams();
    for (const id of serviceIds) {
      params = params.append('serviceIds', id);
    }

    if (gender) {
      params = params.set('gender', gender);
    }

    return firstValueFrom(this.http.get<readonly PreparationQuestionItem[]>(REGISTRATION_API.preparations, { params }), { defaultValue: [] }).then(abortable(signal));
  }

  quote(request: PriceQuoteRequest, signal?: AbortSignal): Promise<PriceQuoteDocument> {
    return firstValueFrom(this.http.post<PriceQuoteDocument>(REGISTRATION_API.quote, request)).then(abortable(signal));
  }

  /** Catalogue A's picker (F2): active services; needs `catalogue.view`. */
  /** Orderable active services of the catalogue; an empty `search` is the first page (the F2 browse, UAT A-13). */
  lookupServices(search: string, signal?: AbortSignal): Promise<Paged<ServiceLookupItem>> {
    const params = new HttpParams({ fromObject: search.trim() === '' ? { orderable: true, page: 1, pageSize: 50 } : { search, orderable: true, page: 1, pageSize: 50 } });
    return firstValueFrom(this.http.get<Paged<ServiceLookupItem>>(CATALOGUE_A_API.lookupServices, { params }), { defaultValue: emptyPage<ServiceLookupItem>() }).then(abortable(signal));
  }

  // ---- reference lists of other modules (tolerant) ------------------------------------------------------------------

  /**
   * Active tiers for the contract picker. A laboratory that never priced anything has no payer yet: the cash payer and
   * its default tier are provisioned lazily (Pricing intake Q1, `POST payers/default`, idempotent) so the first
   * registration can be a cash one; a refusal (no pricing.view) just leaves the list as it is.
   */
  async tiers(signal?: AbortSignal): Promise<readonly TierOption[]> {
    const load = (): Promise<readonly TierOption[]> =>
      tolerant(this.http.get<readonly TierOption[]>(PRICING_API.tierOptions, { params: new HttpParams({ fromObject: { activeOnly: true } }) }), signal);
    const options = await load();
    if (options.some((tier) => tier.isDefault && tier.payerType === 'Cash')) {
      return options;
    }

    const provisioned = await firstValueFrom(this.http.post<unknown>(PRICING_API.defaultPayer, null)).then(() => true, () => false);
    return provisioned ? load() : options;
  }

  doctors(branchId: string | null, signal?: AbortSignal): Promise<readonly DoctorListItem[]> {
    const params = new HttpParams({ fromObject: { isActive: true, page: 1, pageSize: 200, ...(branchId ? { branchId } : {}) } });
    return tolerantPage(this.http.get<Paged<DoctorListItem>>(CATALOGUE_B_API.doctors, { params }), signal);
  }

  diagnoses(signal?: AbortSignal): Promise<readonly DiagnosisListItem[]> {
    const params = new HttpParams({ fromObject: { isActive: true, page: 1, pageSize: 200 } });
    return tolerantPage(this.http.get<Paged<DiagnosisListItem>>(CATALOGUE_B_API.diagnoses, { params }), signal);
  }

  reasons(kind: ReasonKind, signal?: AbortSignal): Promise<readonly ReasonListItem[]> {
    const params = new HttpParams({ fromObject: { kind, isActive: true, page: 1, pageSize: 200 } });
    return tolerantPage(this.http.get<Paged<ReasonListItem>>(CATALOGUE_B_API.reasons, { params }), signal);
  }

  /** Treasuries approved for the current user on this device in the session branch (BR-27); empty when Billing refuses. */
  myTreasuries(deviceId: string, signal?: AbortSignal): Promise<readonly BillingTreasuryItem[]> {
    return tolerant(this.http.get<readonly BillingTreasuryItem[]>(BILLING_API.myTreasuries, { params: new HttpParams({ fromObject: { deviceId } }) }), signal);
  }

  /** BR-26: the payment captured on the registration page, posted right after the visit is saved. */
  pay(request: RecordPaymentRequest): Promise<PaymentCreated> {
    return firstValueFrom(this.http.post<PaymentCreated>(BILLING_API.payments, request));
  }

  receipt(paymentId: string): Promise<PaymentReceiptDocument> {
    return firstValueFrom(this.http.get<PaymentReceiptDocument>(`${BILLING_API.payments}/${encodeURIComponent(paymentId)}/documents/receipt`));
  }

  /** The samples of a visit (CP-10 samples dialog, R1). */
  samples(visitId: string, signal?: AbortSignal): Promise<readonly SampleListItem[]> {
    const params = new HttpParams({ fromObject: { visitId, page: 1, pageSize: 100 } });
    return tolerantPage(this.http.get<Paged<SampleListItem>>(SAMPLES_API.samples, { params }), signal);
  }

  // ---- accession details (CP-10b, change request 2026-10-19 LIS-CR-003 P-C9) --------------------------------------

  /**
   * The visit whose accession is exactly `accession` (any day, any branch): the register's accession filter is a
   * contains-match, so the answer is narrowed here; null when there is none.
   */
  async findByAccession(accession: string, signal?: AbortSignal): Promise<VisitListItem | null> {
    const wanted = accession.trim().toUpperCase();
    if (wanted === '') {
      return null;
    }

    const found = await this.search({ ...EMPTY_VISIT_FILTER, accession: wanted, page: 1, pageSize: 10 }, signal);
    return found.items.find((item) => item.accession.toUpperCase() === wanted) ?? null;
  }

  /** The visits a desk search text matches (accession, patient number, mobile or name as CP-10 classifies it), any day. */
  searchVisits(text: string, signal?: AbortSignal): Promise<Paged<VisitListItem>> {
    const search = classifySearch(text);
    if (search === null) {
      return Promise.resolve(emptyPage<VisitListItem>());
    }

    return this.search({ ...EMPTY_VISIT_FILTER, [search.target]: search.value, page: 1, pageSize: 20 }, signal);
  }

  /** Billing's account of the visit (balance, posted payments and refunds); null when Billing refuses or is absent. */
  visitAccount(visitId: string, signal?: AbortSignal): Promise<VisitAccountView | null> {
    return tolerantOne(this.http.get<VisitAccountView>(`${BILLING_API.visits}/${encodeURIComponent(visitId)}/account`), signal);
  }

  /** The result case of the visit (status per report unit and per result); null when Results refuses or has nothing. */
  resultCase(visitId: string, signal?: AbortSignal): Promise<ResultCaseView | null> {
    return tolerantOne(this.http.get<ResultCaseView>(RESULTS_API.results, { params: new HttpParams({ fromObject: { visitId } }) }), signal);
  }

  /** Collects one sample by its number — the phlebotomy scan of CP-14 (`samples.create`); the server refuses anything not Ordered. */
  collectSample(sampleNumber: string): Promise<SampleDetail> {
    const request: ScanSampleRequest = { sampleNumber, mode: 'Collect' };
    return firstValueFrom(this.http.post<SampleDetail>(SAMPLES_API.scan, request));
  }
}

/** Every filter of the register off: a lookup by one key across all days and branches. */
const EMPTY_VISIT_FILTER: VisitSearchFilter = {
  accession: null,
  patientNo: null,
  name: null,
  mobile: null,
  from: null,
  to: null,
  branchId: null,
  payerId: null,
  status: null,
  patientId: null,
  page: 1,
  pageSize: 20,
};

function tolerantOne<T>(request: Observable<T>, signal?: AbortSignal): Promise<T | null> {
  return firstValueFrom(request)
    .then((value): T | null => value)
    .catch((): T | null => null)
    .then(abortable(signal));
}

function searchParams(filter: VisitSearchFilter, paged: boolean): HttpParams {
  const values: Record<string, string | number> = {};
  const add = (key: string, value: string | number | null): void => {
    if (value !== null && value !== '') {
      values[key] = value;
    }
  };

  add('accession', filter.accession);
  add('patientNo', filter.patientNo);
  add('name', filter.name);
  add('mobile', filter.mobile);
  add('from', filter.from);
  add('to', filter.to);
  add('branchId', filter.branchId);
  add('payerId', filter.payerId);
  add('status', filter.status);
  add('patientId', filter.patientId);
  if (paged) {
    add('page', filter.page);
    add('pageSize', filter.pageSize);
  }

  return new HttpParams({ fromObject: values });
}

function tolerant<T>(request: Observable<readonly T[]>, signal?: AbortSignal): Promise<readonly T[]> {
  return firstValueFrom(request, { defaultValue: [] as readonly T[] })
    .catch((): readonly T[] => [])
    .then(abortable(signal));
}

function tolerantPage<T>(request: Observable<Paged<T>>, signal?: AbortSignal): Promise<readonly T[]> {
  return firstValueFrom(request, { defaultValue: emptyPage<T>() })
    .then((page) => page.items)
    .catch((): readonly T[] => [])
    .then(abortable(signal));
}

/** Resources pass an abort signal; HttpClient has no fetch-level abort here, so a late result for an aborted load is dropped. */
export function abortable<T>(signal: AbortSignal | undefined): (value: T) => T {
  return (value) => {
    if (signal?.aborted) {
      throw new DOMException('aborted', 'AbortError');
    }

    return value;
  };
}
