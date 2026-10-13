/** Registration module contracts mirrored from `Platform.Contracts.Registration` (bootstrap until the OpenAPI generator lands, 01 §6.2). */

export type { Paged } from './paging';

export type VisitStatus = 'Open' | 'Completed';
export type VisitLineStatus = 'Ordered' | 'Reserved' | 'Cancelled';
export type VisitLineKind = 'Service' | 'Package' | 'PackageMember';
export type DiscountKind = 'Amount' | 'Percent';

export const VISIT_STATUSES: readonly VisitStatus[] = ['Open', 'Completed'];

// ---- ordering ------------------------------------------------------------------------------------------------------

export interface OrderLineRequest {
  readonly serviceId: string;
  readonly reserved: boolean;
}

export interface PreparationAnswerRequest {
  readonly preparationId: string;
  readonly answer: string;
}

export interface VisitDiscountRequest {
  readonly kind: DiscountKind;
  readonly value: number;
  readonly reasonId: string;
}

export interface RegisterVisitRequest {
  readonly patientId: string;
  readonly treatmentNo: string | null;
  readonly referringDoctorId: string | null;
  readonly payerId: string;
  readonly contractTierId: string;
  readonly insuranceId: string | null;
  readonly diagnosisId: string | null;
  readonly bookingId: string | null;
  readonly urgent: boolean;
  readonly biohazard: boolean;
  readonly collectedOutside: boolean;
  readonly lines: readonly OrderLineRequest[];
  readonly preparationAnswers: readonly PreparationAnswerRequest[] | null;
  readonly discount: VisitDiscountRequest | null;
  readonly confirmFrequencyWarnings: boolean;
  readonly utcOffsetMinutes: number | null;
}

export interface OrderWarning {
  readonly code: string;
  readonly serviceId: string | null;
  readonly serviceCode: string | null;
  readonly serviceNameEn: string | null;
  readonly serviceNameAr: string | null;
  readonly detail: string | null;
  readonly detailAr: string | null;
  readonly lastOrderedAtUtc: string | null;
  readonly lastAccession: string | null;
  readonly windowDays: number | null;
}

export interface OrderIssue {
  readonly code: string;
  readonly serviceId: string | null;
  readonly serviceCode: string | null;
  readonly message: string;
  /** REG-109: the repeated tests with the services that order them, e.g. "CREA (PKG › RFT)" (validation O-3). */
  readonly detail?: string | null;
}

export interface VisitRegisteredResponse {
  readonly id: string;
  readonly accession: string;
  readonly tubeCount: number;
  readonly warnings: readonly OrderWarning[];
}

export interface CheckOrderRequest {
  readonly visitId: string | null;
  readonly patientId: string | null;
  readonly payerId: string;
  readonly contractTierId: string;
  readonly diagnosisId: string | null;
  readonly lines: readonly OrderLineRequest[];
  readonly utcOffsetMinutes: number | null;
}

export interface OrderLinePreview {
  readonly serviceId: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly type: string;
  readonly kind: VisitLineKind;
  readonly status: VisitLineStatus;
  readonly priced: boolean;
  readonly listPrice: number;
  readonly patientShare: number;
  readonly payerShare: number;
  readonly expectedResultAtUtc: string | null;
  readonly precaution: string | null;
  readonly alertCondition: string | null;
  readonly origin: 'Manual' | 'Rule' | 'Diagnosis';
  readonly addedByRuleId: string | null;
  readonly ruleNameEn: string | null;
  readonly ruleNameAr: string | null;
  readonly tubeKey: string | null;
  readonly members: readonly OrderLinePreview[];
}

export interface TubePreview {
  readonly key: string;
  readonly existingTubeId: string | null;
  readonly sampleNumber: string | null;
  readonly unitCode: string;
  readonly clinicalGroupNameEn: string;
  readonly clinicalGroupNameAr: string;
  readonly sampleTypeNameEn: string;
  readonly sampleTypeNameAr: string;
  readonly sampleConditionNameEn: string | null;
  readonly sampleConditionNameAr: string | null;
  readonly serviceIds: readonly string[];
}

export interface OrderTotals {
  readonly totalListPrice: number;
  readonly patientShareTotal: number;
  readonly payerShareTotal: number;
}

export type PreparationAnswerType = 'Text' | 'YesNo' | 'OptionList';

