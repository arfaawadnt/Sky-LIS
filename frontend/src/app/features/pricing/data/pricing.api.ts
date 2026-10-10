import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { emptyPage } from '../../../core/api/paging';
import {
  type ApplyBasePriceListResult,
  type BasePriceListDetail,
  type BasePriceListFilter,
  type BasePriceListItem,
  type ContractApplyResult,
  type ContractPricingDetail,
  type ContractPricingListItem,
  type ContractPricingReviewDocument,
  type ContractTierRequest,
  type CopyBasePriceListRequest,
  type CreateBasePriceListRequest,
  type CreatedResource,
  type DefaultPayerIds,
  type NegotiatePriceRequest,
  type Paged,
  type PayerListFilter,
  type PayerListItem,
  type PayerRequest,
  PRICING_API,
  type PriceLineItem,
  type PriceLineRequest,
  type SetContractFormulaRequest,
  type TierOption,
  type UpdateBasePriceListRequest,
  type UpsertPriceLinesResult,
} from '../../../core/api/pricing.contracts';
import { toHttpParams } from '../../../shared/forms/paging';

/** Typed calls of the Pricing module; URLs are built with `HttpParams` and encoded ids, never by string concatenation of user text (pitfall P-60). */
@Injectable({ providedIn: 'root' })
export class PricingApi {
  private readonly http = inject(HttpClient);

  // ---- payers and tiers (CP-32) ----

  listPayers(filter: PayerListFilter, signal?: AbortSignal): Promise<Paged<PayerListItem>> {
    const params = toHttpParams({ search: filter.search, type: filter.type, isActive: filter.isActive, page: filter.page, pageSize: filter.pageSize });
    return firstValueFrom(this.http.get<Paged<PayerListItem>>(PRICING_API.payers, { params }), { defaultValue: emptyPage<PayerListItem>() }).then(abortable(signal));
  }

  getPayer(id: string, signal?: AbortSignal): Promise<PayerListItem> {
    return firstValueFrom(this.http.get<PayerListItem>(this.payer(id))).then(abortable(signal));
  }

