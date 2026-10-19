import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { PRICING_API } from '../../../core/api/pricing.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ServicePricesFacade } from '../state/service-prices.facade';
import { SERVICE_PRICES } from '../testing/pricing.fixtures';
import { ServicePricesPage } from './service-prices.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

describe('ServicePricesPage (CP-33b, C22)', () => {
  let backend: HttpTestingController;
  let canEdit = true;

  beforeEach(async () => {
    canEdit = true;
    await TestBed.configureTestingModule({
      imports: [ServicePricesPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        ServicePricesFacade,
        { provide: PermissionsService, useValue: { can: () => canEdit } satisfies Pick<PermissionsService, 'can'> },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function flush(): Promise<void> {
    TestBed.tick();
    await settle();
    TestBed.tick();
    await settle();
  }

  async function render(serviceId: string | undefined): Promise<{ host: HTMLElement; page: ServicePricesPage }> {
    const fixture = TestBed.createComponent(ServicePricesPage);
    fixture.componentRef.setInput('serviceId', serviceId);
    await flush();
    if (serviceId !== undefined) {
      backend.expectOne(`${PRICING_API.servicePrices}/${serviceId}`).flush(SERVICE_PRICES);
      await flush();
    }

    return { host: fixture.nativeElement as HTMLElement, page: fixture.componentInstance };
  }

  it('starts with the picker and an empty state, and reads nothing', async () => {
    const { host } = await render(undefined);

    expect(host.querySelector('app-service-picker')).not.toBeNull();
    expect(host.querySelector('app-empty-state')?.textContent).toContain('pricing.servicePrices.emptyTitle');
    backend.verify();
  });

  it('shows the chosen service with its base lists (linked to their editors) and its contract prices', async () => {
    const { host } = await render('s-cbc');

    expect(host.querySelector('.chosen')?.textContent).toContain('CBC');
    const rows = [...host.querySelectorAll('tbody tr[data-row]')];
    expect(rows.length).toBe(3);
    expect(host.querySelector('a[href="/pricing/base-price-lists/pl26"]')?.textContent?.trim()).toBe('PL26');
    expect(rows[1]?.textContent).toContain('pricing.servicePrices.lineInactive');
    expect(rows[2]?.textContent).toContain('AXA');
    expect(rows[2]?.textContent).toContain('PL26');
    // contract pricing (CP-34) is Release 2: the tier is named, not linked (UAT R1-03)
    expect(host.querySelector('a[href*="/pricing/contract-pricing"]')).toBeNull();
    backend.verify();
  });

  it('puts the picked service into the query string, so the page can be linked and refreshed', async () => {
    const { page } = await render(undefined);
    const router = TestBed.inject(Router);

    (page as unknown as { onPicked(service: { id: string; code: string; nameEn: string; nameAr: string }): void }).onPicked({ id: 's-cbc', code: 'CBC', nameEn: 'CBC', nameAr: 'س' });
    await flush();

    expect(router.url).toContain('serviceId=s-cbc');
  });

  it('saves an edited base price with Enter through the one-line upsert and re-reads the service', async () => {
    const { host } = await render('s-cbc');

    const price = host.querySelector<HTMLInputElement>('tbody tr[data-row] input.price');
    if (!price) {
      throw new Error('price input missing');
    }

    price.value = '195.5';
    price.dispatchEvent(new Event('input'));
    await flush();
    expect(host.querySelector('tbody tr[data-row] button[aria-label="pricing.servicePrices.savePrice"]')).not.toBeNull();

    price.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    await flush();
    const put = backend.expectOne({ method: 'PUT', url: `${PRICING_API.basePriceLists}/pl26/lines` });
    expect(put.request.body).toEqual({ lines: [{ serviceId: 's-cbc', price: 195.5, isActive: true }] });
    put.flush({ added: 0, changed: 1 });
    await flush();
    backend.expectOne(`${PRICING_API.servicePrices}/s-cbc`).flush({ ...SERVICE_PRICES, basePrices: [{ ...SERVICE_PRICES.basePrices[0]!, price: 195.5 }, SERVICE_PRICES.basePrices[1]!] });
    await flush();

    expect(host.querySelector<HTMLInputElement>('tbody tr[data-row] input.price')?.value).toBe('195.5');
    expect(host.querySelector('tbody tr[data-row] button[aria-label="pricing.servicePrices.savePrice"]')).toBeNull();
    backend.verify();
  });

  it('never sends an invalid price, and Escape discards the draft', async () => {
    const { host } = await render('s-cbc');
    const price = host.querySelector<HTMLInputElement>('tbody tr[data-row] input.price');
    if (!price) {
      throw new Error('price input missing');
    }

    price.value = '-1';
    price.dispatchEvent(new Event('input'));
    await flush();
    price.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    await flush();
    backend.expectNone({ method: 'PUT', url: `${PRICING_API.basePriceLists}/pl26/lines` });
    expect(price.classList.contains('invalid')).toBe(true);

    price.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await flush();
    expect(host.querySelector<HTMLInputElement>('tbody tr[data-row] input.price')?.value).toBe('180');
    backend.verify();
  });

  it('shows the prices read-only without pricing.edit (affordance; the server decides)', async () => {
    canEdit = false;
    const { host } = await render('s-cbc');

    expect(host.querySelector('tbody tr[data-row] input.price')).toBeNull();
    expect(host.querySelector('tbody tr[data-row]')?.textContent).toContain('180');
    backend.verify();
  });
});
