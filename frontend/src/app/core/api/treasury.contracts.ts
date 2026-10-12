/** Treasury module contracts mirrored from `Platform.Contracts.Treasury` (bootstrap until the OpenAPI generator lands, 01 §6.2). */

import type { SortRequest } from './paging';

/** Paging lives in `./paging`; re-exported so existing imports keep working. */
export type { Paged } from './paging';

export interface CreatedResource {
  readonly id: string;
}

export type TreasuryAccessStatus = 'Pending' | 'Approved' | 'Rejected' | 'Revoked' | 'Cancelled';

export const TREASURY_ACCESS_STATUSES: readonly TreasuryAccessStatus[] = ['Pending', 'Approved', 'Rejected', 'Revoked', 'Cancelled'];

export interface TreasuryListItem {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly branchId: string;
  readonly isActive: boolean;
  /** The branch code and names, read by the server from the Organization module (null when the branch is unknown; UAT A-04). */
  readonly branchCode: string | null;
  readonly branchNameEn: string | null;
  readonly branchNameAr: string | null;
}

export interface CreateTreasuryRequest {
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly branchId: string;
}

export interface RenameTreasuryRequest {
  readonly nameEn: string;
  readonly nameAr: string;
}

/** `sort`: `code`, `nameEn` or `nameAr` (B-03, server allow-list); absent = by code. */
export interface TreasuryListFilter extends SortRequest {
  /** Matches the code and both names on the server, whatever the UI language (UAT B-08); empty = no search. */
  readonly search: string;
  readonly isActive: boolean | null;
  readonly page: number;
  readonly pageSize: number;
}

export interface RequestTreasuryAccessRequest {
  readonly treasuryId: string;
  readonly deviceId: string;
  readonly deviceLabel: string | null;
}

export interface TreasuryAccessRequestListItem {
  readonly id: string;
  readonly number: string;
  readonly treasuryId: string;
  readonly treasuryCode: string;
  readonly treasuryNameEn: string;
  readonly treasuryNameAr: string;
  readonly branchId: string;
  readonly requestedByUserId: string;
  readonly requestedByName: string;
  readonly deviceId: string;
  readonly deviceLabel: string | null;
  readonly status: TreasuryAccessStatus;
  readonly requestedAtUtc: string;
  readonly decidedAtUtc: string | null;
  readonly decidedByName: string | null;
}

export interface TreasuryAccessRequestFilter {
  readonly status: TreasuryAccessStatus | null;
  readonly mine: boolean;
  readonly page: number;
  readonly pageSize: number;
}

export const TREASURY_API = {
  treasuries: '/api/v1/treasuries',
  requests: '/api/v1/treasury-access-requests',
} as const;

/** Permission keys of the module, as the catalogue defines them. */
export const TREASURY_PERMISSIONS = {
  view: 'treasury.view',
  create: 'treasury.create',
  approve: 'treasury.approve',
  cancel: 'treasury.cancel',
  manage: 'treasury.manage',
  /** CSV export of the treasuries register (UAT R1-05). */
  export: 'treasury.export',
} as const;
