/** Samples module contracts mirrored from `Platform.Contracts.Samples` (bootstrap until the OpenAPI generator lands, 01 §6.2). */

export type { Paged } from './paging';

/** Status names as stored and filtered (intake LIS-INT-SMP-001 §6). */
export type SampleStatus = 'Ordered' | 'Collected' | 'Received' | 'Rejected' | 'Cancelled' | 'Replaced';

export const SAMPLE_STATUSES: readonly SampleStatus[] = ['Ordered', 'Collected', 'Received', 'Rejected', 'Cancelled', 'Replaced'];

/** Timeline actions (CP-17). */
export type SampleAction = 'Ordered' | 'Collected' | 'Received' | 'Rejected' | 'Unrejected' | 'Replaced' | 'ReplacementOrdered' | 'Cancelled';

/** CP-14 scan box: the phlebotomy scan collects, the unit scan receives. */
export type ScanMode = 'Collect' | 'Receive';

export interface SampleListItem {
  readonly id: string;
  readonly sampleNumber: string;
  readonly tubeId: string;
  readonly generation: number;
  readonly visitId: string;
  readonly accession: string;
  readonly patientId: string;
  readonly patientNumber: string | null;
  readonly patientName: string | null;
  readonly patientNameAr: string | null;
  readonly branchId: string;
  readonly clinicalGroupId: string;
  readonly unitCode: string | null;
  readonly clinicalGroupNameEn: string | null;
  readonly clinicalGroupNameAr: string | null;
  readonly sampleTypeId: string;
  readonly sampleTypeNameEn: string | null;
  readonly sampleTypeNameAr: string | null;
  readonly sampleConditionId: string | null;
  readonly sampleConditionNameEn: string | null;
  readonly sampleConditionNameAr: string | null;
  readonly serviceCount: number;
  readonly status: SampleStatus;
  readonly orderedAtUtc: string;
  readonly statusChangedAtUtc: string;
  readonly collectedAtUtc: string | null;
  readonly receivedAtUtc: string | null;
  readonly rejectionReasonId: string | null;
  readonly rejectionReasonNameEn: string | null;
  readonly rejectionReasonNameAr: string | null;
  readonly rejectionNote: string | null;
  readonly rejectedAtUtc: string | null;
  readonly replacedBySampleId: string | null;
  readonly replacesSampleId: string | null;
}

export interface SampleServiceView {
  readonly id: string;
  readonly code: string | null;
  readonly nameEn: string | null;
  readonly nameAr: string | null;
}

export interface SampleStepView {
  readonly atUtc: string;
  readonly userId: string | null;
  readonly userName: string | null;
  readonly branchId: string | null;
  readonly branchNameEn: string | null;
  readonly branchNameAr: string | null;
}

export interface SampleRejectionView {
  readonly reasonId: string;
  readonly reasonNameEn: string | null;
  readonly reasonNameAr: string | null;
  readonly note: string | null;
  readonly step: SampleStepView;
}

export interface SampleLinkView {
  readonly sampleId: string;
  readonly sampleNumber: string;
  readonly status: SampleStatus;
}

export interface SampleDetail {
  readonly summary: SampleListItem;
  readonly services: readonly SampleServiceView[];
  readonly collected: SampleStepView | null;
  readonly received: SampleStepView | null;
  readonly rejection: SampleRejectionView | null;
  readonly replacedBy: SampleLinkView | null;
  readonly replaces: SampleLinkView | null;
  readonly cancelledAtUtc: string | null;
  readonly cancellationReason: string | null;
  readonly isResultEntryAllowed: boolean;
}

export interface VisitSamplesActionResult {
  readonly visitId: string;
  readonly changed: number;
  readonly unchanged: number;
  readonly changedSampleNumbers: readonly string[];
}

/** One step of the CP-17 timeline; `sequence` is the order the steps happened (1, 2, 3 …). */
export interface SampleTimelineStep {
  readonly id: string;
  readonly sequence: number;
  readonly action: SampleAction;
  readonly status: SampleStatus;
  readonly atUtc: string;
  readonly userId: string | null;
  readonly userName: string | null;
  readonly branchId: string | null;
  readonly branchNameEn: string | null;
  readonly branchNameAr: string | null;
  readonly reasonId: string | null;
  readonly reasonNameEn: string | null;
  readonly reasonNameAr: string | null;
  readonly note: string | null;
  readonly sincePreviousSeconds: number | null;
}

export interface SampleStageDurations {
  readonly orderedToCollectedSeconds: number | null;
  readonly collectedToReceivedSeconds: number | null;
  readonly orderedToReceivedSeconds: number | null;
}

export interface SampleTimeline {
  readonly sample: SampleDetail;
  readonly steps: readonly SampleTimelineStep[];
  readonly durations: SampleStageDurations;
}

