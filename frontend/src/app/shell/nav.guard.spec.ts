import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { type ActivatedRouteSnapshot, type RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';

import shellAr from '../../../public/i18n/shell.ar.json';
import shellEn from '../../../public/i18n/shell.en.json';
import { AUTH_API, type AuthResponse } from '../core/api/auth.contracts';
import { AuthStore } from '../core/auth/auth.store';
import { PLATFORM_TENANT_ID } from '../core/tenancy/tenant-ids';
import { ToastService } from '../shared/ui/toast/toast.service';
import { landingEntry } from './nav.filter';
import { isOutOfScopeFeature, mayOpen, navRegistryGuard, registryEntryFor } from './nav.guard';
import { NAV_REGISTRY, type NavEntry, inMenu } from './nav.registry';

const LAB = '11111111-1111-1111-1111-111111111111';

async function signIn(tenantId: string, permissions: readonly string[]): Promise<void> {
  const store = TestBed.inject(AuthStore);
  const pending = store.signIn('user', 'pw', null);
  TestBed.inject(HttpTestingController)
    .expectOne(AUTH_API.signIn)
    .flush({
      step: 'signed-in',
      accessToken: 't',
      expiresAtUtc: null,
      refreshToken: null,
      profile: { userId: 'u1', displayName: 'User', tenantId, roleName: null, permissions, language: 'en', scope: 'full', branchId: 'b1', branchIds: [] },
      tenants: null,
    } satisfies AuthResponse);
  await pending;
}

function guard(url: string): true | UrlTree {
  return TestBed.runInInjectionContext(() => navRegistryGuard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot));
}

describe('navRegistryGuard (A-08, C-15)', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
  });

  it('finds the entry with the longest matching path, never a sibling with the same prefix', () => {
    expect(registryEntryFor('/billing/payments?visit=1')?.path).toBe('/billing/payments');
    expect(registryEntryFor('/registration/visits/abc')?.path).toBe('/registration/visits');
    expect(registryEntryFor('/admin/catalogue/microbiology/organisms')?.path).toBe('/admin/catalogue/microbiology/organisms');
    expect(registryEntryFor('/admin/clients/123')?.path).toBe('/admin/clients');
    expect(registryEntryFor('/admin/unknown')).toBeNull(); // '/admin' matches only itself
    expect(registryEntryFor('/account/select-branch')).toBeNull();
  });

  it('refuses a typed URL whose permission the user lacks and sends a laboratory user home', async () => {
    await signIn(LAB, ['registration.view']);

    const refused = guard('/billing/payments');
    expect(refused).toBeInstanceOf(UrlTree);
    expect((refused as UrlTree).toString()).toBe('/');
    expect(guard('/registration/visits/abc')).toBe(true);
    expect(guard('/account/profile')).toBe(true); // permission null
    expect(guard('/account/select-branch')).toBe(true); // no entry: today's behaviour
  });

  it('refuses Release-2 pages even with the permission, including feature routes without an entry', async () => {
    await signIn(LAB, ['billing.view', 'samples.view', 'integration.view', 'wallet.view']);

    expect(guard('/billing/payments')).toBe(true);
    // R1-01: refunds are an action of Payments in Release 1 (this test used to expect them refused, the regression)
    expect(guard('/billing/refunds?visitId=v1')).toBe(true);
    for (const url of ['/billing/batch-payment', '/billing/cash-closing', '/account/wallet', '/samples/collection', '/samples/track/S-1', '/integration/analysers']) {
      expect((guard(url) as UrlTree).toString()).toBe('/');
    }

    expect(isOutOfScopeFeature('/samples/anything')).toBe(true);
    expect(isOutOfScopeFeature('/billing/anything')).toBe(false); // billing has release-1 pages
  });

  it('refuses admin URLs outside the platform user’s rights and sends them to the admin home (C-15)', async () => {
    await signIn(PLATFORM_TENANT_ID, ['catalogue.view']);

    expect(guard('/admin/catalogue/tests')).toBe(true);
    expect(guard('/admin')).toBe(true);
    expect(guard('/admin/profile')).toBe(true);
    for (const url of ['/admin/clients', '/admin/organization/users', '/admin/parameters', '/admin/audit', '/admin/library/pcr-kits']) {
      expect((guard(url) as UrlTree).toString()).toBe('/admin');
    }
  });

  it('never refuses the portal homes, so a refusal cannot loop', () => {
    const none = (): boolean => false;
    expect(mayOpen('/', none)).toBe(true);
    expect(mayOpen('/admin', none)).toBe(true);
  });

  it('leaves a signed-out navigation to the auth guard', () => {
    expect(guard('/billing/payments')).toBe(true);
  });

  it('tells the user why a refused URL landed on the home, never silently (R1-04)', async () => {
    await signIn(LAB, ['registration.view']);
    const toasts = TestBed.inject(ToastService);

    expect(guard('/registration/visits')).toBe(true);
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(toasts.toasts()).toEqual([]);

    expect((guard('/billing/payments') as UrlTree).toString()).toBe('/');
    await vi.waitFor(() => expect(toasts.toasts().length).toBe(1));
    expect(toasts.toasts()[0]?.kind).toBe('warn');
    expect(toasts.toasts()[0]?.text).toMatch(/pageNotAvailable|not available/);
  });
});

