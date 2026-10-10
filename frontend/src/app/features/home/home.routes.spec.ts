import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { type ActivatedRouteSnapshot, type RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';

import { AUTH_API, type AuthResponse } from '../../core/api/auth.contracts';
import { AuthStore } from '../../core/auth/auth.store';
import { DASHBOARD_PATH, homeRedirect } from './home.routes';

async function signInWith(permissions: readonly string[]): Promise<void> {
  const pending = TestBed.inject(AuthStore).signIn('user', 'pw', null);
  TestBed.inject(HttpTestingController).expectOne(AUTH_API.signIn).flush({
    step: 'signed-in',
    accessToken: 't',
    expiresAtUtc: null,
    refreshToken: null,
    profile: { userId: 'u1', displayName: 'Mona', tenantId: 't1', roleName: null, permissions, language: 'en', scope: 'full' },
    tenants: null,
  } satisfies AuthResponse);
  await pending;
}

describe('homeRedirect (SRS CP-05 is the client home)', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
  });

  const run = (): true | UrlTree => TestBed.runInInjectionContext(() => homeRedirect({} as ActivatedRouteSnapshot, { url: '/' } as RouterStateSnapshot)) as true | UrlTree;

  it('forwards a user who may see the dashboard to it', async () => {
    await signInWith(['dashboard.view']);
    expect((run() as UrlTree).toString()).toBe(DASHBOARD_PATH);
  });

  it('keeps anyone else on the session summary', async () => {
    await signInWith([]);
    expect(run()).toBe(true);
  });
});