/** The data of a barcode label (CP-15); the SPA renders it with the print kit. */
export interface SampleLabelDocument {
  readonly sampleId: string;
  readonly sampleNumber: string;
  readonly accession: string;
  readonly unitCode: string | null;
  readonly clinicalGroupNameEn: string | null;
  readonly clinicalGroupNameAr: string | null;
  readonly sampleTypeId: string;
  readonly sampleTypeNameEn: string | null;
  readonly sampleTypeNameAr: string | null;
  readonly sampleConditionId: string | null;
  readonly sampleConditionNameEn: string | null;
  readonly sampleConditionNameAr: string | null;
  readonly patientId: string;
  readonly patientNumber: string | null;
  readonly patientName: string | null;
  readonly patientNameAr: string | null;
  readonly branchId: string;
  readonly branchNameEn: string | null;
  readonly branchNameAr: string | null;
  readonly labelAtUtc: string;
  readonly services: readonly SampleServiceView[];
  readonly copies: number;
}

export interface VisitLabelsDocument {
  readonly visitId: string;
  readonly accession: string;
  readonly labels: readonly SampleLabelDocument[];
}

export interface SampleSettingsView {
  readonly autoCollect: boolean;
  readonly autoReceive: boolean;
  readonly newBarcodeOnReject: boolean;
  readonly labelCopies: number;
}

export interface ScanSampleRequest {
  readonly sampleNumber: string;
  readonly mode: ScanMode;
}

export interface RejectSampleRequest {
  readonly reasonId: string;
  readonly note: string | null;
}

export interface SampleNoteRequest {
  readonly note: string | null;
}

export interface CreatedResource {
  readonly id: string;
}

/** Filters of `GET /api/v1/samples`; dates are ISO instants bounding the time the sample was ordered. */
export interface SampleListFilter {
  readonly visitId: string | null;
  readonly accession: string | null;
  readonly status: SampleStatus | null;
  readonly from: string | null;
  readonly to: string | null;
  readonly branchId: string | null;
  readonly clinicalGroupId: string | null;
  readonly search: string | null;
  readonly page: number;
  readonly pageSize: number;
}

/** A catalogue reason as `GET /api/v1/catalogue/reasons?kind=Rejection` returns it (Catalogue B, CP-53). */
export interface RejectionReasonOption {
  readonly id: string;
  readonly kind: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly isActive: boolean;
}

export const SAMPLES_API = {
  samples: '/api/v1/samples',
  scan: '/api/v1/samples/scan',
  rejectedToday: '/api/v1/samples/rejected-today',
  settings: '/api/v1/samples/settings',
  byNumber: (sampleNumber: string): string => `/api/v1/samples/by-number/${encodeURIComponent(sampleNumber)}`,
  sample: (id: string): string => `/api/v1/samples/${encodeURIComponent(id)}`,
  timeline: (id: string): string => `/api/v1/samples/${encodeURIComponent(id)}/timeline`,
  label: (id: string): string => `/api/v1/samples/${encodeURIComponent(id)}/documents/label`,
  reject: (id: string): string => `/api/v1/samples/${encodeURIComponent(id)}/reject`,
  unreject: (id: string): string => `/api/v1/samples/${encodeURIComponent(id)}/unreject`,
  replace: (id: string): string => `/api/v1/samples/${encodeURIComponent(id)}/replace`,
  collectAll: (visitId: string): string => `/api/v1/samples/visits/${encodeURIComponent(visitId)}/collect-all`,
  receiveAll: (visitId: string): string => `/api/v1/samples/visits/${encodeURIComponent(visitId)}/receive-all`,
  visitLabels: (visitId: string): string => `/api/v1/samples/visits/${encodeURIComponent(visitId)}/documents/labels`,
  /** Catalogue B endpoint (agreed contract); only rejection reasons are asked for. */
  rejectionReasons: '/api/v1/catalogue/reasons',
} as const;

/**
 * In-app links of the module, for other pages too (worklist and results link to tracking, registration to the
 * samples of a visit). The job order is printed by Registration; its page path lives here so it changes in one place.
 */
export const SAMPLES_LINKS = {
  collection: '/samples/collection',
  rejections: '/samples/rejections',
  track: (sampleNumber: string): string => `/samples/track/${encodeURIComponent(sampleNumber)}`,
  visitByAccession: (accession: string): string => `/samples/visits?accession=${encodeURIComponent(accession)}`,
  jobOrder: (visitId: string): string => `/registration/visits/${encodeURIComponent(visitId)}/job-order`,
} as const;

/** Permission keys of the module, as the catalogue defines them. */
export const SAMPLES_PERMISSIONS = {
  view: 'samples.view',
  create: 'samples.create',
  edit: 'samples.edit',
  cancel: 'samples.cancel',
  manage: 'samples.manage',
} as const;