/**
 * R1-01: every page a Release-1 page opens from one of its actions (not from the menu) must open for a user who holds the
 * permission of that page; a refusal there is a dead action. One row per action target, with the permission the page needs.
 */
const RELEASE_1_ACTION_TARGETS: readonly (readonly [portal: 'client' | 'admin', url: string, permission: string | null])[] = [
  ['client', '/billing/refunds?visitId=v1', 'billing.view'], // Payments → "Refund the balance"
  ['client', '/billing/payments?visitId=v1', 'billing.view'], // registration, search register → receipts
  ['client', '/registration/visits/v1', 'registration.view'], // search register → edit the visit
  ['client', '/registration/visits/v1/job-order', 'registration.view'], // search register, registration → job order
  ['client', '/registration/new?patientId=p1', 'registration.create'], // price query → register
  ['client', '/registration?patientId=p1', null], // the patient file's link (feature root, no entry of its own)
  ['client', '/patients/p1', 'patients.view'], // patients → patient file
  ['client', '/results/by-accession?accession=A1', 'results.view'], // search register → report
  ['client', '/pricing/base-price-lists/l1', 'pricing.view'], // price lists → editor
  ['client', '/catalogue/services/new', 'catalogue.view'],
  ['client', '/catalogue/services/s1', 'catalogue.view'],
  ['client', '/catalogue/tests/new', 'catalogue.view'],
  ['client', '/catalogue/tests/t1', 'catalogue.view'],
  ['client', '/organization/roles/new', 'organization.view'],
  ['client', '/organization/roles/r1', 'organization.view'],
  ['client', '/organization/users/new', 'identity.view'],
  ['client', '/organization/users/u1', 'identity.view'],
  ['client', '/treasury/access', 'treasury.view'], // Payments, Refunds → request treasury access
  ['client', '/account/select-branch?change=1', null], // user menu → change branch
  ['client', '/account/profile?mustChange=1', null], // the password-change guard
  ['admin', '/admin/organization/roles/new', 'organization.view'],
  ['admin', '/admin/organization/users/u1', 'identity.view'],
  ['admin', '/admin/catalogue/services/new', 'catalogue.view'],
  ['admin', '/admin/catalogue/tests/t1', 'catalogue.view'],
  ['admin', '/admin/subscriptions?client=c1', 'tenancy.view'], // clients → wallet
  ['admin', '/admin/clients', 'tenancy.view'], // platform dashboard → view all
  ['admin', '/admin/profile?mustChange=1', null],
];

describe('Release-1 action targets (R1-01)', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
  });

  for (const [portal, url, permission] of RELEASE_1_ACTION_TARGETS) {
    it(`opens ${url} (${portal}) for a user holding ${permission ?? 'no permission'}, and only then`, async () => {
      expect(mayOpen(url, (held) => held === permission)).toBe(true);
      if (permission !== null) {
        expect(mayOpen(url, () => false)).toBe(false);
      }

      await signIn(portal === 'admin' ? PLATFORM_TENANT_ID : LAB, permission === null ? [] : [permission]);
      expect(guard(url)).toBe(true);
    });
  }

  it('keeps the Release-1 refunds out of the menu while the guard lets them open', () => {
    const refunds = NAV_REGISTRY.find((entry) => entry.path === '/billing/refunds');
    expect(refunds?.release).toBe('R1');
    expect(refunds && inMenu(refunds)).toBe(false);
    expect(refunds && inMenu(refunds, 'R2')).toBe(true); // Release 2 lists them in the Finance group
  });
});

describe('landingEntry (R1-04)', () => {
  it('is the dashboard for whoever holds dashboard.view, else the first menu entry the user may open', () => {
    expect(landingEntry('client', (permission) => permission === 'dashboard.view' || permission === 'registration.view')?.path).toBe('/account/dashboard');
    expect(landingEntry('client', (permission) => permission === 'billing.view')?.path).toBe('/billing/payments');
    expect(landingEntry('client', () => false)?.path).toBe('/account/profile');
    expect(landingEntry('admin', () => false)?.path).toBe('/admin');
  });

  it('never lands on an action-only page', () => {
    for (const permission of ['billing.view', 'registration.view', 'catalogue.view']) {
      const path = landingEntry('client', (held) => held === permission)?.path ?? '';
      expect(NAV_REGISTRY.find((entry) => entry.path === path && !inMenu(entry))).toBeUndefined();
    }
  });
});

describe('navigation labels (C-16)', () => {
  const dictionaries: Readonly<Record<string, Readonly<Record<string, string>>>> = { en: shellEn, ar: shellAr };

  for (const language of ['en', 'ar'] as const) {
    for (const portal of ['client', 'admin'] as const) {
      it(`gives every ${portal} menu entry of the release a distinct ${language} label within its group`, () => {
        const dictionary = dictionaries[language] ?? {};
        const visible = NAV_REGISTRY.filter((entry: NavEntry) => entry.portal === portal && inMenu(entry));
        const byGroup = new Map<string, string[]>();
        for (const entry of visible) {
          const text = dictionary[entry.i18nKey.replace(/^shell\./, '')];
          expect(text, entry.i18nKey).toBeTruthy();
          byGroup.set(entry.group, [...(byGroup.get(entry.group) ?? []), text ?? '']);
        }

        for (const [group, texts] of byGroup) {
          expect(new Set(texts).size, `${group}: ${texts.join(', ')}`).toBe(texts.length);
        }
      });
    }
  }
});
