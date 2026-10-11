import { inject } from '@angular/core';
import { type ActivatedRouteSnapshot, type CanActivateChildFn, type CanActivateFn, Router, type RouterStateSnapshot, type UrlTree } from '@angular/router';

import { AuthStore } from '../core/auth/auth.store';
import { I18nService } from '../core/i18n/i18n.service';
import { PortalService } from '../core/portal/portal.service';
import { ToastService } from '../shared/ui/toast/toast.service';
import { isActivePath } from './nav.filter';
import { type NavEntry, NAV_REGISTRY, type Release, RELEASE_SCOPE, inReleaseScope } from './nav.registry';

/** The path of a URL, without query and fragment. */
function pathOf(url: string): string {
  return url.split('?')[0]?.split('#')[0] ?? url;
}

/** The registry entry a URL belongs to: the entry with the longest path that is the URL or one of its ancestors. */
export function registryEntryFor(url: string, entries: readonly NavEntry[] = NAV_REGISTRY): NavEntry | null {
  const path = pathOf(url);
  let best: NavEntry | null = null;
  for (const entry of entries) {
    if (isActivePath(entry, path) && (best === null || entry.path.length > best.path.length)) {
      best = entry;
    }
  }

  return best;
}

/** `/samples/…` → `/samples`; `/admin/library/…` → `/admin/library` (the Super Admin portal nests its modules one level deeper). */
function featureRoot(path: string): string {
  const segments = path.split('/').filter((segment) => segment.length > 0);
  const depth = segments[0] === 'admin' ? 2 : 1;
  return `/${segments.slice(0, depth).join('/')}`;
}

/** True when every registry entry of the URL's feature lies outside the delivered release (a Release-2 module, A-08). */
export function isOutOfScopeFeature(url: string, entries: readonly NavEntry[] = NAV_REGISTRY, scope: Release = RELEASE_SCOPE): boolean {
  const root = featureRoot(pathOf(url));
  if (root === '/' || root === '/admin') {
    return false;
  }

  const members = entries.filter((entry) => entry.path === root || entry.path.startsWith(`${root}/`));
  return members.length > 0 && members.every((entry) => !inReleaseScope(entry.release, scope));
}

/**
 * Whether the signed-in user may open the URL by the registry: its entry's permission is held (or `null`) and its
 * release is delivered; a URL without an entry passes unless its whole feature is outside the release.
 */
export function mayOpen(url: string, can: (permission: string) => boolean, entries: readonly NavEntry[] = NAV_REGISTRY, scope: Release = RELEASE_SCOPE): boolean {
  const entry = registryEntryFor(url, entries);
  if (entry === null) {
    return !isOutOfScopeFeature(url, entries, scope);
  }

  return inReleaseScope(entry.release, scope) && (entry.permission === null || can(entry.permission));
}

/** The notice a refused URL shows (R1-04); the shell dictionary is loaded at start-up, awaited here so no raw key shows. */
export const PAGE_NOT_AVAILABLE_KEY = 'shell.pageNotAvailable';

/**
 * A-08 / C-15: the address bar obeys the navigation. Placed as `canActivateChild` on the shell route (both portals), it
 * refuses a typed URL whose registry entry needs a permission the user lacks or belongs to a later release, and sends
 * the user to the portal's home instead with a short notice (R1-04: never a silent redirect). An action-only page
 * (`menu: false` or a later menu release, R1-01) is in scope and opens with its permission. Affordance only, like every
 * guard: the server refuses the data regardless.
 */
export const navRegistryGuard = ((_route: ActivatedRouteSnapshot, state: RouterStateSnapshot): true | UrlTree => {
  const auth = inject(AuthStore);
  if (!auth.isAuthenticated() || mayOpen(state.url, (permission) => auth.hasPermission(permission))) {
    return true;
  }

  const i18n = inject(I18nService);
  const toast = inject(ToastService);
  void i18n.load('shell').then(() => toast.warn(i18n.t(PAGE_NOT_AVAILABLE_KEY)));
  return inject(Router).createUrlTree([inject(PortalService).homePath()]);
}) satisfies CanActivateFn & CanActivateChildFn;
