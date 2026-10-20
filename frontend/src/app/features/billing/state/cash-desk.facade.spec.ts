import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { BILLING_API } from '../../../core/api/billing.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { CashDeskFacade } from './cash-desk.facade';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

describe('CashDeskFacade (UAT D-08, LIS-CR-004)', () => {
  let desk: CashDeskFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        {
          provide: AuthStore,
          useValue: {
            currentUser: () => ({ id: 'u-1' }),
            profile: () => ({ displayName: 'sara', displayNameEn: 'Sara Cashier', displayNameAr: 'سارة الكاشير' }),
          },
        },
        CashDeskFacade,
      ],
    });
    desk = TestBed.inject(CashDeskFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  async function tick(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  it('leaves a load in flight alone but re-reads the print parameter once the previous read settled', async () => {
    await tick();
    const first = backend.expectOne(BILLING_API.printSettings);

    desk.refresh();
    await tick();
    backend.expectNone(BILLING_API.printSettings);

    first.flush({ autoPrintReceipt: false });
    await tick();
    expect(desk.shouldAutoPrint()).toBe(false);

    desk.refresh();
    await tick();
    backend.expectOne(BILLING_API.printSettings).flush({ autoPrintReceipt: true });
    await tick();
    expect(desk.shouldAutoPrint()).toBe(true);
    backend.match(() => true).forEach((request) => request.flush([]));
  });

  it('reads no treasury and no device: the movement is posted for the signed-in cashier, named in the page language (LIS-CR-004)', async () => {
    await tick();
    backend.expectNone((request) => request.url.includes('treasur'));
    expect(desk.cashierName()).toBe('Sara Cashier');
    TestBed.inject(I18nService).setLanguage('ar');
    try {
      expect(desk.cashierName()).toBe('سارة الكاشير');
    } finally {
      TestBed.inject(I18nService).setLanguage('en');
    }

    backend.match(() => true).forEach((request) => request.flush([]));
    backend.verify();
  });

  it('reads autoPrintReceipt through billing (no settings.view needed) and prints only when it is on (UAT A-05)', async () => {
    await tick();
    expect(desk.shouldAutoPrint()).toBe(false);   // not known yet: no print
    backend.expectNone('/api/v1/settings');
    backend.expectOne(BILLING_API.printSettings).flush({ autoPrintReceipt: false });
    await tick();
    expect(desk.shouldAutoPrint()).toBe(false);

    desk.refresh();   // the page opens again: a parameter changed since is read again
    await tick();
    backend.expectOne(BILLING_API.printSettings).flush({ autoPrintReceipt: true });
    await tick();
    expect(desk.shouldAutoPrint()).toBe(true);

    desk.refresh();
    await tick();
    backend.expectOne(BILLING_API.printSettings).flush({ type: 'about:blank', title: 'Forbidden', status: 403, code: 'AUTHZ-001' }, { status: 403, statusText: 'Forbidden' });
    await tick();
    expect(desk.shouldAutoPrint()).toBe(false);   // refused: no print
  });

  it("reads the laboratory's payment methods for the movement forms, falls back to no options on a refusal and re-reads them on refresh (CP-30b)", async () => {
    await tick();
    expect(desk.methodOptions()).toEqual([]);
    backend.expectOne(BILLING_API.paymentMethodOptions).flush([{ id: 'pm-cash', code: 'CASH', nameEn: 'Cash', nameAr: 'نقدي', kind: 'Cash', requiresReference: false }]);
    await tick();
    expect(desk.methodOptions().map((option) => option.code)).toEqual(['CASH']);

    desk.refresh();
    await tick();
    backend.expectOne(BILLING_API.paymentMethodOptions).flush({ type: 'about:blank', title: 'Forbidden', status: 403, code: 'AUTH-403' }, { status: 403, statusText: 'Forbidden' });
    await tick();
    expect(desk.methodOptions()).toEqual([]);   // the forms run on the four kinds until a read succeeds

    backend.match(() => true).forEach((request) => request.flush([]));
    backend.verify();
  });
});
