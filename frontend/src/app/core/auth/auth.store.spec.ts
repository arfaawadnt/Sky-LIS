import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { AUTH_API, type AuthResponse } from '../api/auth.contracts';
import { AuthStore } from './auth.store';

function signedIn(token: string, branchId: string | null = null): AuthResponse {
  return {
    step: 'signed-in',
    accessToken: token,
    expiresAtUtc: '2026-10-09T12:15:00Z',
    refreshToken: null,
    profile: {
      userId: 'u1',
      displayName: 'Mona Adel',
      tenantId: 't1',
      roleName: null,
      permissions: ['audit.view'],
      language: 'ar',
      scope: 'full',
      branchId,
      branchIds: ['b1', 'b2'],
    },
    tenants: null,
  };
}

describe('AuthStore (D-01, P-61)', () => {
  let store: AuthStore;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()] });
    store = TestBed.inject(AuthStore);
    http = TestBed.inject(HttpTestingController);
    window.localStorage.clear();
    window.sessionStorage.clear();
  });

  afterEach(() => http.verify());

  it('keeps the access token in memory only and exposes identity from the profile', async () => {
    const pending = store.signIn('mona', 'secret', null);
    const request = http.expectOne(AUTH_API.signIn);
    expect(request.request.body).toEqual({ tenant: null, username: 'mona', password: 'secret' });
    request.flush(signedIn('token-1'));
    await pending;

    expect(store.token()).toBe('token-1');
    expect(store.isAuthenticated()).toBe(true);
    expect(store.currentUser()?.displayName).toBe('Mona Adel');
    expect(store.currentTenant()).toBe('t1');
    expect(store.hasPermission('audit.view')).toBe(true);
    expect(store.hasPermission('audit.export')).toBe(false);
    expect(window.localStorage.length).toBe(0);
    expect(window.sessionStorage.length).toBe(0);
    expect(document.cookie).toBe('');
  });

  it('refreshes single-flight: concurrent callers share one request', async () => {
    const first = store.refresh();
    const second = store.refresh();

    const requests = http.match(AUTH_API.refresh);
    expect(requests.length).toBe(1);
    requests[0]?.flush(signedIn('token-2'));

    expect(await first).toBe('token-2');
    expect(await second).toBe('token-2');
    expect(store.token()).toBe('token-2');

    const again = store.refresh();
    http.expectOne(AUTH_API.refresh).flush(signedIn('token-3'));
    expect(await again).toBe('token-3');
  });

  it('hydrate always resolves and leaves the store signed out when the cookie is gone', async () => {
    const pending = store.hydrate();
    http.expectOne(AUTH_API.refresh).flush({ code: 'AUTH-012', title: 'rejected', status: 401 }, { status: 401, statusText: 'Unauthorized' });
    await pending;

    expect(store.isHydrated()).toBe(true);
    expect(store.isAuthenticated()).toBe(false);
    expect(store.token()).toBeNull();
  });

  it('a select-tenant answer leaves the store signed out until the choice is posted', async () => {
    const pending = store.signIn('mona', 'secret', null);
    http.expectOne(AUTH_API.signIn).flush({ step: 'select-tenant', accessToken: null, expiresAtUtc: null, refreshToken: null, profile: null, tenants: [{ tenantId: 't1', displayName: 'Lab 1' }] } satisfies AuthResponse);
    const response = await pending;

    expect(response.step).toBe('select-tenant');
    expect(store.isAuthenticated()).toBe(false);
  });

  it('exposes the branch of the session and replaces the profile after selecting one (CP-04)', async () => {
    const signIn = store.signIn('mona', 'secret', null);
    http.expectOne(AUTH_API.signIn).flush(signedIn('token-1'));
    await signIn;

    expect(store.currentBranch()).toBeNull();
    expect(store.branchIds()).toEqual(['b1', 'b2']);

    const pending = store.selectBranch('b2');
    const request = http.expectOne(AUTH_API.selectBranch);
    expect(request.request.body).toEqual({ branchId: 'b2' });
    request.flush(signedIn('token-1', 'b2').profile);
    await pending;

    expect(store.currentBranch()).toBe('b2');
    expect(store.token()).toBe('token-1');
    expect(store.isAuthenticated()).toBe(true);
  });

  it('a refused branch leaves the profile untouched', async () => {
    const signIn = store.signIn('mona', 'secret', null);
    http.expectOne(AUTH_API.signIn).flush(signedIn('token-1'));
    await signIn;

    const pending = store.selectBranch('b9');
    http.expectOne(AUTH_API.selectBranch).flush({ code: 'TENANT-005', title: 'not assigned', status: 403 }, { status: 403, statusText: 'Forbidden' });
    await expect(pending).rejects.toBeTruthy();

    expect(store.currentBranch()).toBeNull();
    expect(store.isAuthenticated()).toBe(true);
  });

  it('password recovery posts the identifier, then the code and the new password (CP-03)', async () => {
    const start = store.startPasswordRecovery('+201001234567', 'ar');
    const started = http.expectOne(AUTH_API.recoveryStart);
    expect(started.request.body).toEqual({ usernameOrMobile: '+201001234567', language: 'ar' });
    started.flush(null, { status: 202, statusText: 'Accepted' });
    await start;

    const complete = store.completePasswordRecovery('mona', '123456', 'New-Password-2026!');
    const completed = http.expectOne(AUTH_API.recoveryComplete);
    expect(completed.request.body).toEqual({ usernameOrMobile: 'mona', code: '123456', newPassword: 'New-Password-2026!' });
    completed.flush(null, { status: 204, statusText: 'No Content' });
    await complete;

    expect(store.isAuthenticated()).toBe(false);
  });

  it('sign-out clears local state even when the server call fails', async () => {
    const signIn = store.signIn('mona', 'secret', null);
    http.expectOne(AUTH_API.signIn).flush(signedIn('token-1'));
    await signIn;

    const signOut = store.signOut();
    http.expectOne(AUTH_API.signOut).flush(null, { status: 500, statusText: 'Server Error' });
    await signOut;

    expect(store.token()).toBeNull();
    expect(store.isAuthenticated()).toBe(false);
  });
});
