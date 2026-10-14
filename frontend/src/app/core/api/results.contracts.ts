/** Results module contracts mirrored from `Platform.Contracts.Results` (bootstrap until the OpenAPI generator lands, 01 §6.2). */

import type { SortRequest } from './paging';

export type { Paged } from './paging';

/** The test track (intake LIS-INT-RES-001 §6). */
export type ResultStatus = 'Ordered' | 'Complete' | 'Reviewed' | 'Verified' | 'Printed' | 'Cancelled';

export const RESULT_STATUSES: readonly ResultStatus[] = ['Ordered', 'Complete', 'Reviewed', 'Verified', 'Printed', 'Cancelled'];

/** The statuses a user filters on (Cancelled results are never listed). */
export const RESULT_FILTER_STATUSES: readonly ResultStatus[] = ['Ordered', 'Complete', 'Reviewed', 'Verified', 'Printed'];

export type ResultFlag = 'Normal' | 'Low' | 'High' | 'CriticalLow' | 'CriticalHigh' | 'Abnormal';

export type TestType = 'Numeric' | 'NonNumeric' | 'OptionalList' | 'RestrictedList' | 'Formula' | 'FormattedText' | 'Culture';

export type DelayState = 'OnTime' | 'Warning' | 'Late' | 'Done';

export type SampleStatusName = 'Ordered' | 'Collected' | 'Received' | 'Rejected' | 'Cancelled' | 'Replaced';

export const SAMPLE_FILTER_STATUSES: readonly SampleStatusName[] = ['Ordered', 'Collected', 'Received', 'Rejected'];

// ---- Entry --------------------------------------------------------------------------------------------------------

export interface FormattedParagraphDto {
  readonly title: string;
  readonly text: string;
}

export interface CultureSensitivityEntryDto {
  readonly antibioticId: string;
  readonly interpretation: string;
  readonly mic: string | null;
}

export interface CultureIsolateEntryDto {
  readonly organismId: string;
  readonly quantity: string | null;
  readonly comment: string | null;
  readonly sensitivities: readonly CultureSensitivityEntryDto[];
}

export interface CultureEntryDto {
  readonly noGrowth: boolean;
  readonly isolates: readonly CultureIsolateEntryDto[];
}

export interface ResultValueItem {
  readonly resultId: string;
  readonly numericValue: number | null;
  readonly textValue: string | null;
  readonly paragraphs: readonly FormattedParagraphDto[] | null;
  readonly culture: CultureEntryDto | null;
}

export interface ResultRefusal {
  readonly resultId: string;
  readonly code: string;
  readonly message: string;
}

export interface BulkResultOutcome {
  readonly changed: readonly string[];
  readonly refused: readonly ResultRefusal[];
}

// ---- Case of a visit (CP-19) ------------------------------------------------------------------------------------------

export interface VisitHeaderView {
  readonly visitId: string;
  readonly accession: string;
  readonly registeredAtUtc: string;
  readonly branchId: string;
  readonly branchNameEn: string | null;
  readonly branchNameAr: string | null;
  readonly payerId: string | null;
  readonly payerNameEn: string | null;
  readonly payerNameAr: string | null;
  readonly doctorId: string | null;
  readonly doctorNameEn: string | null;
  readonly doctorNameAr: string | null;
  readonly urgent: boolean;
}

export interface PatientHeaderView {
  readonly patientId: string;
  readonly number: string | null;
  readonly fullName: string | null;
  readonly nameAr: string | null;
  readonly gender: string | null;
  readonly dateOfBirth: string | null;
  readonly ageYears: number | null;
  readonly ageMonths: number | null;
  readonly ageDays: number | null;
  readonly patientConditionId: string | null;
  readonly conditionNameEn: string | null;
  readonly conditionNameAr: string | null;
  readonly isVip: boolean;
  /** A VIP patient shown to a user without the See-VIP right: "VIP patient", no Arabic name or date of birth (F-06); printing is refused (RES-056). */
  readonly isMasked?: boolean;
}

export interface UnitSampleView {
  readonly tubeId: string;
  readonly sampleNumber: string | null;
  readonly status: string | null;
  readonly collectedAtUtc: string | null;
  readonly receivedAtUtc: string | null;
}

