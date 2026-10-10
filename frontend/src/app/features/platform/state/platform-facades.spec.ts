import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { type AdminSessionItem, type ClientListItem, type Paged, PLATFORM_API, type WalletDetail } from '../../../core/api/platform.contracts';
import { ClientsFacade } from './clients.facade';
import { SessionsFacade } from './sessions.facade';
import { WalletsFacade } from './wallets.facade';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function page<T>(items: T[]): Paged<T> {
  return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 };
}

const client: ClientListItem = {
  id: 't1',
  nameEn: 'Al Noor Lab',
  nameAr: 'معمل النور',
  portalSlug: 'al-noor',
  portalUrl: 'https://al-noor.skylis.app',
  countryId: 'eg',
  countryNameEn: 'Egypt',
  countryNameAr: 'مصر',
  governorateId: 'cairo',
  governorateNameEn: 'Cairo',
  governorateNameAr: 'القاهرة',
  managerName: 'Dr. Mona',
  managerMobile: '+201112223333',
  planId: 'p1',
  planCode: 'STARTER',
  planNameEn: 'Starter',
  planNameAr: 'الأساسية',
  walletBalance: 0,
  walletCurrency: 'EGP',
  branches: 1,
  users: 2,
  createdAtUtc: '2026-10-09T09:00:00Z',
  source: 'Manual',
  status: 'Active',
  trialEndsAtUtc: null,
};

const wallet: WalletDetail = {
  walletId: 'w1',
  tenantId: 't1',
  clientNameEn: 'Al Noor Lab',
  clientNameAr: 'معمل النور',
  balance: 0,
  currencyCode: 'EGP',
  nextChargeAtUtc: '2026-11-09T09:00:00Z',
  planId: 'p1',
  planCode: 'STARTER',
  planNameEn: 'Starter',
  planNameAr: 'الأساسية',
  monthlyFee: 1500,
  planCurrencyCode: 'EGP',
  smsQuota: 500,
  smsUsedThisMonth: 0,
  pendingTopUpRequests: [{ id: 'r1', amount: 750, note: 'October', status: 'Pending', requestedAtUtc: '2026-10-09T10:00:00Z', requestedByUserId: 'u1', requestedByName: 'Lab admin', decidedAtUtc: null }],
};

