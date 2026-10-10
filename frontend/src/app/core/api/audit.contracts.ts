/** Audit contracts mirrored from `Platform.Contracts.Audit` (SRS CP-68 / SA-09; bootstrap until the OpenAPI generator lands, 01 §6.2). */

import type { Paged } from './treasury.contracts';

export type AuditEntryKind = 'FieldChange' | 'Event';

export interface FieldChangeItem {
  readonly field: string;
  readonly before: string | null;
  readonly after: string | null;
}

/**
 * One line of the unified audit log: a field-level change (`kind === 'FieldChange'`: `changes` and `reason` are set)
 * or a domain event of the tamper-evident trail (`kind === 'Event'`: `sequence` and `payload` are set).
 */
export interface AuditEntryItem {
  readonly id: string;
  readonly kind: AuditEntryKind;
  readonly atUtc: string;
  readonly actorUserId: string | null;
  readonly actorName: string | null;
  readonly entityType: string;
  readonly entityId: string;
  /** `Created | Modified | Deleted` for a field change; the event name (`TreasuryCreated`) for an event. */
  readonly action: string;
  readonly correlationId: string | null;
  readonly changes: readonly FieldChangeItem[] | null;
  readonly reason: string | null;
  readonly sequence: number | null;
  readonly payload: string | null;
}

export type AuditEntriesPage = Paged<AuditEntryItem>;

export interface AuditEntryFilter {
  readonly from: string | null;
  readonly to: string | null;
  readonly userId: string | null;
  readonly entityType: string | null;
  readonly entityId: string | null;
  readonly page: number;
  readonly pageSize: number;
}

export interface ChainVerificationResult {
  readonly ok: boolean;
  readonly checked: number;
  readonly firstBrokenSequence: number | null;
}

export const AUDIT_API = {
  entries: '/api/v1/audit/entries',
  verifyChain: '/api/v1/audit/verify-chain',
} as const;

export const AUDIT_PERMISSIONS = {
  view: 'audit.view',
  export: 'audit.export',
} as const;