export interface PreparationQuestionItem {
  readonly preparationId: string;
  readonly code: string;
  readonly questionEn: string;
  readonly questionAr: string;
  readonly answerType: PreparationAnswerType;
  readonly options: readonly string[];
  readonly gender: string;
  readonly serviceIds: readonly string[];
}

export interface OrderCheckResult {
  readonly lines: readonly OrderLinePreview[];
  readonly tubes: readonly TubePreview[];
  readonly totals: OrderTotals;
  readonly issues: readonly OrderIssue[];
  readonly warnings: readonly OrderWarning[];
  readonly preparations: readonly PreparationQuestionItem[];
  readonly currency: string;
}

export interface ServiceEntryItem {
  readonly id: string;
  readonly code: string;
  readonly shortName: string;
  readonly shortcutNo: number | null;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly type: string;
}

export interface ServiceEntryResult {
  readonly kind: 'Service' | 'ShortcutSet' | 'None';
  readonly services: readonly ServiceEntryItem[];
  readonly setCode: string | null;
  readonly setNameEn: string | null;
  readonly setNameAr: string | null;
  readonly candidates: readonly ServiceEntryItem[];
}

// ---- saved visit ---------------------------------------------------------------------------------------------------

export interface AddVisitLinesRequest {
  readonly lines: readonly OrderLineRequest[];
  readonly preparationAnswers: readonly PreparationAnswerRequest[] | null;
  readonly confirmFrequencyWarnings: boolean;
}

export interface VisitLinesAddedResponse {
  readonly addedLines: number;
  readonly newTubes: number;
  readonly warnings: readonly OrderWarning[];
}

export interface ReleaseLinesRequest {
  readonly lineIds: readonly string[];
}

export interface CancelLinesRequest {
  readonly lineIds: readonly string[];
  readonly reasonId: string | null;
  readonly reason: string | null;
}

export interface UpdateVisitDetailsRequest {
  readonly treatmentNo: string | null;
  readonly referringDoctorId: string | null;
  readonly insuranceId: string | null;
  readonly urgent: boolean;
  readonly biohazard: boolean;
  readonly collectedOutside: boolean;
}

export interface SetVisitDiscountRequest {
  readonly discount: VisitDiscountRequest | null;
  readonly reason: string | null;
}

export interface ContractRef {
  readonly payerId: string;
  readonly payerNameEn: string;
  readonly payerNameAr: string;
  readonly contractTierId: string;
  readonly tierNameEn: string;
  readonly tierNameAr: string;
}

// ---- registers -----------------------------------------------------------------------------------------------------

export interface VisitLineChip {
  readonly lineId: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly status: VisitLineStatus;
  readonly kind: VisitLineKind;
}

export interface VisitListItem {
  readonly id: string;
  readonly accession: string;
  readonly registeredAtUtc: string;
  readonly branchId: string;
  readonly branchNameEn: string | null;
  readonly branchNameAr: string | null;
  readonly patientId: string;
  readonly patientNumber: string | null;
  readonly patientName: string | null;
  readonly patientNameAr: string | null;
  readonly mobile: string | null;
  readonly patientMasked: boolean;
  readonly payerId: string;
  readonly payerNameEn: string | null;
  readonly payerNameAr: string | null;
  readonly tierNameEn: string | null;
  readonly tierNameAr: string | null;
  readonly status: VisitStatus;
  readonly locationType: string;
  readonly urgent: boolean;
  readonly batchNumber: string | null;
  readonly totalListPrice: number;
  readonly discountTotal: number;
  readonly patientShareTotal: number;
  readonly payerShareTotal: number;
  readonly paid: number | null;
  readonly remaining: number | null;
  readonly lines: readonly VisitLineChip[];
}

export interface VisitSearchFilter {
  readonly accession: string | null;
  readonly patientNo: string | null;
  readonly name: string | null;
  readonly mobile: string | null;
  readonly from: string | null;
  readonly to: string | null;
  readonly branchId: string | null;
  readonly payerId: string | null;
  readonly status: VisitStatus | null;
  readonly patientId: string | null;
  readonly page: number;
  readonly pageSize: number;
}

