import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';

import { ProblemCodes, ProblemError, toProblemDetails } from '../api/problem-details';
import { isApiRequest } from './api-url';

/**
 * Turns every failed API response into a typed `ProblemError` (01 §6.4) and routes the two cases the whole
 * application handles the same way: a forced MFA enrolment goes to the enrolment page; a required step-up is left
 * to the caller, which shows the re-authenticate dialog and retries. Everything else reaches the feature as a code.
 */
export const problemDetailsInterceptor: HttpInterceptorFn = (req, next) => {
  if (!isApiRequest(req.url)) {
    return next(req);
  }

  const router = inject(Router);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      const problem = toProblemDetails(error);
      if (problem.code === ProblemCodes.mfaEnrollmentRequired) {
        void router.navigate(['/login/mfa-enrollment']);
      }

      return throwError(() => new ProblemError(problem, error.status));
    }),
  );
};
