/**
 * Lab Tracker contracts mirrored from `Platform.Contracts.Tracker` (LIS-CR-006 §0; bootstrap until the OpenAPI generator
 * lands, 01 §6.2). One row per visit of the laboratory day with its stage, next step and time left, all derived on the server.
 */

import type { SortRequest } from './paging';

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
  /** The laboratory day read (yyyy-MM-dd, after the default applied) and the branch. */
  readonly day: string;
  readonly branchId: string;
}

/**
 * `GET /api/v1/tracker/visits` [registration.view]. `day` = laboratory day `yyyy-MM-dd` (absent = today in the tenant
 * time zone); `branchId` absent = the session branch. `sort`: `timeLeft` (default), `registeredAt`, `accession`,
 * `patient`, `stage`.
 */
export interface LabTrackerQuery extends SortRequest {
  readonly day: string | null;
  readonly branchId: string | null;
  readonly stage: LabTrackerStage | null;
  readonly attention: LabTrackerAttention | null;
  readonly view: LabTrackerView | null;
  readonly search: string | null;
  readonly page: number;
  readonly pageSize: number;
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

export const TRACKER_API = {
  visits: '/api/v1/tracker/visits',
} as const;

/** The pages a next step opens (CR-006 §3). */
export const TRACKER_LINKS = {
  accession: '/registration/accession',
  results: '/results/by-accession',
  payments: '/billing/payments',
  worklist: '/results/worklist',
} as const;
