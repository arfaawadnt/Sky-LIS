/** Reporting module contracts mirrored from `Platform.Contracts.Reporting` (bootstrap until the OpenAPI generator lands, 01 §6.2). */

/** How the work amount report groups the live service lines (`Platform.Domain.Reporting.WorkAmountGrouping`). */
export type WorkAmountGrouping = 'Branch' | 'User' | 'Payer' | 'ContractTier' | 'Doctor' | 'Day' | 'ClinicalGroup' | 'Service';

export const WORK_AMOUNT_GROUPINGS: readonly WorkAmountGrouping[] = ['Day', 'Payer', 'ContractTier', 'User', 'Doctor', 'ClinicalGroup', 'Service', 'Branch'];

/** One group of the report; `visits` are distinct within the group, `net` = `cashIncome` + `creditIncome`. */
export interface WorkAmountRow {
  readonly key: string;
  readonly labelEn: string;
  readonly labelAr: string;
  readonly code: string | null;
  readonly visits: number;
  readonly services: number;
  readonly gross: number;
  readonly discount: number;
  readonly cashIncome: number;
  readonly creditIncome: number;
  readonly net: number;
}

export interface WorkAmountLetterhead {
  readonly labNameEn: string;
  readonly labNameAr: string;
  readonly branchNameEn: string | null;
  readonly branchNameAr: string | null;
}

/** The report as served and printed: the period (ISO days, inclusive, after defaults), the grouping, the rows and the total. */
export interface WorkAmountReport {
  readonly fromDay: string;
  readonly toDay: string;
  readonly groupBy: WorkAmountGrouping;
  readonly branchId: string;
  readonly timeZone: string;
  readonly letterhead: WorkAmountLetterhead;
  readonly rows: readonly WorkAmountRow[];
  readonly totals: WorkAmountRow;
  readonly generatedBy: string;
  readonly generatedAtUtc: string;
}

/** The query of the report page: ISO days (empty = the server's default, today), the grouping and the branch (null = the session branch). */
export interface WorkAmountQuery {
  readonly from: string;
  readonly to: string;
  readonly groupBy: WorkAmountGrouping;
  readonly branchId: string | null;
}

export const REPORTING_API = {
  workAmount: '/api/v1/reports/work-amount',
  workAmountExport: '/api/v1/reports/work-amount/export',
} as const;

export const REPORTING_PERMISSIONS = {
  view: 'reporting.view',
  export: 'reporting.export',
} as const;
