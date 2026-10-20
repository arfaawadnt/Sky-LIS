import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, type TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, type Routes, provideRouter } from '@angular/router';

import { routes } from '../app.routes';
import { ACCOUNT_API, type MyAccountProfile } from '../core/api/account.contracts';
import { AUTH_API, type AuthProfile, type AuthResponse } from '../core/api/auth.contracts';
import { authGuard } from '../core/auth/auth.guard';
import { AuthStore } from '../core/auth/auth.store';
import { platformGuard, tenantGuard } from '../core/auth/portal.guards';
import { FakeBrowserNavigation, FakeSessionChannel, provideSessionTesting } from '../core/auth/session-testing';
import { PLATFORM_TENANT_ID } from '../core/tenancy/tenant-ids';
import { branchGuard, followProfileChanges, passwordChangeGuard } from '../features/account/account.guards';
import { AccountSession } from '../features/account/state/account-session.service';
import { homeRedirect } from '../features/home/home.routes';
import { navRegistryGuard } from './nav.guard';

const LAB = '11111111-1111-1111-1111-111111111111';

@Component({ template: '' })
class BlankPage {}

/** The signed-in route of the application, as `app.routes.ts` declares it. */
const shellRoute = routes.find((route) => route.path === '' && route.canActivateChild !== undefined);

/**
 * The application's shell route with its own guards, over blank pages standing in for the lazy features (their guards as
 * `app.routes.ts` and the feature routes place them), so the composition is exercised by the real router.
 */
const MIRROR: Routes = [
  { path: 'login', component: BlankPage },
  {
    path: '',
    canActivate: shellRoute?.canActivate ?? [],
    canActivateChild: shellRoute?.canActivateChild ?? [],
    children: [
      { path: '', pathMatch: 'full', canActivate: [tenantGuard, homeRedirect], component: BlankPage },
      { path: 'billing', canActivate: [tenantGuard], children: ['payments', 'payment-methods', 'refunds', 'cash-closing'].map((path) => ({ path, component: BlankPage })) },
      { path: 'registration', canActivate: [tenantGuard], children: ['visits', 'visits/:id/job-order'].map((path) => ({ path, component: BlankPage })) },
      { path: 'account', canActivate: [tenantGuard], children: ['select-branch', 'profile', 'dashboard'].map((path) => ({ path, component: BlankPage })) },
      {
        path: 'admin',
        canActivate: [platformGuard],
        children: [{ path: '', pathMatch: 'full', component: BlankPage }, ...['clients', 'profile'].map((path) => ({ path, component: BlankPage }))],
      },
    ],
  },
];

