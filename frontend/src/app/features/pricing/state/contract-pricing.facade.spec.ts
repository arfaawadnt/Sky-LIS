import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { PRICING_API } from '../../../core/api/pricing.contracts';
import { GOLD, LISTS_PAGE, TIER_OPTIONS } from '../testing/pricing.fixtures';
import { ContractPricingFacade } from './contract-pricing.facade';
import { ServiceNamesStore } from './service-names.store';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

describe('ContractPricingFacade (CP-34)', () => {
  let facade: ContractPricingFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), ContractPricingFacade, ServiceNamesStore] });
    facade = TestBed.inject(ContractPricingFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  async function openGold(): Promise<void> {
    facade.select('t-gold');
    await runEffects();
    backend.expectOne((request) => request.url === PRICING_API.tierOptions).flush(TIER_OPTIONS);
    backend.expectOne((request) => request.url === PRICING_API.basePriceLists).flush(LISTS_PAGE);
    backend.expectOne(`${PRICING_API.contractPricing}/t-gold`).flush(GOLD);
    await runEffects();
  }

  it('loads the tier, its prices with names and the copy sources', async () => {
    await openGold();

    expect(facade.prices().length).toBe(2);
    expect(facade.excludedCount()).toBe(1);
    expect(facade.copySources().map((option) => option.id)).toEqual(['t-silver']);
    facade.search.set('approval');
    expect(facade.visiblePrices().map((price) => price.serviceId)).toEqual(['s-lip']);
    facade.search.set('cbc');
    expect(facade.visiblePrices().map((price) => price.serviceId)).toEqual(['s-cbc']);
    backend.verify();
  });

  it('saves the formula, then reloads the tier and the options', async () => {
    await openGold();

    const saving = facade.saveFormula.execute({ tierId: 't-gold', request: { effectiveFrom: '2026-07-01', effectiveTo: null, terms: [{ baseListId: 'inst', patientFactor: 0.2, payerFactor: 0.8 }] } });
    await settle();
    const put = backend.expectOne({ method: 'PUT', url: `${PRICING_API.contractPricing}/t-gold` });
    expect(put.request.body.terms).toEqual([{ baseListId: 'inst', patientFactor: 0.2, payerFactor: 0.8 }]);
    put.flush(null, { status: 204, statusText: 'No Content' });
    await saving;
    await runEffects();

    backend.expectOne(`${PRICING_API.contractPricing}/t-gold`).flush(GOLD);
    backend.expectOne((request) => request.url === PRICING_API.tierOptions).flush(TIER_OPTIONS);
    await runEffects();
    backend.verify();
  });

  it('applies, negotiates, restores, sets an alert and copies through their endpoints', async () => {
    await openGold();

    const calls: [Promise<unknown>, string, string][] = [
      [facade.apply.execute('t-gold'), 'POST', `${PRICING_API.contractPricing}/t-gold/apply`],
      [facade.negotiate.execute({ tierId: 't-gold', serviceId: 's-cbc', request: { patientShare: 10, payerShare: 150 } }), 'PUT', `${PRICING_API.contractPricing}/t-gold/prices/s-cbc`],
      [facade.clearExclusion.execute({ tierId: 't-gold', serviceId: 's-lip' }), 'DELETE', `${PRICING_API.contractPricing}/t-gold/prices/s-lip/exclusion`],
      [facade.setAlert.execute({ tierId: 't-gold', serviceId: 's-cbc', alertCondition: 'Referral letter' }), 'PUT', `${PRICING_API.contractPricing}/t-gold/prices/s-cbc/alert`],
      [facade.copyFrom.execute({ tierId: 't-gold', sourceTierId: 't-silver', factor: 0.9 }), 'POST', `${PRICING_API.contractPricing}/t-gold/copy-from/t-silver`],
    ];
    await settle();
    for (const [, method, url] of calls) {
      const request = backend.expectOne({ method, url });
      if (url.endsWith('/alert')) {
        expect(request.request.body).toEqual({ alertCondition: 'Referral letter' });
      }

      if (url.includes('copy-from')) {
        expect(request.request.body).toEqual({ factor: 0.9 });
      }

      request.flush(url.endsWith('/apply') ? { priced: 2, added: 0, updated: 1, removed: 0, excludedKept: 1 } : null, { status: url.endsWith('/apply') ? 200 : 204, statusText: 'OK' });
    }

    await Promise.all(calls.map(([pending]) => pending));
    await runEffects();
    for (const request of backend.match(`${PRICING_API.contractPricing}/t-gold`)) {
      request.flush(GOLD);
    }

    for (const request of backend.match((candidate) => candidate.url === PRICING_API.tierOptions)) {
      request.flush(TIER_OPTIONS);
    }

    await runEffects();
    backend.verify();
  });
});
