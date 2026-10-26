/**
 * Lab Tracker contracts mirrored from `Platform.Contracts.Tracker` (LIS-CR-006 §0; bootstrap until the OpenAPI generator
 * lands, 01 §6.2). One row per visit of the laboratory day with its stage, next step and time left, all derived on the server.
 */

import type { SortRequest } from './paging';
import type { ReportLetterhead } from './results.contracts';

/** The stage of a visit, in the server's order of precedence (CR-006 §0). */
export type LabTrackerStage = 'Cancelled' | 'AwaitingSample' | 'InLab' | 'AwaitingVerification' | 'Ready' | 'Delivered';

/** The stages of the pipeline strip and the board lanes, in workflow order (Cancelled is counted, never a lane). */
export const TRACKER_LANES: readonly LabTrackerStage[] = ['AwaitingSample', 'InLab', 'AwaitingVerification', 'Ready', 'Delivered'];

/** What the visit needs next and, by its name, the module that does it. */
export type LabTrackerNextStep = 'CollectSample' | 'RecollectSample' | 'EnterResults' | 'ReviewResults' | 'VerifyResults' | 'TakePayment' | 'PrintReport' | 'None';

/** The "needs attention" filters. */
export type LabTrackerAttention = 'Delayed' | 'Panic' | 'Rejected' | 'ReadyUnpaid';

export const TRACKER_ATTENTION: readonly LabTrackerAttention[] = ['Delayed', 'Panic', 'Rejected', 'ReadyUnpaid'];

/** The desk presets: Reception (awaiting sample or a remaining patient share), Verification, Front desk (ready). */
export type LabTrackerView = 'Reception' | 'Verification' | 'FrontDesk';

/** The turnaround clock of the earliest unsigned result (`TurnaroundClock`). */
export type LabTrackerDelayState = 'OnTime' | 'Warning' | 'Late' | 'Done';

export type LabTrackerBalanceStatus = 'Paid' | 'Unpaid' | 'Partial' | 'RefundDue';

export interface LabTrackerRegistration {
  readonly lines: number;
  readonly cancelledLines: number;
}

/** Present only with `samples.view`. */
export interface LabTrackerSamples {
  readonly total: number;
  readonly ordered: number;
  readonly collected: number;
  readonly received: number;
  readonly rejected: number;
  /** The reason of the earliest rejection still waiting for a new sample (English / Arabic). */
  readonly firstRejectionReason: string | null;
  readonly firstRejectionReasonAr: string | null;
  /** Live samples collected outside the laboratory (LIS-CR-008 C-02): the cell shows "ext". */
  readonly collectedOutside: number;
}

/** Present only with `results.view`; counted over the results the user may see, `hidden` = the others. */
export interface LabTrackerResults {
  readonly total: number;
  readonly ordered: number;
  readonly complete: number;
  readonly reviewed: number;
  readonly verified: number;
  readonly printed: number;
  readonly hidden: number;
  readonly panic: number;
  readonly delta: number;
  /** The test of the first visible panic (English / Arabic). */
  readonly firstPanicTest: string | null;
  readonly firstPanicTestAr: string | null;
}

/** Present only with `billing.view`. */
export interface LabTrackerPayment {
  readonly status: LabTrackerBalanceStatus;
  readonly patientShare: number;
  readonly paid: number;
  readonly remaining: number;
}

export interface LabTrackerItem {
  readonly visitId: string;
  readonly accession: string;
  readonly patientId: string;
  readonly patientNumber: string | null;
  /** VIP masked by the server like the visits register ("VIP patient", patientMasked true, no Arabic name). */
  readonly patientName: string;
  readonly patientNameAr: string | null;
  readonly patientMasked: boolean;
  readonly gender: string | null;
  /** The age on the laboratory day in its largest whole unit: 34y, 5m, 12d. */
  readonly ageText: string | null;
  readonly urgent: boolean;
  readonly registeredAtUtc: string;
  readonly registeredByName: string | null;
  readonly payerName: string | null;
  readonly payerNameAr: string | null;
  readonly tierName: string | null;
  readonly tierNameAr: string | null;
  readonly branchId: string;
  readonly stage: LabTrackerStage;
  readonly nextStep: LabTrackerNextStep;
  /** Minutes to the earliest expected time of an unsigned visible result; negative = late; null = no clock. */
  readonly minutesLeft: number | null;
  readonly delayState: LabTrackerDelayState | null;
  readonly registration: LabTrackerRegistration;
  readonly samples: LabTrackerSamples | null;
  readonly results: LabTrackerResults | null;
  readonly payment: LabTrackerPayment | null;
}

/**
 * One live top-level service line of a visit (LIS-CR-014 T3, `?rows=services`): the visit's fields as `LabTrackerItem` carries
 * them, except `stage`, `nextStep`, `minutesLeft`, `delayState`, `samples` and `results`, which are the line's own (its tubes and
 * results, package members included); `visitStage` is the visit's; `payment` is the visit's balance.
 */
export interface LabTrackerServiceItem extends LabTrackerItem {
  readonly lineId: string;
  readonly serviceId: string;
  readonly serviceCode: string;
  readonly serviceName: string;
  readonly serviceNameAr: string;
  /** Service | Package. */
  readonly serviceKind: string;
  readonly visitStage: LabTrackerStage;
  /**
   * The worklist columns (change request of 2026-10-26: the service rows read like CP-18). The first live sample of the line and
   * the count of its live samples, its sample status (Rejected while a sample waits for a new one, else the least advanced) and
   * earliest collection: null / 0 without `samples.view`. The least advanced result and the clinical group: null without
   * `results.view`. The clock of the unsigned result that has consumed most of its turnaround (all signed: the one expected last,
   * measured at its verification, state Done): null without results.
   */
  readonly sampleNumber?: string | null;
  readonly sampleCount?: number;
  readonly sampleStatus?: string | null;
  readonly collectedAtUtc?: string | null;
  readonly resultStatus?: string | null;
  readonly groupNameEn?: string | null;
  readonly groupNameAr?: string | null;
  readonly expectedAtUtc?: string | null;
  readonly delaySeconds?: number | null;
  readonly consumedPercent?: number | null;
  readonly turnaroundState?: LabTrackerDelayState | null;
}

