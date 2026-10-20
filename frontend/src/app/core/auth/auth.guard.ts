import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { AuthStore } from './auth.store';
import { signInUrl } from './session-navigation';

/**
 * Protected routes (01 §6.5). Affordance: the API refuses an unauthenticated request regardless (D-04). LIS-CR-005: an
 * anonymous user goes to the find page `/login` with the return URL — or to the Super Admin sign-in when the page asked for
 * is a Super Admin page (`/admin/…`), the only address that can lead back to it.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const store = inject(AuthStore);
  if (store.isAuthenticated()) {
    return true;
  }

  return inject(Router).parseUrl(signInUrl(state.url));
};

/** The sign-in page sends an authenticated user back to the application. */
export const anonymousGuard: CanActivateFn = () => {
  const store = inject(AuthStore);
  return store.isAuthenticated() ? inject(Router).createUrlTree(['/']) : true;
};
