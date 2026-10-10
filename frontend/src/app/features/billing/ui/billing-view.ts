import { ProblemCodes, ProblemError, type ProblemDetails } from '../../../core/api/problem-details';
import type { PaymentStatus } from '../../../core/api/billing.contracts';
import type { BadgeTone } from '../../../shared/ui/badge/status-tones';

/** The problem of a failed resource load, or null; a non-API failure reads as unexpected. */
export function problemOf(error: unknown): Pick<ProblemDetails, 'code'> | null {
  if (error === undefined || error === null) {
    return null;
  }

  return error instanceof ProblemError ? error.problem : { code: ProblemCodes.unexpected };
}

/** Badge tones of a payment's status (Posted counts, Voided does not). */
export function paymentTone(status: PaymentStatus): BadgeTone {
  return status === 'Posted' ? 'ok' : 'neutral';
}

/** A `yyyy-MM-dd` date filter as the start of that local day, as an ISO instant. */
export function dayStartIso(value: string): string | null {
  if (value === '') {
    return null;
  }

  const [year, month, day] = value.split('-').map(Number);
  if (year === undefined || month === undefined || day === undefined || [year, month, day].some(Number.isNaN)) {
    return null;
  }

  return new Date(year, month - 1, day, 0, 0, 0, 0).toISOString();
}

/** A `yyyy-MM-dd` date filter as the start of the following local day (an inclusive "to" date), as an ISO instant. */
export function dayEndIso(value: string): string | null {
  const start = dayStartIso(value);
  if (start === null) {
    return null;
  }

  const next = new Date(start);
  next.setDate(next.getDate() + 1);
  return next.toISOString();
}