export interface ReportUnitRightsView {
  readonly canEdit: boolean;
  readonly canReview: boolean;
  readonly canVerify: boolean;
  readonly canPrint: boolean;
  readonly canModifyVerified: boolean;
  readonly canPreviewWithoutVerify: boolean;
}

export interface ServiceBannerView {
  readonly serviceId: string;
  readonly code: string | null;
  readonly nameEn: string | null;
  readonly nameAr: string | null;
  readonly reportName: string | null;
  readonly status: ResultStatus;
  readonly resultCount: number;
}

export interface RangeView {
  readonly low: number | null;
  readonly high: number | null;
  readonly panicLow: number | null;
  readonly panicHigh: number | null;
  readonly reportableLow: number | null;
  readonly reportableHigh: number | null;
  readonly text: string | null;
  readonly display: string;
}

export interface DeltaView {
  readonly flag: boolean;
  readonly percent: number | null;
  readonly previousValue: number | null;
  readonly previousResultId: string | null;
  readonly acknowledged: boolean;
  readonly acknowledgedByName: string | null;
  readonly acknowledgedAtUtc: string | null;
  readonly comment: string | null;
}

export interface ActorStampView {
  readonly userId: string | null;
  readonly name: string | null;
  readonly atUtc: string;
}

export interface ListOptionView {
  readonly value: string;
  readonly isNormal: boolean;
  readonly isCritical: boolean;
  readonly isPlaceholder: boolean;
  readonly colour: string | null;
}

export interface ParagraphTemplateView {
  readonly title: string;
  readonly text: string | null;
}

export interface CultureSensitivityView {
  readonly antibioticId: string;
  readonly code: string;
  readonly abbreviation: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly interpretation: string;
  readonly mic: string | null;
}

export interface CultureIsolateView {
  readonly organismId: string;
  readonly organismCode: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly quantity: string | null;
  readonly comment: string | null;
  readonly sensitivities: readonly CultureSensitivityView[];
}

export interface CultureView {
  readonly noGrowth: boolean;
  readonly isolates: readonly CultureIsolateView[];
}

export interface ResultRowView {
  readonly id: string;
  readonly lineId: string;
  readonly serviceId: string;
  readonly testId: string;
  readonly testCode: string | null;
  readonly testNameEn: string | null;
  readonly testNameAr: string | null;
  readonly reportName: string | null;
  readonly testType: TestType;
  readonly isHiddenHelper: boolean;
  readonly isFormula: boolean;
  readonly tubeId: string | null;
  readonly sampleNumber: string | null;
  readonly sampleStatus: string | null;
  readonly sampleReceived: boolean;
  readonly status: ResultStatus;
  readonly numericValue: number | null;
  readonly textValue: string | null;
  readonly paragraphs: readonly FormattedParagraphDto[] | null;
  readonly culture: CultureView | null;
  readonly unit: string | null;
  readonly decimals: number;
  readonly range: RangeView | null;
  readonly flag: ResultFlag | null;
  readonly isPanic: boolean;
  readonly delta: DeltaView | null;
  readonly source: 'Manual' | 'Analyser';
  readonly analyserCode: string | null;
  readonly analyserFlags: string | null;
  readonly entered: ActorStampView | null;
  readonly reviewed: ActorStampView | null;
  readonly verified: ActorStampView | null;
  readonly verifiedByName: string | null;
  readonly printCount: number;
  readonly lastPrintedAtUtc: string | null;
  readonly amendmentCount: number;
  readonly reportComment: string | null;
  readonly internalNote: string | null;
  readonly listOptions: readonly ListOptionView[];
  readonly paragraphTemplates: readonly ParagraphTemplateView[];
  readonly expectedAtUtc: string;
}

export interface ReportUnitView {
  readonly key: string;
  readonly clinicalGroupId: string;
  readonly groupCode: string | null;
  readonly groupNameEn: string | null;
  readonly groupNameAr: string | null;
  readonly printAloneServiceId: string | null;
  readonly serviceNameEn: string | null;
  readonly serviceNameAr: string | null;
  readonly status: ResultStatus;
  readonly isPrinted: boolean;
  readonly printCount: number;
  readonly hasMissingValues: boolean;
  readonly hasPanic: boolean;
  readonly hasDelta: boolean;
  readonly samples: readonly UnitSampleView[];
  readonly rights: ReportUnitRightsView;
  readonly services: readonly ServiceBannerView[];
  readonly results: readonly ResultRowView[];
  /** Results of this unit outside the user's result rights (a print-alone service spanning clinical groups), and their groups. */
  readonly hiddenResults: number;
  readonly hiddenGroups: readonly LookupOption[];
}

