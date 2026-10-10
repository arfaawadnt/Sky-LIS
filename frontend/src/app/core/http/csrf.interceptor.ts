import { HttpInterceptorFn } from '@angular/common/http';

import { isApiRequest } from './api-url';

export const CSRF_HEADER = 'X-Requested-With';

/** The value the server requires on the cookie-bearing endpoints (02 §2.3, `AuthOriginCheckMiddleware`). */
export const CSRF_HEADER_VALUE = 'Platform';

/**
 * Sends `X-Requested-With: Platform` on every API request (01 §6.4). A cross-site form cannot set a custom header,
 * and from a cross-origin script it forces a CORS preflight that fails because the API registers no CORS policy.
 * Only `/api/v1/auth/refresh` and `/sign-out` read a cookie, so only they enforce it; sending it everywhere costs
 * nothing and keeps the two calls identical to the rest.
 */
export const csrfInterceptor: HttpInterceptorFn = (req, next) =>
  isApiRequest(req.url) ? next(req.clone({ setHeaders: { [CSRF_HEADER]: CSRF_HEADER_VALUE } })) : next(req);
