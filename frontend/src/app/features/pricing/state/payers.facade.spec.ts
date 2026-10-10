import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { PRICING_API, type PayerListItem } from '../../../core/api/pricing.contracts';
import { PayersFacade } from './payers.facade';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const cash: PayerListItem = {
  id: 'p-cash',
  nameEn: 'Cash',
  nameAr: 'نقدي',
  searchKey: 'CASH',
  type: 'Cash',
  webPortalEnabled: false,
  webUsername: null,
  isActive: true,
  isDefault: true,
  tiers: [{ id: 't-default', payerId: 'p-cash', nameEn: 'Default', nameAr: 'افتراضي', isActive: true, isDefault: true }],
};

describe('PayersFacade (CP-32)', () => {
  let facade: PayersFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), PayersFacade] });
    facade = TestBed.inject(PayersFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  it('provisions the cash payer once, then pages the register with the filter', async () => {
    const first = facade.list({ search: 'ax', active: true }, { page: 2, pageSize: 20 });
    await settle();
    backend.expectOne({ method: 'POST', url: PRICING_API.defaultPayer }).flush({ payerId: 'p-cash', contractTierId: 't-default', created: true });
    await settle();
    const list = backend.expectOne((request) => request.url === PRICING_API.payers);
    expect(list.request.params.get('search')).toBe('ax');
    expect(list.request.params.get('isActive')).toBe('true');
    expect(list.request.params.get('page')).toBe('2');
    expect(list.request.params.get('pageSize')).toBe('20');
    expect(list.request.params.has('type')).toBe(false);
    list.flush({ items: [cash], page: 2, pageSize: 20, totalCount: 21, totalPages: 2 });
    expect((await first).items[0]?.isDefault).toBe(true);

    const second = facade.list({ search: '', active: null }, { page: 1, pageSize: 50 });
    await settle();
    backend.expectNone(PRICING_API.defaultPayer);
    const all = backend.expectOne((request) => request.url === PRICING_API.payers);
    expect(all.request.params.has('isActive')).toBe(false);
    all.flush({ items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 });
    await second;
  });

  it('still lists when provisioning is refused', async () => {
    const pending = facade.list({ search: '', active: true }, { page: 1, pageSize: 50 });
    await settle();
    backend.expectOne(PRICING_API.defaultPayer).flush({ code: 'AUTHZ-001' }, { status: 403, statusText: 'Forbidden' });
    await settle();
    backend.expectOne((request) => request.url === PRICING_API.payers).flush({ items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 });
    expect((await pending).totalCount).toBe(0);
  });

  it('opens the tiers drawer, adds a tier and reloads the payer', async () => {
    facade.openTiers('p-axa');
    await runEffects();
    backend.expectOne(`${PRICING_API.payers}/p-axa`).flush({ ...cash, id: 'p-axa', isDefault: false, tiers: [] });
    await runEffects();
    expect(facade.tiers()).toEqual([]);

    const added = facade.addTier.execute({ payerId: 'p-axa', request: { nameEn: 'Gold', nameAr: 'ذهبي' } });
    await settle();
    const post = backend.expectOne({ method: 'POST', url: `${PRICING_API.payers}/p-axa/tiers` });
    expect(post.request.body).toEqual({ nameEn: 'Gold', nameAr: 'ذهبي' });
    post.flush({ id: 't-gold' }, { status: 201, statusText: 'Created' });
    expect(await added).toEqual({ id: 't-gold' });
    expect(facade.changes()).toBe(1);
    await runEffects();

    backend.expectOne(`${PRICING_API.payers}/p-axa`).flush({ ...cash, id: 'p-axa', isDefault: false, tiers: [{ id: 't-gold', payerId: 'p-axa', nameEn: 'Gold', nameAr: 'ذهبي', isActive: true, isDefault: false }] });
    await runEffects();
    expect(facade.tiers().map((tier) => tier.nameEn)).toEqual(['Gold']);

    facade.closeTiers();
    expect(facade.tiersPayerId()).toBeNull();
  });

  it('deactivates a tier through its endpoint and keeps the refusal as a problem', async () => {
    const pending = facade.setTierActive.execute({ payerId: 'p-cash', tierId: 't-default', active: false });
    await settle();
    backend
      .expectOne({ method: 'POST', url: `${PRICING_API.payers}/p-cash/tiers/t-default/deactivate` })
      .flush({ type: 'about:blank', title: 'locked', status: 409, code: 'PRC-021' }, { status: 409, statusText: 'Conflict', headers: { 'content-type': 'application/problem+json' } });
    expect(await pending).toBeNull();
    expect(facade.setTierActive.problem()).not.toBeNull();
  });
});
