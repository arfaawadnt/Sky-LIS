import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { TREASURY_API, type Paged, type TreasuryAccessRequestListItem } from '../../../core/api/treasury.contracts';
import { DeviceIdentityService } from '../../../core/device/device-identity.service';
import { TreasuryAccessFacade } from './treasury-access.facade';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function page(items: TreasuryAccessRequestListItem[]): Paged<TreasuryAccessRequestListItem> {
  return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 };
}

const noTreasuries = { items: [], page: 1, pageSize: 200, totalCount: 0, totalPages: 0 };

describe('TreasuryAccessFacade (01 §6.3)', () => {
  let facade: TreasuryAccessFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: DeviceIdentityService, useValue: { deviceId: () => 'device-test' } satisfies Pick<DeviceIdentityService, 'deviceId'> },
        TreasuryAccessFacade,
      ],
    });
    facade = TestBed.inject(TreasuryAccessFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  /** Runs the resource effects (which issue the HTTP calls) and lets the promises settle; `whenStable()` would wait for the very requests the test flushes. */
  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  it('loads the requests for the current filter and reloads after a request', async () => {
    await runEffects();
    const first = backend.expectOne((request) => request.url === TREASURY_API.requests);
    expect(first.request.params.get('mine')).toBe('false');
    expect(first.request.params.has('status')).toBe(false);
    first.flush(page([]));
    backend.expectOne((request) => request.url === TREASURY_API.treasuries).flush(noTreasuries);
    await runEffects();
    expect(facade.requests.value()?.items.length).toBe(0);

    const pending = facade.request.execute({ treasuryId: 'trs-1', deviceLabel: 'Reception PC' });
    await settle();
    const post = backend.expectOne(TREASURY_API.requests);
    expect(post.request.body).toEqual({ treasuryId: 'trs-1', deviceId: 'device-test', deviceLabel: 'Reception PC' });
    post.flush({ id: 'req-1' }, { status: 201, statusText: 'Created' });
    expect(await pending).toEqual({ id: 'req-1' });
    await runEffects();

    backend.expectOne((request) => request.url === TREASURY_API.requests).flush(page([]));
    backend.verify();
  });

  it('narrows the list to a status and to my own requests', async () => {
    await runEffects();
    backend.expectOne((request) => request.url === TREASURY_API.requests).flush(page([]));
    backend.expectOne((request) => request.url === TREASURY_API.treasuries).flush(noTreasuries);

    facade.setStatus('Pending');
    facade.setMine(true);
    await runEffects();

    const filtered = backend.expectOne((request) => request.url === TREASURY_API.requests);
    expect(filtered.request.params.get('status')).toBe('Pending');
    expect(filtered.request.params.get('mine')).toBe('true');
    filtered.flush(page([]));
    backend.verify();
  });

  it('re-reads the treasury picker on every visit, so a reactivated treasury is offered (UAT B-07)', async () => {
    await runEffects();
    backend.expectOne((request) => request.url === TREASURY_API.requests).flush(page([]));
    backend.expectOne((request) => request.url === TREASURY_API.treasuries).flush(noTreasuries);
    await runEffects();

    facade.refresh();
    await runEffects();
    backend.expectOne((request) => request.url === TREASURY_API.requests).flush(page([]));
    const picker = backend.expectOne((request) => request.url === TREASURY_API.treasuries);
    expect(picker.request.params.get('isActive')).toBe('true');
    picker.flush({ ...noTreasuries, items: [{ id: 'trs-9', code: 'NORTH-CASH', nameEn: 'North cash', nameAr: 'خزينة الشمال', branchId: 'b1', isActive: true, branchCode: 'NORTH', branchNameEn: 'North', branchNameAr: 'الشمال' }], totalCount: 1 });
    await runEffects();
    expect(facade.treasuries.value()?.items.map((item) => item.code)).toEqual(['NORTH-CASH']);
    backend.verify();
  });
});
