import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { PRICING_API } from '../../../core/api/pricing.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { ContractPricingFacade } from '../state/contract-pricing.facade';
import { GOLD, LISTS_PAGE, TIER_OPTIONS } from '../testing/pricing.fixtures';
import { ServiceNamesStore } from '../state/service-names.store';
import { ContractPricingPage } from './contract-pricing.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

describe('ContractPricingPage (CP-34)', () => {
  let backend: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ContractPricingPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        ContractPricingFacade,
        ServiceNamesStore,
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
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

  async function render(): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(ContractPricingPage);
    fixture.componentRef.setInput('tierId', 't-gold');
    await flush();
    backend.expectOne((request) => request.url === PRICING_API.tierOptions).flush(TIER_OPTIONS);
    backend.expectOne((request) => request.url === PRICING_API.basePriceLists).flush(LISTS_PAGE);
    backend.expectOne(`${PRICING_API.contractPricing}/t-gold`).flush(GOLD);
    await flush();
    return fixture.nativeElement as HTMLElement;
  }

  function select(host: HTMLElement, index: number): HTMLSelectElement {
    const element = host.querySelectorAll<HTMLSelectElement>('select[formcontrolname="baseListId"]')[index];
    if (!element) {
      throw new Error(`formula select ${index} missing`);
    }

    return element;
  }

  it('shows the tier formula, the derived prices with their names and the exclusion', async () => {
    const host = await render();

    expect(select(host, 0).value).toBe('pl26');
    expect(select(host, 1).value).toBe('');
    expect(host.querySelector<HTMLInputElement>('input[formcontrolname="patientFactor"]')?.value).toBe('0.1');
    expect(host.querySelector<HTMLInputElement>('input[type="date"][formcontrolname="effectiveFrom"]')?.value).toBe('2026-07-01');
    const rows = host.querySelectorAll('tbody tr[data-row]');
    expect(rows.length).toBe(2);
    expect(rows[0]?.textContent).toContain('CBC');
    expect(rows[1]?.textContent).toContain('pricing.grid.excluded');
    backend.verify();
  });

  it('refuses a list chosen twice and saves a corrected formula with Ctrl+S', async () => {
    const host = await render();

    const second = select(host, 1);
    second.value = 'pl26';
    second.dispatchEvent(new Event('change'));
    await flush();
    expect(host.textContent).toContain('pricing.contract.formulaError.formulaDuplicate');

    second.value = 'inst';
    second.dispatchEvent(new Event('change'));
    const payer = host.querySelectorAll<HTMLInputElement>('input[formcontrolname="payerFactor"]')[1];
    if (!payer) {
      throw new Error('payer factor missing');
    }

    payer.value = '1';
    payer.dispatchEvent(new Event('input'));
    await flush();
    expect(host.textContent).not.toContain('pricing.contract.formulaError.formulaDuplicate');

    payer.focus();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 's', ctrlKey: true, bubbles: true }));
    await flush();

    const put = backend.expectOne({ method: 'PUT', url: `${PRICING_API.contractPricing}/t-gold` });
    expect(put.request.body).toEqual({
      effectiveFrom: '2026-07-01',
      effectiveTo: '2027-06-30',
      terms: [
        { baseListId: 'pl26', patientFactor: 0.1, payerFactor: 0.9 },
        { baseListId: 'inst', patientFactor: 1, payerFactor: 1 },
      ],
    });
    put.flush(null, { status: 204, statusText: 'No Content' });
    await flush();
    backend.expectOne(`${PRICING_API.contractPricing}/t-gold`).flush(GOLD);
    backend.expectOne((request) => request.url === PRICING_API.tierOptions).flush(TIER_OPTIONS);
    await flush();
    backend.verify();
  });

  it('applies after confirmation', async () => {
    const host = await render();
    const confirm = TestBed.inject(ConfirmService);

    const apply = [...host.querySelectorAll<HTMLButtonElement>('app-page-header button')].find((button) => button.textContent?.includes('pricing.contract.apply'));
    apply?.click();
    await flush();
    expect(confirm.current()?.kind).toBe('confirm');
    confirm.resolve(true);
    await flush();

    backend.expectOne({ method: 'POST', url: `${PRICING_API.contractPricing}/t-gold/apply` }).flush({ priced: 2, added: 0, updated: 1, removed: 0, excludedKept: 1 });
    await flush();
    backend.expectOne(`${PRICING_API.contractPricing}/t-gold`).flush(GOLD);
    await flush();
    backend.verify();
  });
});
