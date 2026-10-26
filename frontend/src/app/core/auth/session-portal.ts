import { InjectionToken, inject } from '@angular/core';

import type { Portal } from '../portal/portal';
import { PLATFORM_TENANT_ID } from '../tenancy/tenant-ids';
import { BROWSER_NAVIGATION, belongsToAdminPortal } from './session-navigation';

/**
 * LIS-CR-014 G1 (one browser, two portals): the laboratory portal and the Super Admin portal each keep their own session —
 * refresh cookie `__Secure-rt` / `__Secure-rta` on the server, session channel and refresh lock per portal in the browser —
 * so a tab of one portal never follows, replaces or signs out the other portal's session.
 *
 * The portal of a tab is its **address**: `/admin` and everything below it (the Super Admin sign-in included) is the Super
 * Admin portal; every other page (the laboratory addresses `/lab/<slug>`, the find page `/login`, the client pages) is the
 * laboratory portal. It is read at every call, so the auth requests, the channel and the lock follow the tab wherever it goes.
 */
export const PORTALS: readonly Portal[] = ['client', 'admin'];

/** The portal of an address (`/admin/clients` → admin, `/registration/visits` → client). */
export function portalOfUrl(url: string): Portal {
  return belongsToAdminPortal(url) ? 'admin' : 'client';
}

/** The portal a session belongs to by its profile: the platform tenant is the Super Admin portal. */
export function portalOfTenant(tenantId: string | null): Portal {
  return tenantId === PLATFORM_TENANT_ID ? 'admin' : 'client';
}

/** The tab's portal at this moment (from the address bar, `BROWSER_NAVIGATION`); a token so specs can pin it. */
export const SESSION_PORTAL = new InjectionToken<() => Portal>('SESSION_PORTAL', {
  providedIn: 'root',
  factory: () => {
    const navigation = inject(BROWSER_NAVIGATION);
    return () => portalOfUrl(navigation.currentUrl());
  },
});

/** The query parameter of the cookie endpoints (`refresh`, `sign-out`); the server's default is the laboratory portal. */
export const PORTAL_PARAMETER = 'portal';

/**
 * The URL of a cookie endpoint for a portal: `/api/v1/auth/refresh?portal=admin` for the Super Admin, the plain path for the
 * laboratory portal (the server reads `__Secure-rt` when no portal is named, as it did before the split).
 */
export function portalAuthUrl(url: string, portal: Portal): string {
  return portal === 'admin' ? `${url}?${PORTAL_PARAMETER}=admin` : url;
}
