import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { type ActivatedRouteSnapshot, type RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';

import { AUTH_API, type AuthResponse } from '../api/auth.contracts';
import { AuthStore } from '../auth/auth.store';
import { platformGuard, tenantGuard } from '../auth/portal.guards';
import { PLATFORM_TENANT_ID } from '../tenancy/tenant-ids';
import { PortalService } from './portal.service';

async function signInTo(tenantId: string): Promise<void> {
  const store = TestBed.inject(AuthStore);
  const backend = TestBed.inject(HttpTestingController);
  const pending = store.signIn('user', 'pw', null);
  backend.expectOne(AUTH_API.signIn).flush({
    step: 'signed-in',
    accessToken: 't',
    expiresAtUtc: null,
    refreshToken: null,
    profile: { userId: 'u1', displayName: 'Mona', tenantId, roleName: 'Admin', permissions: [], language: 'en', scope: 'full' },
    tenants: null,
  } satisfies AuthResponse);
  await pending;
}

function runGuard(guard: typeof platformGuard): true | UrlTree {
  const result = TestBed.runInInjectionContext(() => guard({} as ActivatedRouteSnapshot, { url: '/x' } as RouterStateSnapshot));
  return result as true | UrlTree;
}

describe('PortalService and portal guards (MODULES.md §1: two portals, one SPA)', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
  });

  it('is the client portal until the session belongs to the platform tenant', async () => {
    const portal = TestBed.inject(PortalService);
    expect(portal.portal()).toBe('client');
    expect(portal.homePath()).toBe('/');

    await signInTo(PLATFORM_TENANT_ID);
    expect(portal.portal()).toBe('admin');
    expect(portal.isPlatform()).toBe(true);
    expect(portal.homePath()).toBe('/admin');

    TestBed.inject(AuthStore).clear();
    expect(portal.portal()).toBe('client');
  });

  it('platformGuard admits the platform tenant and sends a laboratory tenant to its home', async () => {
    await signInTo('11111111-1111-1111-1111-111111111111');
    const refused = runGuard(platformGuard);
    expect(refused).toBeInstanceOf(UrlTree);
    expect((refused as UrlTree).toString()).toBe('/');
    expect(runGuard(tenantGuard)).toBe(true);
  });

  it('tenantGuard sends the platform tenant to /admin', async () => {
    await signInTo(PLATFORM_TENANT_ID);
    expect(runGuard(platformGuard)).toBe(true);
    const refused = runGuard(tenantGuard);
    expect(refused).toBeInstanceOf(UrlTree);
    expect((refused as UrlTree).toString()).toBe('/admin');
  });
});
