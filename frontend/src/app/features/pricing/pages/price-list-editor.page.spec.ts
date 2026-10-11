import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { PRICING_API } from '../../../core/api/pricing.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { PriceListEditorFacade } from '../state/price-list-editor.facade';
import { ServiceNamesStore } from '../state/service-names.store';
import { LIST_DETAIL, LIST_LINES } from '../testing/pricing.fixtures';
import { PriceListEditorPage } from './price-list-editor.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

describe('PriceListEditorPage (CP-33)', () => {
  let backend: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PriceListEditorPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        PriceListEditorFacade,
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

  async function render(): Promise<{ host: HTMLElement; page: PriceListEditorPage }> {
    const fixture = TestBed.createComponent(PriceListEditorPage);
    fixture.componentRef.setInput('listId', 'pl26');
    await flush();
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26`).flush(LIST_DETAIL);
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26/lines`).flush(LIST_LINES);
    await flush();
    return { host: fixture.nativeElement as HTMLElement, page: fixture.componentInstance };
  }

  it('shows the list summary and an editable price per service', async () => {
    const { host } = await render();

    expect(host.querySelector('app-page-header')?.textContent).toContain('PL26');
    const rows = host.querySelectorAll('tbody tr[data-row]');
    expect(rows.length).toBe(2);
    expect(rows[0]?.textContent).toContain('CBC');
    expect(host.querySelector<HTMLInputElement>('tbody tr[data-row] input.price')?.value).toBe('180');
    backend.verify();
  });

  it('names the derived contracts without linking to the Release-2 contract pricing page (UAT R1-03)', async () => {
    const fixture = TestBed.createComponent(PriceListEditorPage);
    fixture.componentRef.setInput('listId', 'pl26');
    await flush();
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26`).flush({
      ...LIST_DETAIL,
      derivedContracts: [{ contractPricingId: 'cp1', payerId: 'p-axa', contractTierId: 't-gold', payerNameEn: 'AXA', payerNameAr: 'أكسا', tierNameEn: 'Gold', tierNameAr: 'ذهبي', appliedAtUtc: null }],
    });
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26/lines`).flush(LIST_LINES);
    await flush();

    const host = fixture.nativeElement as HTMLElement;
    const chip = [...host.querySelectorAll('.chips .chip')].find((candidate) => candidate.textContent?.includes('AXA'));
    expect(chip?.textContent).toContain('Gold');
    expect(chip?.tagName).toBe('SPAN');
    expect(host.querySelector('a[href*="/pricing/contract-pricing"]')).toBeNull();
  });

  it('adds a typed service from the catalogue picker with Enter (SRS keyboard rule)', async () => {
    const { host } = await render();

    const picker = host.querySelector<HTMLInputElement>('app-service-picker input[role="combobox"]');
    if (!picker) {
      throw new Error('picker missing');
    }

    picker.value = 'tsh';
    picker.dispatchEvent(new Event('input'));
    await new Promise((resolve) => setTimeout(resolve, 300));
    const search = backend.expectOne((request) => request.url === PRICING_API.catalogueServices);
    expect(search.request.params.get('search')).toBe('tsh');
    expect(search.request.params.get('pageSize')).toBe('20');
    search.flush({ items: [{ id: 's-tsh', code: 'TSH', shortName: 'TSH', nameEn: 'Thyroid stimulating hormone', nameAr: 'هرمون الغدة' }], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 });
    await flush();

    picker.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    await flush();

    const first = host.querySelector('tbody tr[data-row]');
    expect(first?.textContent).toContain('TSH');
    expect(first?.textContent).toContain('pricing.editor.new');
    expect(host.textContent).toContain('pricing.editor.unsaved');
    backend.verify();
  });

  it('marks a typed price as changed and saves it with Ctrl+S; leaving with changes asks first', async () => {
    const { host, page } = await render();

    const price = host.querySelector<HTMLInputElement>('tbody tr[data-row] input.price');
    if (!price) {
      throw new Error('price input missing');
    }

    price.value = '195.5';
    price.dispatchEvent(new Event('input'));
    await flush();
    expect(host.textContent).toContain('pricing.editor.unsaved');
    expect(host.textContent).toContain('pricing.editor.changed');

    const confirm = TestBed.inject(ConfirmService);
    const leaving = page.canLeave();
    await flush();
    expect(confirm.current()?.kind).toBe('confirm');
    confirm.resolve(false);
    expect(await leaving).toBe(false);

    price.focus();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 's', ctrlKey: true, bubbles: true }));
    await flush();
    const put = backend.expectOne({ method: 'PUT', url: `${PRICING_API.basePriceLists}/pl26/lines` });
    expect(put.request.body).toEqual({ lines: [{ serviceId: 's-cbc', price: 195.5, isActive: true }] });
    put.flush({ added: 0, changed: 1 });
    await flush();
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26`).flush(LIST_DETAIL);
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26/lines`).flush([{ ...LIST_LINES[0], price: 195.5 }, LIST_LINES[1]]);
    await flush();

    expect(host.textContent).not.toContain('pricing.editor.unsaved');
    expect(await page.canLeave()).toBe(true);
    backend.verify();
  });
});
