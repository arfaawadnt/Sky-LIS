import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { PRICING_API } from '../../../core/api/pricing.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { SERVICE_PRICES } from '../testing/pricing.fixtures';
import { ServicePricesFacade } from './service-prices.facade';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

describe('ServicePricesFacade (CP-33b, C22)', () => {
  let facade: ServicePricesFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(withInterceptors([problemDetailsInterceptor])), provideHttpClientTesting(), ServicePricesFacade] });
    facade = TestBed.inject(ServicePricesFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  it('reads nothing until a service is chosen, then every price of that service', async () => {
    await runEffects();
    backend.expectNone((request) => request.url.startsWith(PRICING_API.servicePrices));
    expect(facade.current()).toBeNull();

    facade.load('s-cbc');
    await runEffects();
    backend.expectOne(`${PRICING_API.servicePrices}/s-cbc`).flush(SERVICE_PRICES);
    await runEffects();

    expect(facade.current()?.code).toBe('CBC');
    expect(facade.current()?.basePrices.map((price) => price.listCode)).toEqual(['PL26', 'PL25']);
    expect(facade.current()?.contractPrices.map((price) => price.tierId)).toEqual(['t-gold']);
    backend.verify();
  });

  it('re-reads the same service on a second load (the facade outlives the page) and clears on null', async () => {
    facade.load('s-cbc');
    await runEffects();
    backend.expectOne(`${PRICING_API.servicePrices}/s-cbc`).flush(SERVICE_PRICES);
    await runEffects();

    facade.load('s-cbc');
    await runEffects();
    backend.expectOne(`${PRICING_API.servicePrices}/s-cbc`).flush(SERVICE_PRICES);
    await runEffects();

    facade.load(null);
    await runEffects();
    expect(facade.serviceId()).toBeNull();
    expect(facade.current()).toBeNull();
    backend.verify();
  });

  it('saves one base price through the list lines endpoint and re-reads the service', async () => {
    facade.load('s-cbc');
    await runEffects();
    backend.expectOne(`${PRICING_API.servicePrices}/s-cbc`).flush(SERVICE_PRICES);
    await runEffects();

    const saving = facade.setBasePrice.execute({ listId: 'pl26', serviceId: 's-cbc', price: 195.5, isActive: true });
    await settle();
    const put = backend.expectOne({ method: 'PUT', url: `${PRICING_API.basePriceLists}/pl26/lines` });
    expect(put.request.body).toEqual({ lines: [{ serviceId: 's-cbc', price: 195.5, isActive: true }] });
    put.flush({ added: 0, changed: 1 });
    expect(await saving).toEqual({ added: 0, changed: 1 });
    await runEffects();
    backend.expectOne(`${PRICING_API.servicePrices}/s-cbc`).flush({ ...SERVICE_PRICES, basePrices: [{ ...SERVICE_PRICES.basePrices[0]!, price: 195.5 }, SERVICE_PRICES.basePrices[1]!] });
    await runEffects();

    expect(facade.current()?.basePrices[0]?.price).toBe(195.5);
    backend.verify();
  });

  it('keeps a refusal of the save as the mutation problem and re-reads nothing', async () => {
    facade.load('s-cbc');
    await runEffects();
    backend.expectOne(`${PRICING_API.servicePrices}/s-cbc`).flush(SERVICE_PRICES);
    await runEffects();

    const saving = facade.setBasePrice.execute({ listId: 'pl26', serviceId: 's-cbc', price: -1, isActive: true });
    await settle();
    backend.expectOne({ method: 'PUT', url: `${PRICING_API.basePriceLists}/pl26/lines` }).flush(
      { type: 'about:blank', title: 'Refused', status: 422, code: 'PRC-006', traceId: null, correlationId: null, errors: null },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    expect(await saving).toBeNull();
    await runEffects();

    expect(facade.setBasePrice.problem()?.code).toBe('PRC-006');
    backend.verify();
  });
});
