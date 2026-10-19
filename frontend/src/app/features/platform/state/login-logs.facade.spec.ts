import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { type ClientLoginLogItem, type Paged, PLATFORM_API } from '../../../core/api/platform.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { LoginLogsFacade } from './login-logs.facade';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function page<T>(items: T[]): Paged<T> {
  return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 };
}

const row: ClientLoginLogItem = {
  familyId: 'f1',
  tenantId: 't1',
  clientNameEn: 'Al Noor Lab',
  clientNameAr: 'معمل النور',
  userId: 'u1',
  username: 'noor.reception',
  displayName: 'Noor Reception',
  loginAtUtc: '2026-10-19T09:00:00Z',
  logoutAtUtc: '2026-10-19T09:07:00Z',
  lastSeenAtUtc: '2026-10-19T09:07:00Z',
  ipAddress: '10.0.0.7',
  device: 'Computer',
  os: 'Windows 10',
  browser: 'Chrome 129',
  durationMinutes: 7,
  status: 'SignedOut',
  revocationReason: null,
};

describe('LoginLogsFacade (SA-08b, LIS-CR-003 §2)', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), LoginLogsFacade],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  function expectLogs(): ReturnType<HttpTestingController['expectOne']> {
    return backend.expectOne((request) => request.method === 'GET' && request.url === PLATFORM_API.loginLogs);
  }

  it('lists with only the criteria that are set and loads the clients of the filter', async () => {
    const facade = TestBed.inject(LoginLogsFacade);
    await runEffects();
    const first = expectLogs();
    expect(first.request.params.keys().sort()).toEqual(['page', 'pageSize']);
    first.flush(page([row]));
    const clients = backend.expectOne((request) => request.url === PLATFORM_API.clients);
    expect([clients.request.params.get('sort'), clients.request.params.get('pageSize')]).toEqual(['nameEn', '200']);
    clients.flush(page([]));
    await runEffects();
    expect(facade.rows().length).toBe(1);
    expect(facade.totalCount()).toBe(1);

    facade.setPage({ page: 3, pageSize: 20 });
    await runEffects();
    expectLogs().flush(page([]));

    facade.applyCriteria({ tenantId: 't1', search: 'noor', loginFromUtc: '2026-10-19T00:00:00.000Z', loginToUtc: null, logoutFromUtc: null, logoutToUtc: null, device: 'Mobile', minMinutes: 5, maxMinutes: null, status: 'SignedOut' });
    await runEffects();
    const filtered = expectLogs();
    const params = filtered.request.params;
    expect([params.get('tenantId'), params.get('search'), params.get('loginFromUtc'), params.get('device'), params.get('minMinutes'), params.get('status'), params.get('page'), params.get('pageSize')])
      .toEqual(['t1', 'noor', '2026-10-19T00:00:00.000Z', 'Mobile', '5', 'SignedOut', '1', '20']);
    expect(params.has('loginToUtc')).toBe(false);
    filtered.flush(page([]));
  });

  it('B-03: sorts on the server from page 1, the client column by language', async () => {
    const facade = TestBed.inject(LoginLogsFacade);
    const i18n = TestBed.inject(I18nService);
    await runEffects();
    expectLogs().flush(page([]));
    backend.expectOne((request) => request.url === PLATFORM_API.clients).flush(page([]));

    facade.setPage({ page: 2, pageSize: 50 });
    await runEffects();
    expectLogs().flush(page([]));

    facade.setSort({ key: 'durationMinutes', direction: 'desc' });
    await runEffects();
    const byDuration = expectLogs();
    expect([byDuration.request.params.get('sort'), byDuration.request.params.get('direction'), byDuration.request.params.get('page')]).toEqual(['duration', 'desc', '1']);
    byDuration.flush(page([]));

    facade.setSort({ key: 'client', direction: 'asc' });
    await runEffects();
    expect(expectLogs().request.params.get('sort')).toBe('client');

    i18n.setLanguage('ar');
    await runEffects();
    expect(expectLogs().request.params.get('sort')).toBe('clientAr');
    i18n.setLanguage('en');
  });

  it('asks the server for the CSV of the same criteria and order and keeps its file name', async () => {
    const facade = TestBed.inject(LoginLogsFacade);
    await runEffects();
    expectLogs().flush(page([]));
    backend.expectOne((request) => request.url === PLATFORM_API.clients).flush(page([]));

    facade.applyCriteria({ tenantId: 't1', search: null, loginFromUtc: null, loginToUtc: null, logoutFromUtc: null, logoutToUtc: null, device: null, minMinutes: null, maxMinutes: null, status: 'Active' });
    facade.setSort({ key: 'user', direction: 'asc' });
    await runEffects();
    expectLogs().flush(page([]));

    const pending = facade.export.execute();
    await settle();
    const request = backend.expectOne((candidate) => candidate.url === `${PLATFORM_API.loginLogs}/export`);
    expect([request.request.params.get('tenantId'), request.request.params.get('status'), request.request.params.get('sort'), request.request.params.get('direction'), request.request.params.get('language')])
      .toEqual(['t1', 'Active', 'user', 'asc', 'en']);
    expect(request.request.params.has('page')).toBe(false);
    request.flush(new Blob(['﻿Client,User'], { type: 'text/csv' }), { headers: { 'Content-Disposition': 'attachment; filename=client-login-logs-2026-10-19-0900.csv' } });
    const file = await pending;
    expect(file?.fileName).toBe('client-login-logs-2026-10-19-0900.csv');
    expect(file?.blob.size).toBeGreaterThan(0);
  });
});
