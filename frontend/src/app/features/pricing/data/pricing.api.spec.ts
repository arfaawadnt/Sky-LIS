import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { PRICING_API } from '../../../core/api/pricing.contracts';
import { mutation } from '../../../core/state/mutation';
import { PricingApi } from './pricing.api';

/** UAT B-06 / C-02: an empty 204 answer is a success; `mutation()` reads a `null` result as a failure. */
describe('PricingApi empty answers', () => {
  let api: PricingApi;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(PricingApi);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  const request = { nameEn: 'Gold', nameAr: 'ذهبي' };
  const cases: readonly (readonly [string, (api: PricingApi) => Promise<void>, string, string])[] = [
    ['update a payer', (a) => a.updatePayer('p1', {} as never), 'PUT', `${PRICING_API.payers}/p1`],
    ['deactivate a payer', (a) => a.setPayerActive('p1', false), 'POST', `${PRICING_API.payers}/p1/deactivate`],
    ['rename a tier', (a) => a.renameTier('p1', 't1', request), 'PUT', `${PRICING_API.payers}/p1/tiers/t1`],
    ['reactivate a tier', (a) => a.setTierActive('p1', 't1', true), 'POST', `${PRICING_API.payers}/p1/tiers/t1/reactivate`],
    ['update a price list', (a) => a.updateBasePriceList('l1', {} as never), 'PUT', `${PRICING_API.basePriceLists}/l1`],
    ['activate a price list', (a) => a.setBasePriceListActive('l1', true), 'POST', `${PRICING_API.basePriceLists}/l1/activate`],
    ['set a contract formula', (a) => a.setFormula('t1', {} as never), 'PUT', `${PRICING_API.contractPricing}/t1`],
    ['negotiate a price', (a) => a.negotiatePrice('t1', 's1', {} as never), 'PUT', `${PRICING_API.contractPricing}/t1/prices/s1`],
    ['clear an exclusion', (a) => a.clearExclusion('t1', 's1'), 'DELETE', `${PRICING_API.contractPricing}/t1/prices/s1/exclusion`],
    ['set a price alert', (a) => a.setAlert('t1', 's1', null), 'PUT', `${PRICING_API.contractPricing}/t1/prices/s1/alert`],
    ['copy a contract', (a) => a.copyContractPricing('t1', 't2', null), 'POST', `${PRICING_API.contractPricing}/t1/copy-from/t2`],
  ];

  for (const [name, call, method, url] of cases) {
    it(`${name}: a 204 resolves undefined, so mutation() reports success`, async () => {
      const write = mutation(() => call(api));
      const outcome = write.execute(undefined);
      backend.expectOne({ method, url }).flush(null, { status: 204, statusText: 'No Content' });

      expect(await outcome).toBeUndefined();
      expect(write.problem()).toBeNull();
    });
  }
});