export interface ResultSettingsView {
  readonly reviewBeforeVerify: boolean;
  readonly printUnverified: boolean;
  readonly resultLanguage: string;
}

export interface ResultCaseView {
  readonly visit: VisitHeaderView;
  readonly patient: PatientHeaderView;
  readonly units: readonly ReportUnitView[];
  /** Report units with no visible result. */
  readonly hiddenUnits: number;
  /** Results hidden inside the visible units (sum of the units' `hiddenResults`). */
  readonly hiddenResults: number;
  readonly scannedSampleNumber: string | null;
  readonly settings: ResultSettingsView;
}

export interface ResultVisitListItem {
  readonly visitId: string;
  readonly accession: string;
  readonly registeredAtUtc: string;
  readonly patientId: string;
  readonly patientNumber: string | null;
  readonly patientName: string | null;
  readonly patientNameAr: string | null;
  readonly branchId: string;
  readonly status: ResultStatus;
  readonly results: number;
  readonly units: number;
  readonly hasPanic: boolean;
  readonly hasDelta: boolean;
  readonly anyPrinted: boolean;
  /** A VIP file the user may not see (rule A-03): the server sends "VIP patient" and no Arabic name. */
  readonly patientMasked?: boolean;
}

/** The query panel of CP-19 (LDM §5.2); `patientName` accepts `%`. */
export interface ResultVisitQuery {
  readonly accession: string | null;
  readonly patientNumber: string | null;
  readonly patientName: string | null;
  readonly status: ResultStatus | null;
  readonly sampleStatus: SampleStatusName | null;
  /** Visits with at least one analyser result of this analyser ("from analyser", LDM §5.2). */
  readonly analyserId: string | null;
  readonly notPrinted: boolean;
  readonly notVerified: boolean;
  readonly delta: boolean;
  readonly from: string | null;
  readonly to: string | null;
  /** Only visits of the session branch (default on; the laboratory may look across branches). */
  readonly branchOnly: boolean;
  readonly page: number;
  readonly pageSize: number;
}

export type ResultLogKind = 'Ordered' | 'Value' | 'Status' | 'DeltaAcknowledged' | 'Comment' | 'Note' | 'PatientMoved';

export interface ResultLogItem {
  readonly id: string;
  readonly kind: ResultLogKind;
  readonly fromStatus: ResultStatus | null;
  readonly toStatus: ResultStatus | null;
  readonly valueBefore: string | null;
  readonly valueAfter: string | null;
  readonly source: string | null;
  readonly analyserId: string | null;
  readonly atUtc: string;
  readonly byUserId: string | null;
  readonly byName: string | null;
  readonly detail: string | null;
}

export interface PreviousResultItem {
  readonly resultId: string;
  readonly visitId: string;
  readonly accession: string;
  readonly registeredAtUtc: string;
  readonly status: ResultStatus;
  readonly numericValue: number | null;
  readonly textValue: string | null;
  readonly unit: string | null;
  readonly flag: ResultFlag | null;
  readonly verifiedAtUtc: string | null;
}

// ---- Worklist (CP-18) ------------------------------------------------------------------------------------------------

export interface WorklistItem {
  readonly resultId: string;
  readonly visitId: string;
  readonly accession: string;
  readonly patientId: string;
  readonly patientNumber: string | null;
  readonly patientName: string | null;
  readonly patientNameAr: string | null;
  readonly gender: string | null;
  readonly ageYears: number | null;
  readonly testId: string;
  readonly testCode: string | null;
  readonly testNameEn: string | null;
  readonly testNameAr: string | null;
  readonly clinicalGroupId: string;
  readonly groupNameEn: string | null;
  readonly groupNameAr: string | null;
  readonly status: ResultStatus;
  readonly tubeId: string | null;
  readonly sampleNumber: string | null;
  readonly sampleStatus: string | null;
  readonly collectedAtUtc: string | null;
  readonly expectedAtUtc: string;
  readonly delaySeconds: number;
  readonly consumedPercent: number;
  readonly delayState: DelayState;
  readonly urgent: boolean;
  readonly textValue: string | null;
  readonly flag: ResultFlag | null;
  readonly isPrinted: boolean;
  readonly source: string;
  /** A VIP file the user may not see (rule A-03): the server sends "VIP patient" and no Arabic name. */
  readonly patientMasked?: boolean;
}

