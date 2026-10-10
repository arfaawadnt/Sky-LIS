import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { AuthStore } from './auth.store';

/** Protected routes (01 §6.5). Affordance: the API refuses an unauthenticated request regardless (D-04). */
export const authGuard: CanActivateFn = (_route, state) => {
  const store = inject(AuthStore);
  if (store.isAuthenticated()) {
    return true;
  }

  return inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** The sign-in page sends an authenticated user back to the application. */
export const anonymousGuard: CanActivateFn = () => {
  const store = inject(AuthStore);
  return store.isAuthenticated() ? inject(Router).createUrlTree(['/']) : true;
};