export interface VisitLineItem {
  readonly id: string;
  readonly serviceId: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly serviceType: string;
  readonly kind: VisitLineKind;
  readonly parentLineId: string | null;
  readonly status: VisitLineStatus;
  readonly listPrice: number;
  readonly patientShare: number;
  readonly payerShare: number;
  readonly discount: number;
  readonly expectedResultAtUtc: string | null;
  readonly tubeId: string | null;
  readonly sampleNumber: string | null;
  readonly collectorUserId: string | null;
  readonly collectorName: string | null;
  readonly origin: string;
  readonly orderedAtUtc: string;
  readonly orderedByName: string | null;
  readonly releasedAtUtc: string | null;
  readonly cancellationReason: string | null;
  readonly cancelledByName: string | null;
  readonly cancelledAtUtc: string | null;
}

export interface VisitTubeItem {
  readonly id: string;
  readonly sampleNumber: string;
  readonly unitCode: string;
  readonly clinicalGroupId: string;
  readonly clinicalGroupNameEn: string;
  readonly clinicalGroupNameAr: string;
  readonly sampleTypeId: string;
  readonly sampleTypeNameEn: string;
  readonly sampleTypeNameAr: string;
  readonly sampleConditionNameEn: string | null;
  readonly sampleConditionNameAr: string | null;
  readonly isLive: boolean;
  readonly serviceCodes: readonly string[];
}

export interface PreparationAnswerItem {
  readonly preparationId: string;
  readonly questionEn: string;
  readonly questionAr: string;
  readonly answer: string;
  readonly answeredAtUtc: string;
}

export interface VisitBalanceView {
  readonly patientShareTotal: number;
  readonly paid: number;
  readonly refunded: number;
  readonly remaining: number;
  readonly refundable: number;
}

export interface VisitPatientView {
  readonly id: string;
  readonly number: string | null;
  readonly title: string | null;
  readonly fullName: string | null;
  readonly nameAr: string | null;
  readonly gender: string | null;
  readonly ageYears: number;
  readonly ageMonths: number;
  readonly ageDays: number;
  readonly mobile: string | null;
  readonly nationalId: string | null;
  readonly isVip: boolean;
  readonly status: string | null;
  readonly masked: boolean;
}

export interface VisitDetail {
  readonly id: string;
  readonly accession: string;
  readonly status: VisitStatus;
  readonly locationType: string;
  readonly branchId: string;
  readonly branchNameEn: string | null;
  readonly branchNameAr: string | null;
  readonly patient: VisitPatientView;
  readonly originalPatientId: string | null;
  readonly contract: ContractRef;
  readonly referringDoctorId: string | null;
  readonly doctorNameEn: string | null;
  readonly doctorNameAr: string | null;
  readonly treatmentNo: string | null;
  readonly insuranceId: string | null;
  readonly diagnosisId: string | null;
  readonly bookingId: string | null;
  readonly batchId: string | null;
  readonly batchNumber: string | null;
  readonly urgent: boolean;
  readonly biohazard: boolean;
  readonly collectedOutside: boolean;
  readonly registeredAtUtc: string;
  readonly registeredByName: string | null;
  readonly completedAtUtc: string | null;
  readonly totalListPrice: number;
  readonly discountTotal: number;
  readonly patientShareTotal: number;
  readonly payerShareTotal: number;
  readonly discountKind: DiscountKind | null;
  readonly discountValue: number;
  readonly discountReasonId: string | null;
  readonly discountReason: string | null;
  readonly utcOffsetMinutes: number;
  readonly lines: readonly VisitLineItem[];
  readonly tubes: readonly VisitTubeItem[];
  readonly preparationAnswers: readonly PreparationAnswerItem[];
  readonly transfers: readonly unknown[];
  readonly balance: VisitBalanceView | null;
  readonly rowVersion: number;
}

export interface RegistrationPageSettings {
  readonly autoPrintLabels: boolean;
  readonly autoPrintJobOrder: boolean;
  readonly autoPrintReceipt: boolean;
  readonly labelCopies: number;
  readonly mandatoryMobile: boolean;
  readonly mandatoryDoctor: boolean;
  readonly mandatoryNationalId: boolean;
  readonly discountCapPercent: number;
  readonly canCancel: boolean;
  readonly canTransfer: boolean;
  readonly canPay: boolean;
  readonly canSeeVip: boolean;
  readonly currency: string;
  /** The session's branch (CP-04) for the page header; null before a branch is chosen. */
  readonly branchNameEn?: string | null;
  readonly branchNameAr?: string | null;
}

// ---- documents -----------------------------------------------------------------------------------------------------