export interface WorklistCounters {
  readonly requests: number;
  readonly tests: number;
  readonly samples: number;
  readonly delayed: number;
}

export interface WorklistPage {
  readonly items: readonly WorklistItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly counters: WorklistCounters;
  readonly warnPercent: number;
  readonly latePercent: number;
}

/** Worklist filters (LDM §6): the collection window, group, test, analyser, sample status, payer, status boxes. */
/** `sort`: a key of the server allow-list (B-03, ResultsSortKeys.Worklist); absent = urgent first, then the expected time. */
export interface WorklistQuery extends SortRequest {
  readonly from: string | null;
  readonly to: string | null;
  readonly clinicalGroupId: string | null;
  readonly testId: string | null;
  readonly analyserId: string | null;
  readonly sampleStatus: SampleStatusName | null;
  readonly payerId: string | null;
  readonly statuses: readonly ResultStatus[];
  readonly delayedOnly: boolean;
  readonly search: string | null;
  readonly branchOnly: boolean;
  readonly page: number;
  readonly pageSize: number;
}

// ---- Documents -----------------------------------------------------------------------------------------------------

export interface ReportLetterhead {
  readonly labNameEn: string;
  readonly labNameAr: string;
  readonly branchNameEn: string | null;
  readonly branchNameAr: string | null;
}

export interface ReportTemplateInfo {
  readonly templateId: string;
  readonly nameEn: string;
  readonly language: string;
  readonly showQr: boolean;
  readonly logoFileId: string | null;
  readonly signatureFileId: string | null;
  readonly headerTextEn: string | null;
  readonly headerTextAr: string | null;
  readonly footerTextEn: string | null;
  readonly footerTextAr: string | null;
}

export interface ReportLine {
  readonly resultId: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly testType: TestType;
  readonly value: string | null;
  readonly unit: string | null;
  readonly range: string | null;
  readonly flag: ResultFlag | null;
  readonly isPanic: boolean;
  readonly status: ResultStatus;
  readonly comment: string | null;
  readonly paragraphs: readonly FormattedParagraphDto[] | null;
  readonly culture: CultureView | null;
  readonly methodology: string | null;
}

export interface ReportSection {
  readonly serviceId: string;
  readonly headingEn: string | null;
  readonly headingAr: string | null;
  readonly isProfile: boolean;
  readonly lines: readonly ReportLine[];
}

export interface LaboratoryReportDocument {
  readonly letterhead: ReportLetterhead;
  readonly template: ReportTemplateInfo | null;
  readonly language: string;
  readonly reportUnitKey: string;
  readonly titleEn: string;
  readonly titleAr: string;
  readonly patient: {
    readonly number: string | null;
    readonly fullName: string | null;
    readonly nameAr: string | null;
    readonly gender: string | null;
    readonly ageYears: number | null;
    readonly ageMonths: number | null;
    readonly ageDays: number | null;
    readonly dateOfBirth: string | null;
    /** Previewed without the See-VIP right (F-06). */
    readonly isMasked?: boolean;
  };
  readonly visit: {
    readonly accession: string;
    readonly registeredAtUtc: string;
    readonly payerNameEn: string | null;
    readonly payerNameAr: string | null;
    readonly doctorNameEn: string | null;
    readonly doctorNameAr: string | null;
  };
  readonly samples: readonly { readonly sampleNumber: string | null; readonly collectedAtUtc: string | null; readonly receivedAtUtc: string | null }[];
  readonly sections: readonly ReportSection[];
  readonly signature: { readonly verifiedByName: string | null; readonly verifiedAtUtc: string | null; readonly reviewedByName: string | null; readonly reviewedAtUtc: string | null };
  readonly isPreliminary: boolean;
  readonly isPreview: boolean;
  readonly printCount: number;
  readonly amendmentCount: number;
  readonly printListNumber: string | null;
  readonly qrValue: string;
  readonly generatedAtUtc: string;
  readonly generatedBy: string | null;
}

