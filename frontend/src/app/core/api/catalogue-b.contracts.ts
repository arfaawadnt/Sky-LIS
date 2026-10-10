/** Catalogue B contracts mirrored from `Platform.Contracts.Catalogue.CatalogueBContracts` (bootstrap until the OpenAPI generator lands, 01 §6.2). */

export type { Paged } from './paging';

export interface CatalogueBCreated {
  readonly id: string;
}

export type PreparationAnswerType = 'Text' | 'YesNo' | 'OptionList';
export const PREPARATION_ANSWER_TYPES: readonly PreparationAnswerType[] = ['Text', 'YesNo', 'OptionList'];

export type PreparationGender = 'Any' | 'Male' | 'Female';
export const PREPARATION_GENDERS: readonly PreparationGender[] = ['Any', 'Male', 'Female'];

export type ReasonKind = 'Rejection' | 'Cancellation' | 'Discount' | 'Refund' | 'Transfer';
export const REASON_KINDS: readonly ReasonKind[] = ['Rejection', 'Cancellation', 'Discount', 'Refund', 'Transfer'];

export type ReportTemplateType = 'Results' | 'Culture' | 'Text' | 'Claim' | 'Receipt' | 'Label' | 'JobOrder';
export const REPORT_TEMPLATE_TYPES: readonly ReportTemplateType[] = ['Results', 'Culture', 'Text', 'Claim', 'Receipt', 'Label', 'JobOrder'];

export type ReportTemplateLanguage = 'En' | 'Ar' | 'Both';
export const REPORT_TEMPLATE_LANGUAGES: readonly ReportTemplateLanguage[] = ['En', 'Ar', 'Both'];

/** A service linked to a Catalogue B record, with the labels Catalogue A holds for it. */
export interface LinkedServiceView {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly isActive: boolean;
}

/** The shared register filter: search text, active flag (`null` = both), paging. */
export interface CatalogueBListFilter {
  readonly search: string;
  readonly isActive: boolean | null;
  readonly page: number;
  readonly pageSize: number;
}

// CP-46 Preparations

export interface PreparationListItem {
  readonly id: string;
  readonly code: string;
  readonly questionEn: string;
  readonly questionAr: string;
  readonly answerType: PreparationAnswerType;
  readonly options: readonly string[];
  readonly gender: PreparationGender;
  /** Empty = asked for every service. */
  readonly services: readonly LinkedServiceView[];
  readonly isActive: boolean;
}

export interface CreatePreparationRequest {
  readonly code: string;
  readonly questionEn: string;
  readonly questionAr: string;
  readonly answerType: PreparationAnswerType;
  readonly options: readonly string[];
  readonly gender: PreparationGender;
  readonly serviceIds: readonly string[];
}

export type UpdatePreparationRequest = Omit<CreatePreparationRequest, 'code'>;

// CP-47 Comment library

export interface CommentTemplateListItem {
  readonly id: string;
  readonly shortName: string;
  readonly textEn: string;
  readonly textAr: string;
  readonly clinicalGroupId: string | null;
  readonly clinicalGroupNameEn: string | null;
  readonly clinicalGroupNameAr: string | null;
  readonly isActive: boolean;
}

export interface CommentTemplateRequest {
  readonly shortName: string;
  readonly textEn: string;
  readonly textAr: string;
  readonly clinicalGroupId: string | null;
}

// CP-48 Doctors

export interface DoctorListItem {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly specialty: string | null;
  readonly occupation: string | null;
  /** Empty = offered in every branch. */
  readonly branchIds: readonly string[];
  readonly sharePercent: number;
  readonly phone: string | null;
  readonly isActive: boolean;
}

export interface CreateDoctorRequest {
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly specialty: string | null;
  readonly occupation: string | null;
  readonly branchIds: readonly string[];
  readonly sharePercent: number;
  readonly phone: string | null;
}

export type UpdateDoctorRequest = Omit<CreateDoctorRequest, 'code'>;

// CP-49 Diagnoses

export interface DiagnosisListItem {
  readonly id: string;
  readonly icdCode: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly services: readonly LinkedServiceView[];
  readonly isActive: boolean;
}

export interface CreateDiagnosisRequest {
  readonly icdCode: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly serviceIds: readonly string[];
}

export type UpdateDiagnosisRequest = Omit<CreateDiagnosisRequest, 'icdCode'>;

// CP-50 Sending labs

export interface SendingLabListItem {
  readonly id: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly payerId: string;
  readonly contractTierId: string;
  readonly courierEn: string;
  readonly courierAr: string;
  readonly phone: string | null;
  readonly doctorId: string | null;
  readonly doctorNameEn: string | null;
  readonly doctorNameAr: string | null;
  readonly isActive: boolean;
}

export interface SendingLabRequest {
  readonly nameEn: string;
  readonly nameAr: string;
  readonly payerId: string;
  readonly contractTierId: string;
  readonly courierEn: string;
  readonly courierAr: string;
  readonly phone: string | null;
  readonly doctorId: string | null;
}

// CP-51 Shortcut sets

export interface ShortcutSetListItem {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly services: readonly LinkedServiceView[];
  readonly isActive: boolean;
}

export interface CreateShortcutSetRequest {
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly serviceIds: readonly string[];
}

export type UpdateShortcutSetRequest = Omit<CreateShortcutSetRequest, 'code'>;

// CP-52 Test rules

