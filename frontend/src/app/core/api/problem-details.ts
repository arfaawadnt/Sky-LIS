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
  /**
   * C-06: per field, the machine-readable rules that failed (`{ rule: 'MaximumLength', maxLength: 200 }`), so the SPA
   * shows its own localized text; absent on older answers and on problems without field errors.
   */
  readonly rules?: Readonly<Record<string, readonly FieldRule[]>> | null;
}

/** One failed validation rule of a field (the server's FluentValidation code without "Validator", plus numeric arguments). */
export interface FieldRule {
  readonly rule: string;
  readonly [argument: string]: string | number;
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
  /** F-01: the session was idle longer than its tenant's timeout; the sign-in page says so. */
  sessionIdleTimeout: 'AUTH-017',
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

function readRules(value: unknown): Readonly<Record<string, readonly FieldRule[]>> | null {
  if (!isRecord(value)) {
    return null;
  }

  const result: Record<string, readonly FieldRule[]> = {};
  for (const [key, rules] of Object.entries(value)) {
    if (!Array.isArray(rules)) {
      continue;
    }

    const read: FieldRule[] = [];
    for (const entry of rules) {
      if (isRecord(entry) && typeof entry['rule'] === 'string') {
        const rule: Record<string, string | number> = { rule: entry['rule'] };
        for (const [name, argument] of Object.entries(entry)) {
          if (typeof argument === 'number' && Number.isFinite(argument)) {
            rule[name] = argument;
          }
        }

        read.push(rule as FieldRule);
      }
    }

    result[key] = read;
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
      rules: readRules(body['rules']),
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
