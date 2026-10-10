import { HttpErrorResponse } from '@angular/common/http';

/** The one error shape of the API (01 §5.1): `application/problem+json` with a stable `code`. */
export interface ProblemDetails {
  readonly type: string;
  readonly title: string;
  readonly status: number;
  readonly code: string;
  readonly traceId: string | null;
  readonly correlationId: string | null;
  readonly errors: Readonly<Record<string, readonly string[]>> | null;
}

/** Codes the client reacts to (the server's `ProblemCodes`). */
export const ProblemCodes = {
  authenticatedActorRequired: 'AUTH-001',
  invalidCredentials: 'AUTH-002',
  accountLocked: 'AUTH-003',
  accountInactive: 'AUTH-004',
  noActiveMembership: 'AUTH-005',
  sessionRevokedOrExpired: 'AUTH-006',
  refreshTokenRejected: 'AUTH-012',
  stepUpRequired: 'AUTH-STEPUP-REQUIRED',
  mfaEnrollmentRequired: 'MFA-ENROLL-REQUIRED',
  credentialEnvelopeMalformed: 'CRED-001',
  credentialChallengeExpired: 'CRED-002',
  credentialReplayed: 'CRED-003',
  credentialRetargeted: 'CRED-004',
  credentialPlaintext: 'CRED-005',
  rateLimited: 'RATE-429',
  concurrencyConflict: 'CONCURRENCY-409',
  validationFailed: 'VALIDATION-400',
  unexpected: 'INTERNAL-500',
  network: 'NETWORK-000',
} as const;

/** A failed HTTP call as the application sees it: the typed problem plus the status, never the raw body. */
export class ProblemError extends Error {
  constructor(
    readonly problem: ProblemDetails,
    readonly status: number,
  ) {
    super(problem.title);
    this.name = 'ProblemError';
  }

  get code(): string {
    return this.problem.code;
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function stringOrNull(value: unknown): string | null {
  return typeof value === 'string' ? value : null;
}

function readErrors(value: unknown): Readonly<Record<string, readonly string[]>> | null {
  if (!isRecord(value)) {
    return null;
  }

  const result: Record<string, readonly string[]> = {};
  for (const [key, messages] of Object.entries(value)) {
    if (Array.isArray(messages)) {
      result[key] = messages.filter((m): m is string => typeof m === 'string');
    }
  }

  return result;
}

/** Maps any HTTP failure to a `ProblemDetails`; a body that is not a problem document becomes a generic one. */
export function toProblemDetails(error: HttpErrorResponse): ProblemDetails {
  const body: unknown = error.error;
  if (isRecord(body) && typeof body['code'] === 'string') {
    return {
      type: stringOrNull(body['type']) ?? 'about:blank',
      title: stringOrNull(body['title']) ?? error.statusText,
      status: typeof body['status'] === 'number' ? body['status'] : error.status,
      code: body['code'],
      traceId: stringOrNull(body['traceId']),
      correlationId: stringOrNull(body['correlationId']),
      errors: readErrors(body['errors']),
    };
  }

  return {
    type: 'about:blank',
    title: error.status === 0 ? 'The server could not be reached.' : error.statusText || 'Request failed.',
    status: error.status,
    code: error.status === 0 ? ProblemCodes.network : error.status >= 500 ? ProblemCodes.unexpected : `HTTP-${error.status}`,
    traceId: null,
    correlationId: null,
    errors: null,
  };
}

export function isProblem(error: unknown, code: string): boolean {
  return error instanceof ProblemError && error.code === code;
}

/** The HTTP status of a failure, whether it was already typed or is still the raw response. */
export function statusOf(error: unknown): number | null {
  if (error instanceof ProblemError) {
    return error.status;
  }

  if (error instanceof HttpErrorResponse) {
    return error.status;
  }

  return null;
}
