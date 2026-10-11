import { type HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { tap } from 'rxjs';

import { ACCOUNT_API } from '../api/account.contracts';
import { pathOf } from '../api/credential-fields';
import { AuthStore } from '../auth/auth.store';

/** The requests that change the signed-in user's own profile: the password change and the language preference. */
const PROFILE_CHANGES: readonly string[] = [ACCOUNT_API.password, ACCOUNT_API.preferences];

/** True for a write to one of the own-profile endpoints. */
export function isProfileChange(method: string, url: string): boolean {
  return method !== 'GET' && PROFILE_CHANGES.includes(pathOf(url));
}

/**
 * R2-01: after a successful change of the user's own profile, the other tabs of the session are told (`profile-changed`
 * on the session channel), so a tab held on the forced password change leaves it without a reload. One place for every
 * caller of those endpoints; a refused change announces nothing.
 */
export const profileChangeInterceptor: HttpInterceptorFn = (req, next) => {
  if (!isProfileChange(req.method, req.url)) {
    return next(req);
  }

  const auth = inject(AuthStore);
  return next(req).pipe(
    tap((event) => {
      if (event instanceof HttpResponse && event.ok) {
        auth.announceProfileChange();
      }
    }),
  );
};
