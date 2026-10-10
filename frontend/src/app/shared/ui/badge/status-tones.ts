/** The colour tones of a badge (prototype `.badge.*`). */
export type BadgeTone = 'neutral' | 'info' | 'ok' | 'warn' | 'bad' | 'violet';

/**
 * The one map from the common status names of the SRS status models to a tone, so every list colours
 * `Pending`/`Approved`/`Verified`/`Paid`… the same way. Unknown statuses are neutral.
 */
export const STATUS_TONES: Readonly<Record<string, BadgeTone>> = {
  // approvals (treasury access, requests)
  Pending: 'warn',
  Approved: 'ok',
  Rejected: 'bad',
  Revoked: 'bad',
  Cancelled: 'neutral',
  // master data and accounts
  Active: 'ok',
  Inactive: 'neutral',
  Locked: 'bad',
  // samples
  Ordered: 'info',
  Collected: 'violet',
  Received: 'ok',
  Replaced: 'warn',
  Merged: 'neutral',
  RefundDue: 'warn',
  Voided: 'bad',
  Posted: 'ok',
  Resolved: 'ok',
  InProgress: 'warn',
  // results
  Complete: 'info',
  Reviewed: 'violet',
  Verified: 'ok',
  Printed: 'neutral',
  // claims, closings, tickets
  Draft: 'neutral',
  Issued: 'ok',
  Open: 'info',
  Closed: 'neutral',
  Completed: 'ok',
  // bookings
  Booked: 'info',
  Converted: 'ok',
  // balances
  Unpaid: 'bad',
  Partial: 'warn',
  Paid: 'ok',
  // visits and lines
  Reserved: 'violet',
  Urgent: 'bad',
  Panic: 'bad',
};

export function toneOf(status: string | null | undefined): BadgeTone {
  return status ? (STATUS_TONES[status] ?? 'neutral') : 'neutral';
}
