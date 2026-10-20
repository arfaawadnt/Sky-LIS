/** Audit contracts mirrored from `Platform.Contracts.Audit` (SRS CP-68 / SA-09; bootstrap until the OpenAPI generator lands, 01 §6.2). */

import type { Paged } from './paging';

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
  /** SA-09 "client" (UAT C-18): the laboratory the entry concerns, in the platform tenant's log only. */
  readonly clientTenantId?: string | null;
  readonly clientNameEn?: string | null;
  readonly clientNameAr?: string | null;
}

/** A client the platform log can be filtered by (`GET /api/v1/audit/clients`; empty in a laboratory). */
export interface AuditClientItem {
  readonly tenantId: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly portalSlug: string;
}

export type AuditEntriesPage = Paged<AuditEntryItem>;

export interface AuditEntryFilter {
  readonly from: string | null;
  readonly to: string | null;
  readonly userId: string | null;
  readonly entityType: string | null;
  readonly entityId: string | null;
  /** The platform log's client filter (SA-09); ignored in a laboratory. */
  readonly clientTenantId?: string | null;
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
  clients: '/api/v1/audit/clients',
} as const;

export const AUDIT_PERMISSIONS = {
  view: 'audit.view',
  export: 'audit.export',
} as const;
