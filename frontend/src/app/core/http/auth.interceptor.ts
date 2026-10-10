import { HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';

import { statusOf } from '../api/problem-details';
import { AuthStore } from '../auth/auth.store';
import { isApiRequest, isAuthEndpoint } from './api-url';

function withBearer<T>(req: HttpRequest<T>, token: string | null): HttpRequest<T> {
  return token === null ? req : req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
}

/**
 * Bearer from the memory signal on API requests; on a 401 from a data endpoint one single-flight refresh, then
 * the original request is replayed once with the new token (01 §6.4, D-01). Concurrent 401s share the same
 * refresh call. When the refresh fails the session is gone: state is cleared and the user is routed to sign-in.
 * The auth endpoints themselves are never retried.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!isApiRequest(req.url)) {
    return next(req);
  }

  const store = inject(AuthStore);
  const router = inject(Router);

  return next(withBearer(req, store.token())).pipe(
    catchError((error: unknown) => {
      if (statusOf(error) !== 401 || isAuthEndpoint(req.url)) {
        return throwError(() => error);
      }

      return from(store.refresh()).pipe(
        switchMap((token) => {
          if (token === null) {
            void router.navigate(['/login'], { queryParams: { returnUrl: router.url } });
            return throwError(() => error);
          }

          return next(withBearer(req, token));
        }),
      );
    }),
  );
};