  createPayer(request: PayerRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(PRICING_API.payers, request));
  }

  updatePayer(id: string, request: PayerRequest): Promise<void> {
    return firstValueFrom(this.http.put<void>(this.payer(id), request), { defaultValue: undefined });
  }

  setPayerActive(id: string, active: boolean): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${this.payer(id)}/${active ? 'reactivate' : 'deactivate'}`, null), { defaultValue: undefined });
  }

  /** The tenant's cash payer (created on first call, intake Q1). */
  ensureDefaultPayer(): Promise<DefaultPayerIds> {
    return firstValueFrom(this.http.post<DefaultPayerIds>(PRICING_API.defaultPayer, null));
  }

  addTier(payerId: string, request: ContractTierRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(`${this.payer(payerId)}/tiers`, request));
  }

  renameTier(payerId: string, tierId: string, request: ContractTierRequest): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${this.payer(payerId)}/tiers/${encodeURIComponent(tierId)}`, request), { defaultValue: undefined });
  }

  setTierActive(payerId: string, tierId: string, active: boolean): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${this.payer(payerId)}/tiers/${encodeURIComponent(tierId)}/${active ? 'reactivate' : 'deactivate'}`, null), { defaultValue: undefined });
  }

  listTierOptions(activeOnly: boolean, signal?: AbortSignal): Promise<readonly TierOption[]> {
    const params = new HttpParams({ fromObject: { activeOnly } });
    return firstValueFrom(this.http.get<readonly TierOption[]>(PRICING_API.tierOptions, { params }), { defaultValue: [] }).then(abortable(signal));
  }

  // ---- base price lists (CP-33) ----

  listBasePriceLists(filter: BasePriceListFilter, signal?: AbortSignal): Promise<Paged<BasePriceListItem>> {
    const params = toHttpParams({ search: filter.search, year: filter.year, isActive: filter.isActive, page: filter.page, pageSize: filter.pageSize });
    return firstValueFrom(this.http.get<Paged<BasePriceListItem>>(PRICING_API.basePriceLists, { params }), { defaultValue: emptyPage<BasePriceListItem>() }).then(abortable(signal));
  }

  getBasePriceList(id: string, signal?: AbortSignal): Promise<BasePriceListDetail> {
    return firstValueFrom(this.http.get<BasePriceListDetail>(this.list(id))).then(abortable(signal));
  }

  createBasePriceList(request: CreateBasePriceListRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(PRICING_API.basePriceLists, request));
  }

  updateBasePriceList(id: string, request: UpdateBasePriceListRequest): Promise<void> {
    return firstValueFrom(this.http.put<void>(this.list(id), request), { defaultValue: undefined });
  }

  setBasePriceListActive(id: string, active: boolean): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${this.list(id)}/${active ? 'activate' : 'deactivate'}`, null), { defaultValue: undefined });
  }

  getPriceLines(id: string, signal?: AbortSignal): Promise<readonly PriceLineItem[]> {
    return firstValueFrom(this.http.get<readonly PriceLineItem[]>(`${this.list(id)}/lines`), { defaultValue: [] }).then(abortable(signal));
  }

  upsertPriceLines(id: string, lines: readonly PriceLineRequest[]): Promise<UpsertPriceLinesResult> {
    return firstValueFrom(this.http.put<UpsertPriceLinesResult>(`${this.list(id)}/lines`, { lines }));
  }

  copyBasePriceList(id: string, request: CopyBasePriceListRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(`${this.list(id)}/copy`, request));
  }

  applyBasePriceList(id: string): Promise<ApplyBasePriceListResult> {
    return firstValueFrom(this.http.post<ApplyBasePriceListResult>(`${this.list(id)}/apply`, null));
  }

  // ---- contract pricing (CP-34) ----

  listContractPricings(payerId: string | null, signal?: AbortSignal): Promise<readonly ContractPricingListItem[]> {
    const params = toHttpParams({ payerId });
    return firstValueFrom(this.http.get<readonly ContractPricingListItem[]>(PRICING_API.contractPricing, { params }), { defaultValue: [] }).then(abortable(signal));
  }

  getContractPricing(tierId: string, signal?: AbortSignal): Promise<ContractPricingDetail> {
    return firstValueFrom(this.http.get<ContractPricingDetail>(this.contract(tierId))).then(abortable(signal));
  }

  setFormula(tierId: string, request: SetContractFormulaRequest): Promise<void> {
    return firstValueFrom(this.http.put<void>(this.contract(tierId), request), { defaultValue: undefined });
  }

  applyContractPricing(tierId: string): Promise<ContractApplyResult> {
    return firstValueFrom(this.http.post<ContractApplyResult>(`${this.contract(tierId)}/apply`, null));
  }

  negotiatePrice(tierId: string, serviceId: string, request: NegotiatePriceRequest): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${this.contract(tierId)}/prices/${encodeURIComponent(serviceId)}`, request), { defaultValue: undefined });
  }

  clearExclusion(tierId: string, serviceId: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.contract(tierId)}/prices/${encodeURIComponent(serviceId)}/exclusion`), { defaultValue: undefined });
  }

  setAlert(tierId: string, serviceId: string, alertCondition: string | null): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${this.contract(tierId)}/prices/${encodeURIComponent(serviceId)}/alert`, { alertCondition }), { defaultValue: undefined });
  }

  copyContractPricing(tierId: string, sourceTierId: string, factor: number | null): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${this.contract(tierId)}/copy-from/${encodeURIComponent(sourceTierId)}`, { factor }), { defaultValue: undefined });
  }

  getReview(tierId: string): Promise<ContractPricingReviewDocument> {
    return firstValueFrom(this.http.get<ContractPricingReviewDocument>(`${this.contract(tierId)}/review`));
  }

  private payer(id: string): string {
    return `${PRICING_API.payers}/${encodeURIComponent(id)}`;
  }

  private list(id: string): string {
    return `${PRICING_API.basePriceLists}/${encodeURIComponent(id)}`;
  }

  private contract(tierId: string): string {
    return `${PRICING_API.contractPricing}/${encodeURIComponent(tierId)}`;
  }
}

/** Resources pass an abort signal; a late result of an aborted load is dropped. */
export function abortable<T>(signal: AbortSignal | undefined): (value: T) => T {
  return (value) => {
    if (signal?.aborted) {
      throw new DOMException('aborted', 'AbortError');
    }

    return value;
  };
}
