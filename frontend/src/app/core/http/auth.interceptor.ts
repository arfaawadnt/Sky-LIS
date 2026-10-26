import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';

import { statusOf } from '../api/problem-details';
import { AuthStore } from '../auth/auth.store';
import { signInUrl } from '../auth/session-navigation';
import { isApiRequest, isAuthEndpoint } from './api-url';

function withBearer<T>(req: HttpRequest<T>, token: string | null): HttpRequest<T> {
  return token === null ? req : req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
}

/**
 * Bearer from the memory signal on API requests; on a 401 from a data endpoint one single-flight refresh, then
 * the original request is replayed once with the new token (01 §6.4, D-01). Concurrent 401s share the same
 * refresh call. The auth endpoints themselves are never retried.
 *
 * A replay happens only when the refresh kept the tab's user and tenant (C-01): the refresh cookie is shared by the
 * tabs of the browser, and a request built for one user must never be sent as another. When the refresh fails or
 * returns another identity, the store clears the session and reloads the sign-in page itself (full page load, A-01);
 * the original error is surfaced and nothing is replayed. A 401 without a session (anonymous page) routes to sign-in.
 * LIS-CR-014 G1: the refresh behind a replay is the tab's own portal's (`AuthStore.refresh` asks `?portal=admin` on a Super
 * Admin page), so a 401 in one portal never rotates or adopts the other portal's session.
 *
 * Once the tab's session has ended and the sign-in page is loading (`AuthStore.hasEnded`, round 2 R2-05), no API request
 * leaves the browser: it fails locally (status 0) instead of reaching the server without a token and being refused with 401.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!isApiRequest(req.url)) {
    return next(req);
  }

  const store = inject(AuthStore);
  if (store.hasEnded()) {
    return throwError(() => new HttpErrorResponse({ url: req.url, status: 0, statusText: 'Session ended' }));
  }

  const router = inject(Router);
  const sentAs = store.currentUser()?.id ?? null;
  const sentFor = store.currentTenant();

  return next(withBearer(req, store.token())).pipe(
    catchError((error: unknown) => {
      if (statusOf(error) !== 401 || isAuthEndpoint(req.url)) {
        return throwError(() => error);
      }

      const hadSession = sentAs !== null || store.profile() !== null;
      return from(store.refresh()).pipe(
        switchMap((token) => {
          if (token === null) {
            if (!hadSession) {
              // LIS-CR-005: the find page, or the Super Admin sign-in for a Super Admin page
              void router.navigateByUrl(signInUrl(router.url));
            }

            return throwError(() => error);
          }

          // defence in depth: the store refuses a changed identity; a request built for another one is never replayed
          if (sentAs !== null && (store.currentUser()?.id !== sentAs || store.currentTenant() !== sentFor)) {
            return throwError(() => error);
          }

          return next(withBearer(req, token));
        }),
      );
    }),
  );
};