describe('route guards composed on the shell (R1-01, R1-04, R2-01)', () => {
  let channel: FakeSessionChannel;

  beforeEach(() => {
    channel = new FakeSessionChannel();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter(MIRROR),
        provideHttpClient(),
        provideHttpClientTesting(),
        ...provideSessionTesting(new FakeBrowserNavigation(), channel),
      ],
    });
  });

  async function signIn(profile: Partial<AuthProfile>, mustChangePassword: boolean): Promise<void> {
    const pending = TestBed.inject(AuthStore).signIn('user', 'pw', null);
    const full: AuthProfile = { userId: 'u1', displayName: 'User', tenantId: LAB, roleName: null, permissions: [], language: 'en', scope: 'full', branchId: 'b1', branchIds: [], ...profile };
    TestBed.inject(HttpTestingController).expectOne(AUTH_API.signIn).flush({ step: 'signed-in', accessToken: 't', expiresAtUtc: null, refreshToken: null, profile: full, tenants: null } satisfies AuthResponse);
    await pending;
    TestBed.inject(AccountSession).set({ userId: full.userId, tenantId: full.tenantId, mustChangePassword } as MyAccountProfile);
  }

  async function open(url: string): Promise<string> {
    const router = TestBed.inject(Router);
    await router.navigateByUrl(url);
    return router.url;
  }

  it('keeps the guard order of the shell: registry, branch, password change', () => {
    expect(shellRoute?.canActivate).toEqual([authGuard]);
    expect(shellRoute?.canActivateChild).toEqual([navRegistryGuard, branchGuard, passwordChangeGuard]);
  });

  it('opens the Release-1 refunds from Payments for a cashier, and never a Release-2 page (R1-01)', async () => {
    await signIn({ permissions: ['billing.view'] }, false);

    expect(await open('/billing/refunds?visitId=v1')).toBe('/billing/refunds?visitId=v1');
    expect(await open('/billing/cash-closing')).toBe('/billing/payments'); // refused → home → first menu entry
  });

  it('lands a user without the dashboard on their first menu entry when a URL is refused (R1-04)', async () => {
    await signIn({ permissions: ['registration.view'] }, false);

    expect(await open('/billing/payments')).toBe('/registration/visits');
    expect(await open('/registration/visits/v1/job-order')).toBe('/registration/visits/v1/job-order');
  });

  it('sends a must-change laboratory user without a branch to the profile, where the branch is not asked (CP-04, CP-38)', async () => {
    await signIn({ permissions: ['billing.view'], branchId: null }, true);

    expect(await open('/billing/payments')).toBe('/account/profile?mustChange=1');
  });

  it('sends a must-change platform user refused by the registry to the forced change, not to a dead end (C-14, C-15)', async () => {
    await signIn({ tenantId: PLATFORM_TENANT_ID, branchId: null }, true);

    expect(await open('/admin/clients')).toBe('/admin/profile?mustChange=1');
    expect(await open('/billing/payments')).toBe('/admin/profile?mustChange=1');
  });

  it('lets a tab held on the forced change go once another tab changed the password (R2-01)', async () => {
    await signIn({ tenantId: PLATFORM_TENANT_ID, branchId: null, permissions: ['tenancy.view'] }, true);
    TestBed.runInInjectionContext(() => followProfileChanges());
    expect(await open('/admin/clients')).toBe('/admin/profile?mustChange=1');

    channel.deliver({ kind: 'profile-changed', userId: 'u1', tenantId: PLATFORM_TENANT_ID });
    TestBed.tick();

    const backend = TestBed.inject(HttpTestingController);
    let reads: TestRequest[] = [];
    await vi.waitFor(() => {
      reads = [...reads, ...backend.match(ACCOUNT_API.profile)];
      expect(reads.length).toBe(1);
    });
    reads[0]?.flush({ userId: 'u1', tenantId: PLATFORM_TENANT_ID, mustChangePassword: false });
    const router = TestBed.inject(Router);
    await vi.waitFor(() => expect(router.url).toBe('/admin'));
    expect(await open('/admin/clients')).toBe('/admin/clients');
  });

  it('keeps the forced change when the profile still requires it, and ignores another user’s change (R2-01)', async () => {
    await signIn({ tenantId: PLATFORM_TENANT_ID, branchId: null }, true);
    TestBed.runInInjectionContext(() => followProfileChanges());
    expect(await open('/admin')).toBe('/admin/profile?mustChange=1');

    channel.deliver({ kind: 'profile-changed', userId: 'someone-else', tenantId: PLATFORM_TENANT_ID });
    TestBed.tick();
    TestBed.inject(HttpTestingController).expectNone(ACCOUNT_API.profile);

    channel.deliver({ kind: 'profile-changed', userId: 'u1', tenantId: PLATFORM_TENANT_ID });
    TestBed.tick();
    const backend = TestBed.inject(HttpTestingController);
    let reads: TestRequest[] = [];
    await vi.waitFor(() => {
      reads = [...reads, ...backend.match(ACCOUNT_API.profile)];
      expect(reads.length).toBe(1);
    });
    reads[0]?.flush({ userId: 'u1', tenantId: PLATFORM_TENANT_ID, mustChangePassword: true });
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(TestBed.inject(Router).url).toBe('/admin/profile?mustChange=1');
  });
});
