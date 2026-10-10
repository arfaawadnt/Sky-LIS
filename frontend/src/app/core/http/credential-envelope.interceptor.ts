import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError, type Observable } from 'rxjs';

import { credentialFieldsFor, pathOf } from '../api/credential-fields';
import { ProblemCodes, isProblem } from '../api/problem-details';
import { CredentialEnvelopeClient } from '../crypto/credential-envelope.client';

function isPlainObject(value: unknown): value is Readonly<Record<string, unknown>> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/**
 * Seals every `[CredentialField]` property of a request body before it leaves the browser (01 §6.4, D-03): one
 * challenge per request, the same nonce on every field, the audience bound to the exact method and path. An
 * expired or replayed challenge (`CRED-002`, `CRED-003`) is retried once with a fresh challenge; anything else
 * is the caller's problem.
 */
export const credentialEnvelopeInterceptor: HttpInterceptorFn = (req, next) => {
  const fields = credentialFieldsFor(req.method, req.url);
  const body = req.body;
  if (fields.length === 0 || !isPlainObject(body)) {
    return next(req);
  }

  const client = inject(CredentialEnvelopeClient);
  const audience = `${req.method.toUpperCase()} ${pathOf(req.url)}`;
  const attempt = (): Observable<ReturnType<typeof next> extends Observable<infer T> ? T : never> =>
    from(client.sealFields(body, fields, audience)).pipe(switchMap((sealed) => next(req.clone({ body: sealed }))));

  return attempt().pipe(
    catchError((error: unknown) =>
      isProblem(error, ProblemCodes.credentialChallengeExpired) || isProblem(error, ProblemCodes.credentialReplayed) ? attempt() : throwError(() => error),
    ),
  );
};
