import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { AuthStore } from './auth.store';
import { SIGN_IN_PATH, meaningfulReturnUrl } from './session-navigation';

/** Protected routes (01 §6.5). Affordance: the API refuses an unauthenticated request regardless (D-04). */
export const authGuard: CanActivateFn = (_route, state) => {
  const store = inject(AuthStore);
  if (store.isAuthenticated()) {
    return true;
  }

  const returnUrl = meaningfulReturnUrl(state.url);
  return inject(Router).createUrlTree([SIGN_IN_PATH], returnUrl === null ? {} : { queryParams: { returnUrl } });
};

/** The sign-in page sends an authenticated user back to the application. */
export const anonymousGuard: CanActivateFn = () => {
  const store = inject(AuthStore);
  return store.isAuthenticated() ? inject(Router).createUrlTree(['/']) : true;
};
