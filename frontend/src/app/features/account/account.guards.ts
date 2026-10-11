import { effect, inject, untracked } from '@angular/core';
import { type ActivatedRouteSnapshot, type CanActivateChildFn, type CanActivateFn, Router, type RouterStateSnapshot, type UrlTree } from '@angular/router';

import { AuthStore } from '../../core/auth/auth.store';
import { PortalService } from '../../core/portal/portal.service';
import { AccountSession } from './state/account-session.service';

/** The account module's addresses other parts of the shell link to. */
export const ACCOUNT_PATHS = {
  selectBranch: '/account/select-branch',
  dashboard: '/account/dashboard',
  profile: '/account/profile',
  wallet: '/account/wallet',
  support: '/account/support',
  audit: '/account/audit',
  /** SA-21: the Super Admin portal's own profile page. */
  adminProfile: '/admin/profile',
} as const;

/**
 * The "change branch" action (CP-04) for the shell's user menu: `router.navigateByUrl(changeBranchUrl(router, router.url))`.
 * `change=1` keeps the selection page open although the session already has a branch.
 */
export function changeBranchUrl(router: Router, returnUrl: string): UrlTree {
  return router.createUrlTree([ACCOUNT_PATHS.selectBranch], { queryParams: { change: 1, returnUrl } });
}

/**
 * CP-04: a client-portal session without a branch goes to branch selection first (every transaction is branch-stamped,
 * TENANT-004 otherwise). Place it as `canActivate` on client routes or `canActivateChild` on the shell route; the
 * selection page itself, the profile and the platform portal pass. Affordance only: the server refuses regardless.
 */
export const branchGuard = ((_route: ActivatedRouteSnapshot, state: RouterStateSnapshot): true | UrlTree => {
  const auth = inject(AuthStore);
  if (!auth.isAuthenticated() || inject(PortalService).portal() !== 'client' || auth.currentBranch() !== null) {
    return true;
  }

  const path = state.url.split('?')[0] ?? state.url;
  if (path === ACCOUNT_PATHS.selectBranch || path === ACCOUNT_PATHS.profile) {
    return true;
  }

  return inject(Router).createUrlTree([ACCOUNT_PATHS.selectBranch], { queryParams: { returnUrl: state.url } });
}) satisfies CanActivateFn & CanActivateChildFn;

/**
 * CP-38 "must change at next sign-in" (organization module sets the flag; this module enforces it): while the flag is
 * set, every page but the profile (where the password is changed) sends the user to the profile of the session's portal
 * (C-14: `/admin/profile` for a platform session, `/account/profile` for a laboratory one). Place it like
 * `branchGuard`. One profile call per session (cached by `AccountSession`); an unreadable profile lets the user pass.
 */
export const passwordChangeGuard = (async (_route: ActivatedRouteSnapshot, state: RouterStateSnapshot): Promise<true | UrlTree> => {
  const auth = inject(AuthStore);
  const session = inject(AccountSession);
  const router = inject(Router);
  if (!auth.isAuthenticated()) {
    return true;
  }

  const profilePath = inject(PortalService).isPlatform() ? ACCOUNT_PATHS.adminProfile : ACCOUNT_PATHS.profile;
  const path = state.url.split('?')[0] ?? state.url;
  if (path === profilePath) {
    return true;
  }

  const profile = await session.ensure();
  return profile?.mustChangePassword ? router.createUrlTree([profilePath], { queryParams: { mustChange: 1 } }) : true;
}) satisfies CanActivateFn & CanActivateChildFn;

/** True when the URL is the forced password change the guard above sends to (`?mustChange=1`). */
export function isForcedPasswordChange(router: Router, url: string): boolean {
  return router.parseUrl(url).queryParamMap.has('mustChange');
}

/**
 * R2-01: another tab of the same session changed the user's own profile (`AuthStore.profileRevision` moved): the cached
 * profile is dropped, and a tab held on the forced password change goes to its portal's home, through the guards again
 * (`passwordChangeGuard` re-reads the profile and sends it back only if the change is still due). Call once, in an
 * injection context that lives as long as the signed-in layout (the shell's constructor).
 */
export function followProfileChanges(): void {
  const auth = inject(AuthStore);
  const session = inject(AccountSession);
  const router = inject(Router);
  const portal = inject(PortalService);
  let seen = untracked(auth.profileRevision);
  effect(() => {
    const revision = auth.profileRevision();
    if (revision === seen) {
      return;
    }

    seen = revision;
    untracked(() => {
      session.invalidate();
      if (isForcedPasswordChange(router, router.url)) {
        void router.navigateByUrl(portal.homePath());
      }
    });
  });
}