export interface RegistrationLetterhead {
  readonly labNameEn: string;
  readonly labNameAr: string;
  readonly branchNameEn: string | null;
  readonly branchNameAr: string | null;
}

export interface SampleLabel {
  readonly tubeId: string;
  readonly sampleNumber: string;
  readonly unitCode: string;
  readonly sampleTypeNameEn: string;
  readonly sampleTypeNameAr: string;
  readonly sampleConditionNameEn: string | null;
  readonly sampleConditionNameAr: string | null;
  readonly accession: string;
  readonly patientNumber: string | null;
  readonly patientName: string | null;
  readonly patientNameAr: string | null;
  readonly gender: string | null;
  readonly ageYears: number;
  readonly registeredAtUtc: string;
  readonly branchNameEn: string | null;
  readonly branchNameAr: string | null;
  readonly urgent: boolean;
  readonly serviceCodes: readonly string[];
  /** The visit is flagged biohazard: the label prints "BIO" (validation 2026-10-11). */
  readonly biohazard: boolean;
}

export interface VisitLabelsDocument {
  readonly letterhead: RegistrationLetterhead;
  readonly accession: string;
  readonly copies: number;
  readonly utcOffsetMinutes: number;
  readonly labels: readonly SampleLabel[];
}

export interface JobOrderLine {
  readonly lineId: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly kind: VisitLineKind;
  readonly status: VisitLineStatus;
  readonly unitCode: string | null;
  readonly sampleNumber: string | null;
  readonly sampleTypeNameEn: string | null;
  readonly sampleTypeNameAr: string | null;
  readonly expectedResultAtUtc: string | null;
}

export interface JobOrderDocument {
  readonly letterhead: RegistrationLetterhead;
  readonly visitId: string;
  readonly accession: string;
  readonly registeredAtUtc: string;
  readonly registeredByName: string | null;
  readonly patient: VisitPatientView;
  readonly visitOrdinal: number;
  readonly contract: ContractRef;
  readonly doctorNameEn: string | null;
  readonly doctorNameAr: string | null;
  readonly urgent: boolean;
  readonly biohazard: boolean;
  readonly collectedOutside: boolean;
  readonly batchNumber: string | null;
  readonly lines: readonly JobOrderLine[];
  readonly tubes: readonly VisitTubeItem[];
  readonly preparationAnswers: readonly PreparationAnswerItem[];
  readonly balance: VisitBalanceView | null;
  readonly currency: string;
  readonly utcOffsetMinutes: number;
  readonly printedBy: string | null;
  readonly printedAtUtc: string;
}

export interface PriceQuoteRequest {
  readonly payerId: string | null;
  readonly contractTierId: string | null;
  readonly serviceIds: readonly string[];
  readonly gender: 'Male' | 'Female' | null;
  readonly utcOffsetMinutes: number | null;
}

export interface PriceQuoteDocumentLine {
  readonly serviceId: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly type: string;
  readonly priced: boolean;
  readonly listPrice: number;
  readonly patientShare: number;
  readonly payerShare: number;
  readonly expectedResultAtUtc: string | null;
  readonly precaution: string | null;
  readonly alertCondition: string | null;
}

export interface PriceQuoteDocument {
  readonly letterhead: RegistrationLetterhead;
  readonly contract: ContractRef | null;
  readonly currency: string;
  readonly lines: readonly PriceQuoteDocumentLine[];
  readonly totalListPrice: number;
  readonly totalPatientShare: number;
  readonly totalPayerShare: number;
  readonly preparations: readonly PreparationQuestionItem[];
  readonly issues: readonly OrderIssue[];
  readonly utcOffsetMinutes: number;
  readonly printedBy: string | null;
  readonly printedAtUtc: string;
}

export const REGISTRATION_API = {
  visits: '/api/v1/registration/visits',
  check: '/api/v1/registration/visits/check',
  exportVisits: '/api/v1/registration/visits/export',
  preparations: '/api/v1/registration/preparations',
  serviceEntry: '/api/v1/registration/service-entry',
  settings: '/api/v1/registration/settings',
  quote: '/api/v1/registration/quote',
} as const;

/** Permission keys of the module, as the catalogue defines them. */
export const REGISTRATION_PERMISSIONS = {
  view: 'registration.view',
  create: 'registration.create',
  edit: 'registration.edit',
  cancel: 'registration.cancel',
  approve: 'registration.approve',
  export: 'registration.export',
} as const;
