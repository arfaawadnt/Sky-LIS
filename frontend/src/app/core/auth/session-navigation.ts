import { DOCUMENT } from '@angular/common';
import { InjectionToken, inject } from '@angular/core';

import type { AuthProfile } from '../api/auth.contracts';
import { LABORATORY_SLUG_PATTERN } from '../api/public-laboratory.contracts';
import { PLATFORM_TENANT_ID } from '../tenancy/tenant-ids';

/**
 * LIS-CR-005: every laboratory signs in at its own address and the Super Admin at its own; `/login` is the
 * **Find your laboratory** page, where a session of unknown origin ends up.
 */
export const SIGN_IN_PATH = '/login';

/** The Super Admin sign-in (`/admin/login`, recovery under `/admin/login/recover`). */
export const ADMIN_SIGN_IN_PATH = '/admin/login';

/** The prefix of a laboratory's sign-in address (`/lab/<slug>`, recovery under `/lab/<slug>/recover`). */
export const LABORATORY_PATH_PREFIX = '/lab';

/**
 * The browser's address bar as the session sees it (C-01, A-01). Ending a session, or noticing that the browser now
 * belongs to another user, ends with a **full page load**: it discards every module's in-memory state (route-provided
 * facades, resources, caches) at once, which no router navigation can guarantee. A token so specs can replace it.
 */
export interface BrowserNavigation {
  /** Path, query and fragment of the current document (`/results/worklist?x=1`). */
  currentUrl(): string;
  /** Loads `url` as a new document. */
  load(url: string): void;
}

export const BROWSER_NAVIGATION = new InjectionToken<BrowserNavigation>('BROWSER_NAVIGATION', {
  providedIn: 'root',
  factory: () => {
    const view = inject(DOCUMENT).defaultView;
    return {
      currentUrl: () => (view ? `${view.location.pathname}${view.location.search}${view.location.hash}` : '/'),
      load: (url: string) => view?.location.assign(url),
    };
  },
});

/** True when `url` is `base` itself or below it (`base/…`, `base?…`, `base#…`). */
function isAtOrBelow(url: string, base: string): boolean {
  return url === base || url.startsWith(`${base}/`) || url.startsWith(`${base}?`) || url.startsWith(`${base}#`);
}

/**
 * The sign-in routes, never a destination after signing in: the find page (`/login…`), a laboratory address
 * (`/lab/…`) and the Super Admin sign-in (`/admin/login…`), with their recovery pages.
 */
export function isLoginRoute(url: string): boolean {
  return isAtOrBelow(url, SIGN_IN_PATH) || isAtOrBelow(url, LABORATORY_PATH_PREFIX) || isAtOrBelow(url, ADMIN_SIGN_IN_PATH);
}

/** A page of the Super Admin portal (`/admin`, `/admin/…`); everything else belongs to the client portal. */
export function belongsToAdminPortal(url: string): boolean {
  return isAtOrBelow(url, '/admin');
}

/**
 * The return URL worth carrying to the sign-in page: a local path that is neither the sign-in routes themselves (B-11:
 * `returnUrl=%2Flogin` after a sign-out) nor the root (where the user lands anyway); `null` otherwise.
 */
export function meaningfulReturnUrl(url: string | null | undefined): string | null {
  if (!url || !url.startsWith('/') || url.startsWith('//') || url === '/' || isLoginRoute(url)) {
    return null;
  }

  return url;
}

/** A typed or pasted laboratory address, lower-cased and trimmed; a full address (`https://host/lab/abc`) gives its slug. */
export function normaliseLaboratorySlug(text: string): string {
  const value = text.trim().toLowerCase();
  const marker = value.lastIndexOf(`${LABORATORY_PATH_PREFIX}/`);
  const slug = marker < 0 ? value : value.slice(marker + LABORATORY_PATH_PREFIX.length + 1);
  return slug.replace(/[/?#].*$/, '');
}

/** The shape of a laboratory address (3–40 of `a-z 0-9 -`); whether it exists is the laboratory page's answer. */
export function isLaboratorySlug(slug: string): boolean {
  return LABORATORY_SLUG_PATTERN.test(slug);
}

/** `/lab/<slug>`: the sign-in address of a laboratory. */
export function laboratorySignInPath(slug: string): string {
  return `${LABORATORY_PATH_PREFIX}/${encodeURIComponent(slug)}`;
}

/**
 * The slug of a portal address (`https://host/lab/abc` → `abc`, the subscription wizard's `portalUrl`), or `null` when
 * the address is not a laboratory address of the expected shape.
 */
export function laboratorySlugOf(portalUrl: string | null | undefined): string | null {
  if (!portalUrl || !portalUrl.toLowerCase().includes(`${LABORATORY_PATH_PREFIX}/`)) {
    return null;
  }

  const slug = normaliseLaboratorySlug(portalUrl);
  return isLaboratorySlug(slug) ? slug : null;
}

/** What the sign-in address of a session is decided by: its tenant and its laboratory's slug. */
export type SignInAddressProfile = Pick<AuthProfile, 'tenantId' | 'portalSlug'>;

/**
 * The sign-in page of a session (LIS-CR-005): `/admin/login` for the platform, `/lab/<slug>` for a laboratory, the find
 * page `/login` when the session is unknown — unless the page the user wanted is a Super Admin page, which only the
 * Super Admin sign-in can lead back to.
 */
export function signInPathFor(profile: SignInAddressProfile | null, returnUrl: string | null = null): string {
  if (profile === null) {
    return returnUrl !== null && belongsToAdminPortal(returnUrl) ? ADMIN_SIGN_IN_PATH : SIGN_IN_PATH;
  }

  if (profile.tenantId === PLATFORM_TENANT_ID) {
    return ADMIN_SIGN_IN_PATH;
  }

  const slug = profile.portalSlug ?? null;
  return slug !== null && isLaboratorySlug(slug) ? laboratorySignInPath(slug) : SIGN_IN_PATH;
}

/**
 * The sign-in page of the session, for a full page load, with the return URL when it is meaningful and belongs to the
 * portal that address signs into (a laboratory address never carries a Super Admin page, and the other way round):
 * `/lab/abc?returnUrl=%2Fresults%2Fworklist`, `/admin/login`, `/login`.
 */
export function signInUrl(returnUrl: string | null = null, profile: SignInAddressProfile | null = null): string {
  const wanted = meaningfulReturnUrl(returnUrl);
  const path = signInPathFor(profile, wanted);
  const fits = wanted !== null && (path === SIGN_IN_PATH || belongsToAdminPortal(wanted) === (path === ADMIN_SIGN_IN_PATH));
  return fits ? `${path}?returnUrl=${encodeURIComponent(wanted)}` : path;
}

/** Why a session ended, when the sign-in page should say so (`?reason=idle`, validation F-01). */
export type SessionEndReason = 'idle';

/** The `reason` query value of the sign-in page after an idle end (F-01). */
export const IDLE_REASON: SessionEndReason = 'idle';

/** The session's own sign-in page with the notice "Your session ended after inactivity" (F-01): `/lab/abc?reason=idle`. */
export function idleSignInUrl(profile: SignInAddressProfile | null = null): string {
  return `${signInPathFor(profile)}?reason=${IDLE_REASON}`;
}
