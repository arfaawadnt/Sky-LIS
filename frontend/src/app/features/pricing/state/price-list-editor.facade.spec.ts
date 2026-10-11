import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { PRICING_API } from '../../../core/api/pricing.contracts';
import { LIST_DETAIL, LIST_LINES } from '../testing/pricing.fixtures';
import { PriceListEditorFacade } from './price-list-editor.facade';
import { ServiceNamesStore } from './service-names.store';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

describe('PriceListEditorFacade (CP-33)', () => {
  let facade: PriceListEditorFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), PriceListEditorFacade, ServiceNamesStore] });
    facade = TestBed.inject(PriceListEditorFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  async function loadList(): Promise<void> {
    facade.load('pl26');
    await runEffects();
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26`).flush(LIST_DETAIL);
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26/lines`).flush(LIST_LINES);
    await runEffects();
  }

  it('loads the list, its lines and the service names', async () => {
    await loadList();

    expect(facade.rows().map((row) => [row.serviceId, row.price, row.dirty])).toEqual([
      ['s-cbc', 180, false],
      ['s-lip', 300, false],
    ]);
    expect(facade.names.code('s-cbc')).toBe('CBC');
    facade.search.set('lipid');
    expect(facade.visibleRows().map((row) => row.serviceId)).toEqual(['s-lip']);
    backend.verify();
  });

  it('tracks edits, flags invalid prices, adds services and saves only the changed lines', async () => {
    await loadList();

    facade.setPrice('s-cbc', 190);
    facade.setLineActive('s-lip', false);
    facade.setPrice('s-lip', 300);
    expect(facade.dirtyRows().map((row) => row.serviceId)).toEqual(['s-cbc', 's-lip']);

    facade.setPrice('s-cbc', 180);   // back to the stored value: no longer dirty
    expect(facade.dirtyRows().map((row) => row.serviceId)).toEqual(['s-lip']);

    expect(facade.addService({ id: 's-tsh', code: 'TSH', nameEn: 'TSH', nameAr: 'هرمون' })).toBe(true);
    expect(facade.addService({ id: 's-cbc', code: 'CBC', nameEn: 'CBC', nameAr: 'صورة دم' })).toBe(false);
    expect(facade.rows()[0]).toMatchObject({ serviceId: 's-tsh', isNew: true, dirty: true, price: 0 });

    facade.setPrice('s-tsh', 90.555);
    expect(facade.invalidCount()).toBe(1);
    facade.setPrice('s-tsh', 90.5);
    expect(facade.invalidCount()).toBe(0);

    const saving = facade.save.execute({ listId: 'pl26', lines: facade.pendingLines() });
    await settle();
    const put = backend.expectOne({ method: 'PUT', url: `${PRICING_API.basePriceLists}/pl26/lines` });
    expect(put.request.body).toEqual({
      lines: [
        { serviceId: 's-tsh', price: 90.5, isActive: true },
        { serviceId: 's-lip', price: 300, isActive: false },
      ],
    });
    put.flush({ added: 1, changed: 1 });
    expect(await saving).toEqual({ added: 1, changed: 1 });
    expect(facade.dirtyRows()).toEqual([]);
    await runEffects();

    backend.expectOne(`${PRICING_API.basePriceLists}/pl26`).flush(LIST_DETAIL);
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26/lines`).flush([...LIST_LINES, { id: 'l3', serviceId: 's-tsh', price: 90.5, isActive: true, serviceCode: 'TSH', serviceNameEn: 'TSH', serviceNameAr: 'هرمون' }]);
    await runEffects();
    backend.verify();
  });

  it('applies the list to the derived contracts and reloads the header', async () => {
    await loadList();

    const applying = facade.apply.execute('pl26');
    await settle();
    backend.expectOne({ method: 'POST', url: `${PRICING_API.basePriceLists}/pl26/apply` }).flush({ contractsApplied: 2, pricesChanged: 14 });
    expect(await applying).toEqual({ contractsApplied: 2, pricesChanged: 14 });
    await runEffects();
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26`).flush(LIST_DETAIL);
    backend.verify();
  });

  it('re-reads the same list when a page opens it again, dropping the edits of the previous visit (UAT D-14)', async () => {
    await loadList();
    facade.setPrice('s-cbc', 190);

    facade.load('pl26');
    await runEffects();
    expect(facade.dirtyRows()).toEqual([]);
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26`).flush(LIST_DETAIL);
    backend.expectOne(`${PRICING_API.basePriceLists}/pl26/lines`).flush(LIST_LINES);
    backend.verify();
  });
});