/** The two row modes of the table: one row per visit (default) or one per live top-level service line. */
export type LabTrackerRowsMode = 'Visits' | 'Services';

/** A service row (the `services` of a `rows=services` page) told from a visit row. */
export function isServiceRow(row: LabTrackerItem): row is LabTrackerServiceItem {
  return 'lineId' in row && typeof (row as Partial<LabTrackerServiceItem>).lineId === 'string';
}

/**
 * The counters of the whole filtered day, before the stage, attention and view filters (so the strip and the chips always show
 * the day): `total` = every visit of the day, one count per stage and one per attention flag.
 */
export interface LabTrackerCounters {
  readonly total: number;
  readonly cancelled: number;
  readonly awaitingSample: number;
  readonly inLab: number;
  readonly awaitingVerification: number;
  readonly ready: number;
  readonly delivered: number;
  readonly delayed: number;
  readonly panic: number;
  readonly rejected: number;
  readonly readyUnpaid: number;
}

export interface LabTrackerPage {
  readonly items: readonly LabTrackerItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly counters: LabTrackerCounters;
  /** The laboratory day read (yyyy-MM-dd, after the default applied; the first day of a range) and the branch. */
  readonly day: string;
  readonly branchId: string;
  /** The UTC window read, half-open. */
  readonly fromUtc: string;
  readonly toUtc: string;
  /** Visits (rows in `items`) or Services (LIS-CR-014 T3: rows in `services`, `items` empty, `totalCount` = service rows). */
  readonly rows?: LabTrackerRowsMode;
  readonly services?: readonly LabTrackerServiceItem[] | null;
  /** The tenant's delay thresholds (`delayWarnPercent`, `delayLatePercent`) for the service rows' legend. */
  readonly warnPercent?: number;
  readonly latePercent?: number;
}

/**
 * `GET /api/v1/tracker/visits` [registration.view]. `day` = laboratory day `yyyy-MM-dd` (absent = today in the tenant
 * time zone); `from` / `to` (together, laboratory wall-clock `yyyy-MM-ddTHH:mm`, inclusive to the minute, at most 31 days)
 * replace the day; `branchId` absent = the session branch. `sort`: `timeLeft` (default), `registeredAt`, `accession`,
 * `patient`, `stage`; with `rows=Services` also `service`, `status`, `expected`, `delay`.
 */
export interface LabTrackerQuery extends SortRequest {
  readonly day: string | null;
  readonly from: string | null;
  readonly to: string | null;
  readonly branchId: string | null;
  readonly stage: LabTrackerStage | null;
  readonly attention: LabTrackerAttention | null;
  readonly view: LabTrackerView | null;
  readonly search: string | null;
  readonly page: number;
  readonly pageSize: number;
  /** `Services` = one row per live top-level line (LIS-CR-014 T3; sort key `service` besides the others); absent = visits. */
  readonly rows?: LabTrackerRowsMode | null;
}

/** The counter of a stage. */
export function stageCount(counters: LabTrackerCounters, stage: LabTrackerStage): number {
  switch (stage) {
    case 'Cancelled':
      return counters.cancelled;
    case 'AwaitingSample':
      return counters.awaitingSample;
    case 'InLab':
      return counters.inLab;
    case 'AwaitingVerification':
      return counters.awaitingVerification;
    case 'Ready':
      return counters.ready;
    case 'Delivered':
      return counters.delivered;
  }
}

/** The counter of an attention flag. */
export function attentionCount(counters: LabTrackerCounters, attention: LabTrackerAttention): number {
  switch (attention) {
    case 'Delayed':
      return counters.delayed;
    case 'Panic':
      return counters.panic;
    case 'Rejected':
      return counters.rejected;
    case 'ReadyUnpaid':
      return counters.readyUnpaid;
  }
}

export const EMPTY_TRACKER_COUNTERS: LabTrackerCounters = {
  total: 0,
  cancelled: 0,
  awaitingSample: 0,
  inLab: 0,
  awaitingVerification: 0,
  ready: 0,
  delivered: 0,
  delayed: 0,
  panic: 0,
  rejected: 0,
  readyUnpaid: 0,
};

/**
 * The worklist report of the service rows of a filter (`GET …/visits/documents/worklist`, change request of 2026-10-26): the
 * letterhead, when and by whom it was printed, the period read and the rows in the list's order, at most 2 000.
 */
export interface LabTrackerWorklistDocument {
  readonly letterhead: ReportLetterhead;
  readonly generatedAtUtc: string;
  readonly generatedBy: string | null;
  readonly fromUtc: string;
  readonly toUtc: string;
  readonly rows: readonly LabTrackerServiceItem[];
  readonly truncated: boolean;
}

export const TRACKER_API = {
  visits: '/api/v1/tracker/visits',
  worklistDocument: '/api/v1/tracker/visits/documents/worklist',
} as const;

/** The pages a next step or a row action opens (CR-006 §3, LIS-CR-014 T1). */
export const TRACKER_LINKS = {
  accession: '/registration/accession',
  results: '/results/by-accession',
  payments: '/billing/payments',
  worklist: '/results/worklist',
  visits: '/registration/visits',
  patients: '/patients',
} as const;
