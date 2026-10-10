import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { BILLING_API } from '../../../core/api/billing.contracts';
import { DeviceIdentityService } from '../../../core/device/device-identity.service';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { CashDeskFacade } from './cash-desk.facade';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const main = { id: 't-1', code: 'MAIN', nameEn: 'Main drawer', nameAr: 'الخزينة الرئيسية', branchId: 'b-1', isActive: true };

describe('CashDeskFacade (BR-27, UAT D-08)', () => {
  let desk: CashDeskFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: DeviceIdentityService, useValue: { deviceId: () => 'device-test' } satisfies Pick<DeviceIdentityService, 'deviceId'> },
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

  it('leaves a load in flight alone but re-reads the approved treasuries once the previous read settled', async () => {
    await tick();
    const first = backend.expectOne((request) => request.url === BILLING_API.myTreasuries);

    desk.refresh();
    await tick();
    backend.expectNone((request) => request.url === BILLING_API.myTreasuries);

    first.flush([]);
    await tick();
    expect(desk.hasStation()).toBe(false);

    desk.refresh();
    await tick();
    backend.expectOne((request) => request.url === BILLING_API.myTreasuries).flush([main]);
    await tick();
    expect(desk.hasStation()).toBe(true);
    expect(desk.treasuryId()).toBe('t-1');
  });

  it('re-reads after a refused read, so a right granted since (BR-17) is not hidden behind the old refusal', async () => {
    await tick();
    backend.expectOne((request) => request.url === BILLING_API.myTreasuries).flush({ type: 'about:blank', title: 'Forbidden', status: 403, code: 'AUTHZ-001' }, { status: 403, statusText: 'Forbidden' });
    await tick();
    expect(desk.approved()).toEqual([]);

    desk.refresh();
    await tick();
    backend.expectOne((request) => request.url === BILLING_API.myTreasuries).flush([main]);
    await tick();
    expect(desk.approved().map((item) => item.code)).toEqual(['MAIN']);
  });
});