export interface WorklistDocument {
  readonly letterhead: ReportLetterhead;
  readonly generatedAtUtc: string;
  readonly generatedBy: string | null;
  readonly fromUtc: string | null;
  readonly toUtc: string | null;
  readonly groupNameEn: string | null;
  readonly groupNameAr: string | null;
  readonly rows: readonly WorklistItem[];
  readonly truncated: boolean;
}

// ---- Entry lookups -------------------------------------------------------------------------------------------------

/** A filter option (clinical group, test, payer). */
export interface LookupOption {
  readonly id: string;
  readonly code: string | null;
  readonly nameEn: string;
  readonly nameAr: string;
}

export interface CommentTemplateOption {
  readonly id: string;
  readonly shortName: string;
  readonly textEn: string;
  readonly textAr: string;
  readonly clinicalGroupId: string | null;
}

export interface OrganismOption {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
}

export interface AntibioticOption {
  readonly id: string;
  readonly code: string;
  readonly abbreviation: string;
  readonly nameEn: string;
  readonly nameAr: string;
}

export interface SensitivityOption {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly displayOrder: number;
}

export interface MicrobiologyLookups {
  readonly organisms: readonly OrganismOption[];
  readonly antibiotics: readonly AntibioticOption[];
  readonly sensitivities: readonly SensitivityOption[];
}

export interface AnalyserOption {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
}

const enc = encodeURIComponent;

export const RESULTS_API = {
  results: '/api/v1/results',
  visits: '/api/v1/results/visits',
  values: '/api/v1/results/values',
  reviewMany: '/api/v1/results/review',
  verifyMany: '/api/v1/results/verify',
  worklist: '/api/v1/results/worklist',
  worklistDocument: '/api/v1/results/worklist/documents/worklist',
  commentTemplates: '/api/v1/results/lookups/comment-templates',
  microbiology: '/api/v1/results/lookups/microbiology',
  analysers: '/api/v1/results/lookups/analysers',
  clinicalGroups: '/api/v1/results/lookups/clinical-groups',
  tests: '/api/v1/results/lookups/tests',
  payers: '/api/v1/results/lookups/payers',
  result: (id: string): string => `/api/v1/results/${enc(id)}`,
  review: (id: string): string => `/api/v1/results/${enc(id)}/review`,
  verify: (id: string): string => `/api/v1/results/${enc(id)}/verify`,
  undo: (id: string): string => `/api/v1/results/${enc(id)}/undo`,
  amend: (id: string): string => `/api/v1/results/${enc(id)}/amend`,
  acknowledgeDelta: (id: string): string => `/api/v1/results/${enc(id)}/acknowledge-delta`,
  comment: (id: string): string => `/api/v1/results/${enc(id)}/comment`,
  note: (id: string): string => `/api/v1/results/${enc(id)}/note`,
  log: (id: string): string => `/api/v1/results/${enc(id)}/log`,
  previous: (id: string): string => `/api/v1/results/${enc(id)}/previous`,
  report: (key: string): string => `/api/v1/results/report-units/${enc(key)}/documents/report`,
  print: (key: string): string => `/api/v1/results/report-units/${enc(key)}/print`,
} as const;

/** Permission keys of the module (affordance only; the server decides). */
export const RESULTS_PERMISSIONS = {
  view: 'results.view',
  enter: 'results.create',
  amend: 'results.edit',
  approve: 'results.approve',
  undo: 'results.cancel',
  print: 'results.export',
  export: 'results.export',
} as const;

/**
 * In-app links of the module, for other pages too: registration and patients open a visit's results with
 * `/results/by-accession?accession=<acc>` (the page binds the query parameter to its `accession` input).
 */
export const RESULTS_LINKS = {
  worklist: '/results/worklist',
  byAccession: '/results/by-accession',
  accession: (accession: string): string => `/results/by-accession?accession=${enc(accession)}`,
} as const;
