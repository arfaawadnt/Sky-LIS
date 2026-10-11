import { DOCUMENT } from '@angular/common';
import { InjectionToken, inject } from '@angular/core';

/** The sign-in page; the only place a session ends up after it ends. */
export const SIGN_IN_PATH = '/login';

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

/** `/login`, `/login/recover`, `/login?…`: the sign-in routes, never a destination after signing in. */
export function isLoginRoute(url: string): boolean {
  return url === SIGN_IN_PATH || url.startsWith(`${SIGN_IN_PATH}/`) || url.startsWith(`${SIGN_IN_PATH}?`);
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

/** `/login` or `/login?returnUrl=…` for a full page load. */
export function signInUrl(returnUrl: string | null = null): string {
  const target = meaningfulReturnUrl(returnUrl);
  return target === null ? SIGN_IN_PATH : `${SIGN_IN_PATH}?returnUrl=${encodeURIComponent(target)}`;
}
