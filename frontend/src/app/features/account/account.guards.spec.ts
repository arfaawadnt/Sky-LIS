import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { type ActivatedRouteSnapshot, type RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';

import { ACCOUNT_API } from '../../core/api/account.contracts';
import { AUTH_API, type AuthProfile, type AuthResponse } from '../../core/api/auth.contracts';
import { AuthStore } from '../../core/auth/auth.store';
import { PLATFORM_TENANT_ID } from '../../core/tenancy/tenant-ids';
import { ACCOUNT_PATHS, branchGuard, passwordChangeGuard } from './account.guards';

const LAB = '11111111-1111-1111-1111-111111111111';

async function signInAs(profile: Partial<AuthProfile>): Promise<void> {
  const store = TestBed.inject(AuthStore);
  const backend = TestBed.inject(HttpTestingController);
  const pending = store.signIn('user', 'pw', null);
  backend.expectOne(AUTH_API.signIn).flush({
    step: 'signed-in',
    accessToken: 't',
    expiresAtUtc: null,
    refreshToken: null,
    profile: { userId: 'u1', displayName: 'Mona', tenantId: LAB, roleName: null, permissions: [], language: 'en', scope: 'full', branchId: null, branchIds: [], ...profile },
    tenants: null,
  } satisfies AuthResponse);
  await pending;
}

function run<T>(guard: (route: ActivatedRouteSnapshot, state: RouterStateSnapshot) => T, url: string): T {
  return TestBed.runInInjectionContext(() => guard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot));
}

describe('Account guards (CP-04 branch, CP-38 must-change)', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
  });

  it('branchGuard sends a laboratory session without a branch to the selection, keeping where it was going', async () => {
    await signInAs({ branchId: null });

    const refused = run(branchGuard, '/registration/new?x=1');
    expect(refused).toBeInstanceOf(UrlTree);
    expect((refused as UrlTree).toString()).toBe(`${ACCOUNT_PATHS.selectBranch}?returnUrl=%2Fregistration%2Fnew%3Fx%3D1`);
    expect(run(branchGuard, `${ACCOUNT_PATHS.selectBranch}?change=1`)).toBe(true);
    expect(run(branchGuard, ACCOUNT_PATHS.profile)).toBe(true);
  });

  it('branchGuard lets a session with a branch and the platform portal pass', async () => {
    await signInAs({ branchId: 'b1' });
    expect(run(branchGuard, '/registration')).toBe(true);

    TestBed.inject(AuthStore).clear();
    await signInAs({ tenantId: PLATFORM_TENANT_ID, branchId: null });
    expect(run(branchGuard, '/admin')).toBe(true);
  });

  it('passwordChangeGuard keeps a must-change user on the profile', async () => {
    await signInAs({ branchId: 'b1' });
    const backend = TestBed.inject(HttpTestingController);

    const pending = run(passwordChangeGuard, '/account/dashboard');
    backend.expectOne(ACCOUNT_API.profile).flush({ userId: 'u1', tenantId: LAB, mustChangePassword: true });
    const result = await pending;
    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe(`${ACCOUNT_PATHS.profile}?mustChange=1`);

    expect(await run(passwordChangeGuard, ACCOUNT_PATHS.profile)).toBe(true);
    expect(await run(passwordChangeGuard, '/samples')).toBeInstanceOf(UrlTree);   // cached: no second call
    backend.verify();
  });
});
