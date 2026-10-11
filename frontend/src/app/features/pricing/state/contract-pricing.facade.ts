import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { ContractPriceItem, ContractPricingDetail, ContractPricingReviewDocument, NegotiatePriceRequest, SetContractFormulaRequest } from '../../../core/api/pricing.contracts';
import { mutation } from '../../../core/state/mutation';
import { PricingApi } from '../data/pricing.api';
import { ServiceNamesStore } from './service-names.store';

/** Base lists offered by the formula selects (all of them; inactive ones are marked, Apply skips them). */
const LIST_PAGE_SIZE = 200;

/**
 * Contract pricing of one tier (CP-34): the tier picker's options, the base lists for the formula, the tier's formula
 * and derived prices, and the actions of the page — save the formula, Apply (BR-07), negotiate / restore a price, set
 * an alert, copy from another tier, fetch the review report. Provided at the route with `ServiceNamesStore`.
 */
@Injectable()
export class ContractPricingFacade {
  private readonly api = inject(PricingApi);
  readonly names = inject(ServiceNamesStore);

  readonly tierId = signal<string | null>(null);

  readonly search = signal('');

  readonly tierOptions = resource({
    loader: ({ abortSignal }) => this.api.listTierOptions(false, abortSignal),
  });

  readonly baseLists = resource({
    loader: ({ abortSignal }) => this.api.listBasePriceLists({ search: '', year: null, isActive: null, page: 1, pageSize: LIST_PAGE_SIZE }, abortSignal),
  });

  readonly detail = resource({
    params: () => this.tierId() ?? undefined,
    loader: async ({ params, abortSignal }): Promise<ContractPricingDetail> => {
      const detail = await this.api.getContractPricing(params, abortSignal);
      this.names.rememberRows(detail.prices);
      return detail;
    },
  });

  readonly prices = computed<readonly ContractPriceItem[]>(() => (this.detail.hasValue() ? this.detail.value().prices : []));

  readonly visiblePrices = computed<readonly ContractPriceItem[]>(() => {
    const term = this.search().trim().toLowerCase();
    const prices = this.prices();
    if (term === '') {
      return prices;
    }

    return prices.filter((price) => {
      const label = this.names.label(price.serviceId);
      return [label?.code, label?.nameEn, label?.nameAr, price.sourceListCode, price.alertCondition].some((text) => (text ?? '').toLowerCase().includes(term));
    });
  });

  readonly excludedCount = computed(() => this.prices().filter((price) => price.isExcluded).length);

  /** Tiers another tier can be copied from: those with a contract pricing, except the current one. */
  readonly copySources = computed(() => (this.tierOptions.hasValue() ? this.tierOptions.value() : []).filter((option) => option.hasPricing && option.id !== this.tierId()));

  readonly saveFormula = mutation((input: { tierId: string; request: SetContractFormulaRequest }) => this.api.setFormula(input.tierId, input.request), {
    then: () => this.afterChange(true),
  });

  readonly apply = mutation((tierId: string) => this.api.applyContractPricing(tierId), { then: () => this.afterChange(false) });

  readonly negotiate = mutation((input: { tierId: string; serviceId: string; request: NegotiatePriceRequest }) => this.api.negotiatePrice(input.tierId, input.serviceId, input.request), {
    then: () => this.afterChange(false),
  });

  readonly clearExclusion = mutation((input: { tierId: string; serviceId: string }) => this.api.clearExclusion(input.tierId, input.serviceId), { then: () => this.afterChange(false) });

  readonly setAlert = mutation((input: { tierId: string; serviceId: string; alertCondition: string | null }) => this.api.setAlert(input.tierId, input.serviceId, input.alertCondition), {
    then: () => this.afterChange(false),
  });

  readonly copyFrom = mutation((input: { tierId: string; sourceTierId: string; factor: number }) => this.api.copyContractPricing(input.tierId, input.sourceTierId, input.factor), {
    then: () => this.afterChange(true),
  });

  select(tierId: string | null): void {
    if (this.tierId() !== tierId) {
      this.search.set('');
      for (const action of [this.saveFormula, this.apply, this.negotiate, this.clearExclusion, this.setAlert, this.copyFrom]) {
        action.reset();
      }

      this.tierId.set(tierId);
    }
  }

  /** Re-reads the tiers, the price lists and the open tier when a page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14). */
  refresh(): void {
    this.tierOptions.reload();
    this.baseLists.reload();
    if (this.tierId() !== null) {
      this.detail.reload();
    }
  }

  review(tierId: string): Promise<ContractPricingReviewDocument> {
    return this.api.getReview(tierId);
  }

  private afterChange(tiersToo: boolean): void {
    this.detail.reload();
    if (tiersToo) {
      this.tierOptions.reload();
    }
  }
}
