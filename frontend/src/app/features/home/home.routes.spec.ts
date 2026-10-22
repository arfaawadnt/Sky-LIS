import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { type ActivatedRouteSnapshot, type RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';

import { AUTH_API, type AuthResponse } from '../../core/api/auth.contracts';
import { AuthStore } from '../../core/auth/auth.store';
import { DASHBOARD_PATH, homeRedirect } from './home.routes';
import { HomePage } from './pages/home.page';

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

  // R1-04: this case used to keep the user on a developer placeholder (tenant id, raw expiry, permission keys)
  it('sends a user without the dashboard to the first menu entry they may open', async () => {
    await signInWith(['results.view', 'registration.view']);
    expect((run() as UrlTree).toString()).toBe('/tracker'); // the Lab Tracker is the first Reception entry (LIS-CR-006)
  });

  it('sends a user without any menu permission to the profile, open to every client user', async () => {
    await signInWith([]);
    expect((run() as UrlTree).toString()).toBe('/account/profile');
  });
});

describe('HomePage (R1-04)', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
  });

  it('shows a plain welcome and the way forward, never session internals', async () => {
    await signInWith(['some.permission']);
    const fixture = TestBed.createComponent(HomePage);
    await fixture.whenStable();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('Mona');
    expect(text).not.toContain('t1');
    expect(text).not.toContain('some.permission');
    expect((fixture.nativeElement as HTMLElement).querySelector('a')?.getAttribute('href')).toBe('/account/profile');
    TestBed.inject(HttpTestingController).verify(); // no session call
  });
});
