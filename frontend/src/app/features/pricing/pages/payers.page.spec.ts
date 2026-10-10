import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { PRICING_API, type PayerListItem } from '../../../core/api/pricing.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { PayersFacade } from '../state/payers.facade';
import { nextCode } from '../ui/copy-list-dialog.component';
import { PayersPage, toPayerRequest } from './payers.page';
import { toListRequest } from './base-price-lists.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const axa: PayerListItem = {
  id: 'p-axa',
  nameEn: 'AXA Insurance',
  nameAr: 'أكسا للتأمين',
  searchKey: 'AXA',
  type: 'Insurance',
  webPortalEnabled: true,
  webUsername: 'axa.portal',
  isActive: true,
  isDefault: false,
  tiers: [{ id: 't-gold', payerId: 'p-axa', nameEn: 'Gold', nameAr: 'ذهبي', isActive: true, isDefault: false }],
};

const cash: PayerListItem = {
  ...axa,
  id: 'p-cash',
  nameEn: 'Cash',
  nameAr: 'نقدي',
  searchKey: 'CASH',
  type: 'Cash',
  webPortalEnabled: false,
  webUsername: null,
  isDefault: true,
  tiers: [{ id: 't-default', payerId: 'p-cash', nameEn: 'Default', nameAr: 'افتراضي', isActive: true, isDefault: true }],
};

describe('PayersPage (CP-32)', () => {
  let backend: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PayersPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        PayersFacade,
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

  it('provisions the cash payer, lists the payers with their tiers and opens the tiers drawer', async () => {
    const fixture = TestBed.createComponent(PayersPage);
    await flush();
    backend.expectOne({ method: 'POST', url: PRICING_API.defaultPayer }).flush({ payerId: 'p-cash', contractTierId: 't-default', created: true });
    await flush();
    backend.expectOne((request) => request.url === PRICING_API.payers).flush({ items: [cash, axa], page: 1, pageSize: 50, totalCount: 2, totalPages: 1 });
    await flush();

    const host = fixture.nativeElement as HTMLElement;
    const rows = host.querySelectorAll('tbody tr[data-row]');
    expect(rows.length).toBe(2);
    expect(rows[0]?.textContent).toContain('pricing.payers.default');
    expect(rows[1]?.textContent).toContain('Gold');

    const tiersButton = rows[1]?.querySelector<HTMLButtonElement>('button[aria-label="pricing.payers.action.tiers"]');
    tiersButton?.click();
    await flush();
    backend.expectOne(`${PRICING_API.payers}/p-axa`).flush(axa);
    await flush();

    const drawer = host.querySelector<HTMLElement>('app-drawer [role="dialog"]');
    expect(drawer?.textContent).toContain('Gold');
    backend.verify();
  });

  it('maps the drawer form to the request: keys trimmed, username dropped while the portal is off', () => {
    expect(toPayerRequest({ name: { en: ' AXA ', ar: ' أكسا ' }, searchKey: ' axa ', type: 'Insurance', webPortalEnabled: false, webUsername: 'left.over' })).toEqual({
      nameEn: 'AXA',
      nameAr: 'أكسا',
      searchKey: 'axa',
      type: 'Insurance',
      webPortalEnabled: false,
      webUsername: null,
    });
    expect(toPayerRequest({ name: { en: 'X', ar: 'س' }, searchKey: 'X', type: 'nonsense', webPortalEnabled: true, webUsername: ' x.portal ' })).toMatchObject({ type: 'Credit', webUsername: 'x.portal' });
    expect(toListRequest({ code: ' pl27 ', name: { en: 'Main 2027', ar: 'رئيسية 2027' }, year: '2027' })).toEqual({ code: 'pl27', nameEn: 'Main 2027', nameAr: 'رئيسية 2027', year: 2027 });
  });

  it('proposes next year\'s code for a copy', () => {
    expect(nextCode('PL26', 2026, 2027)).toBe('PL27');
    expect(nextCode('MAIN-2026', 2026, 2027)).toBe('MAIN-2027');
    expect(nextCode('INSTITUTIONAL', 2026, 2027)).toBe('INSTITUTION-COPY');
  });
});