describe('Platform administration facades (01 §6.3)', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), ClientsFacade, WalletsFacade, SessionsFacade],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  it('lists clients with only the filters that are set and provisions with the administrator credentials (SA-04)', async () => {
    const facade = TestBed.inject(ClientsFacade);
    await runEffects();

    const first = backend.expectOne((request) => request.url === PLATFORM_API.clients);
    expect(first.request.params.keys().sort()).toEqual(['page', 'pageSize']);
    first.flush(page([client]));
    backend.expectOne((request) => request.url === PLATFORM_API.plans).flush(page([]));
    backend.expectOne((request) => request.url === PLATFORM_API.countries).flush(page([]));
    await runEffects();
    expect(facade.rows().map((row) => row.id)).toEqual(['t1']);

    facade.setSource('Subscription');
    facade.setStatus('Trial');
    facade.setSearch('noor');
    await runEffects();
    const filtered = backend.expectOne((request) => request.url === PLATFORM_API.clients);
    expect([filtered.request.params.get('source'), filtered.request.params.get('status'), filtered.request.params.get('search'), filtered.request.params.get('page')]).toEqual(['Subscription', 'Trial', 'noor', '1']);
    filtered.flush(page([]));

    const pending = facade.provision.execute({
      nameEn: 'New Lab',
      nameAr: 'معمل جديد',
      countryId: 'eg',
      governorateId: 'cairo',
      address: 'Street 1',
      mobile: '01012345678',
      licenceNo: null,
      managerName: 'Manager',
      managerMobile: '01112223333',
      email: null,
      planId: 'p1',
      language: 'en',
      portalSlug: null,
      adminUsername: 'new.admin',
      adminPassword: 'a long enough password',
    });
    await settle();
    const post = backend.expectOne((request) => request.method === 'POST' && request.url === PLATFORM_API.clients);
    expect(post.request.body).toMatchObject({ adminUsername: 'new.admin', adminPassword: 'a long enough password', portalSlug: null });
    post.flush({ id: 't2', portalUrl: 'https://new-lab.skylis.app', administratorUsername: 'new.admin' }, { status: 201, statusText: 'Created' });
    expect(await pending).toEqual({ id: 't2', portalUrl: 'https://new-lab.skylis.app', administratorUsername: 'new.admin' });
    await runEffects();
    backend.expectOne((request) => request.url === PLATFORM_API.clients).flush(page([]));
    backend.verify();
  });

  it('deactivation sends the reason and a refusal keeps its problem code', async () => {
    const facade = TestBed.inject(ClientsFacade);
    await runEffects();
    backend.match(() => true).forEach((request) => request.flush(page([])));

    const pending = facade.deactivate.execute({ id: 't1', reason: 'unpaid' });
    await settle();
    const post = backend.expectOne(`${PLATFORM_API.clients}/t1/deactivate`);
    expect(post.request.body).toEqual({ reason: 'unpaid' });
    post.flush({ code: 'CLI-011', title: 'already inactive', status: 409 }, { status: 409, statusText: 'Conflict' });
    expect(await pending).toBeNull();
    expect(facade.deactivate.problem()).not.toBeNull();   // typed as ProblemError by the app's interceptor
  });

  it('opens the wallet of the selected client with its transactions and settles a request by topping up (SA-05)', async () => {
    const facade = TestBed.inject(WalletsFacade);
    await runEffects();
    backend.expectOne((request) => request.url === PLATFORM_API.plans).flush(page([]));
    backend.expectOne((request) => request.url === PLATFORM_API.clients).flush(page([client]));

    facade.selectClient('t1');
    await runEffects();
    backend.expectOne(`${PLATFORM_API.wallets}/t1`).flush(wallet);
    const transactions = backend.expectOne((request) => request.url === `${PLATFORM_API.wallets}/t1/transactions`);
    expect(transactions.request.params.get('pageSize')).toBe('20');
    expect(transactions.request.params.has('kind')).toBe(false);
    transactions.flush(page([]));
    await runEffects();
    expect(facade.wallet.value()?.pendingTopUpRequests.length).toBe(1);

    const pending = facade.topUp.execute({ amount: 750, reference: 'INV-750', note: null, topUpRequestId: 'r1' });
    await settle();
    const post = backend.expectOne(`${PLATFORM_API.wallets}/t1/top-up`);
    expect(post.request.body).toEqual({ amount: 750, reference: 'INV-750', note: null, topUpRequestId: 'r1' });
    post.flush({ id: 'tx1' }, { status: 201, statusText: 'Created' });
    expect(await pending).toEqual({ id: 'tx1' });
    await runEffects();
    backend.expectOne(`${PLATFORM_API.wallets}/t1`).flush({ ...wallet, balance: 750, pendingTopUpRequests: [] });
    backend.expectOne((request) => request.url === `${PLATFORM_API.wallets}/t1/transactions`).flush(page([]));
    backend.expectOne((request) => request.url === PLATFORM_API.clients).flush(page([client]));
    backend.verify();
  });

  it('terminates a session and reloads the list (SA-08)', async () => {
    const facade = TestBed.inject(SessionsFacade);
    const session: AdminSessionItem = {
      sessionId: 's2',
      userId: 'u2',
      displayName: 'Other Admin',
      username: 'other.admin',
      ipAddress: '10.0.0.2',
      sinceUtc: '2026-10-09T08:00:00Z',
      lastSeenAtUtc: '2026-10-09T09:00:00Z',
      expiresAtUtc: '2026-10-09T16:00:00Z',
      status: 'Active',
      isCurrent: false,
    };
    await runEffects();
    backend.expectOne((request) => request.url === PLATFORM_API.sessions).flush([session]);
    await runEffects();
    expect(facade.rows().length).toBe(1);

    const pending = facade.terminate.execute('s2');
    await settle();
    backend.expectOne(`${PLATFORM_API.sessions}/s2/terminate`).flush(null, { status: 204, statusText: 'No Content' });
    await pending;
    await runEffects();
    backend.expectOne((request) => request.url === PLATFORM_API.sessions).flush([]);
    backend.verify();
  });
});