export interface TestRuleListItem {
  readonly id: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly clinicalGroupId: string | null;
  readonly clinicalGroupNameEn: string | null;
  readonly clinicalGroupNameAr: string | null;
  readonly triggers: readonly LinkedServiceView[];
  readonly added: readonly LinkedServiceView[];
  readonly isActive: boolean;
}

export interface TestRuleRequest {
  readonly nameEn: string;
  readonly nameAr: string;
  readonly clinicalGroupId: string | null;
  readonly triggerServiceIds: readonly string[];
  readonly addedServiceIds: readonly string[];
}

export interface ValidateTestRuleRequest {
  readonly ruleId: string | null;
  readonly triggerServiceIds: readonly string[];
  readonly addedServiceIds: readonly string[];
}

export type TestRuleIssueSeverity = 'Error' | 'Warning';

export interface TestRuleIssue {
  readonly code: string;
  readonly severity: TestRuleIssueSeverity;
  readonly message: string;
  readonly serviceId: string | null;
  readonly otherRuleId: string | null;
}

export interface TestRuleValidationResult {
  readonly isValid: boolean;
  readonly issues: readonly TestRuleIssue[];
}

// CP-53 Reasons

export interface ReasonListItem {
  readonly id: string;
  readonly kind: ReasonKind;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly isActive: boolean;
}

export interface ReasonRequest {
  readonly kind: ReasonKind;
  readonly nameEn: string;
  readonly nameAr: string;
}

// CP-54 Report templates

export interface ReportTemplateListItem {
  readonly id: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly type: ReportTemplateType;
  readonly logoFileId: string | null;
  readonly signatureFileId: string | null;
  readonly showQr: boolean;
  readonly language: ReportTemplateLanguage;
  readonly headerTextEn: string | null;
  readonly headerTextAr: string | null;
  readonly footerTextEn: string | null;
  readonly footerTextAr: string | null;
  readonly isActive: boolean;
}

export interface ReportTemplateRequest {
  readonly nameEn: string;
  readonly nameAr: string;
  readonly type: ReportTemplateType;
  readonly logoFileId: string | null;
  readonly signatureFileId: string | null;
  readonly showQr: boolean;
  readonly language: ReportTemplateLanguage;
  readonly headerTextEn: string | null;
  readonly headerTextAr: string | null;
  readonly footerTextEn: string | null;
  readonly footerTextAr: string | null;
}

// CP-56 / SA-14 Microbiology

export interface DrugTypeListItem {
  readonly id: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly isActive: boolean;
}

export interface DrugTypeRequest {
  readonly nameEn: string;
  readonly nameAr: string;
}

export interface CommercialNameListItem {
  readonly id: string;
  readonly drugTypeId: string;
  readonly drugTypeNameEn: string;
  readonly drugTypeNameAr: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly isActive: boolean;
}

export interface CommercialNameRequest {
  readonly drugTypeId: string;
  readonly nameEn: string;
  readonly nameAr: string;
}

export interface AntibioticListItem {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly abbreviation: string;
  readonly commercialNameId: string | null;
  readonly commercialNameEn: string | null;
  readonly commercialNameAr: string | null;
  readonly isActive: boolean;
}

export interface AntibioticRequest {
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly abbreviation: string;
  readonly commercialNameId: string | null;
}

export interface OrganismListItem {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly isActive: boolean;
}

export interface OrganismRequest {
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
}

export interface SensitivityValueListItem {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly displayOrder: number;
  readonly isActive: boolean;
}

export interface SensitivityValueRequest {
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly displayOrder: number;
}

/** Endpoints of the module. Each list also serves `GET {id}`, `GET export` (CSV), `PUT {id}`, `POST {id}/deactivate|reactivate`. */
export const CATALOGUE_B_API = {
  preparations: '/api/v1/catalogue/preparations',
  commentTemplates: '/api/v1/catalogue/comment-templates',
  doctors: '/api/v1/catalogue/doctors',
  diagnoses: '/api/v1/catalogue/diagnoses',
  sendingLabs: '/api/v1/catalogue/sending-labs',
  shortcutSets: '/api/v1/catalogue/shortcut-sets',
  testRules: '/api/v1/catalogue/test-rules',
  testRuleValidation: '/api/v1/catalogue/test-rules/validate',
  reasons: '/api/v1/catalogue/reasons',
  reportTemplates: '/api/v1/catalogue/report-templates',
  drugTypes: '/api/v1/catalogue/microbiology/drug-types',
  commercialNames: '/api/v1/catalogue/microbiology/commercial-names',
  antibiotics: '/api/v1/catalogue/microbiology/antibiotics',
  organisms: '/api/v1/catalogue/microbiology/organisms',
  sensitivityValues: '/api/v1/catalogue/microbiology/sensitivity-values',
} as const;

/** The lookups of other modules the pages read (same wave; parsed defensively in `data-b/catalogue-b-lookups.ts`). */
export const CATALOGUE_B_LOOKUP_API = {
  services: '/api/v1/catalogue/lookups/services',
  clinicalGroups: '/api/v1/catalogue/clinical-groups',
  branches: '/api/v1/organization/branches',
  tierOptions: '/api/v1/pricing/payers/tiers',
} as const;

/** Permission keys of the module, as the catalogue defines them. */
export const CATALOGUE_B_PERMISSIONS = {
  view: 'catalogue.view',
  create: 'catalogue.create',
  edit: 'catalogue.edit',
  manage: 'catalogue.manage',
  export: 'catalogue.export',
} as const;
