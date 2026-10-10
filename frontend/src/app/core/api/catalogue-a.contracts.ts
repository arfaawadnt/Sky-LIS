/** Catalogue half A contracts mirrored from `Platform.Contracts.Catalogue` (bootstrap until the OpenAPI generator lands, 01 §6.2). */

export type { Paged } from './paging';

export type ServiceType = 'Test' | 'Profile' | 'Package' | 'Culture' | 'TextReport' | 'Additional';
export const SERVICE_TYPES: readonly ServiceType[] = ['Test', 'Profile', 'Package', 'Culture', 'TextReport', 'Additional'];

export type LabTestType = 'Numeric' | 'NonNumeric' | 'OptionalList' | 'RestrictedList' | 'Formula' | 'FormattedText' | 'Culture';
export const LAB_TEST_TYPES: readonly LabTestType[] = ['Numeric', 'NonNumeric', 'OptionalList', 'RestrictedList', 'Formula', 'FormattedText', 'Culture'];

export type GenderRestriction = 'Any' | 'Male' | 'Female';
export const GENDER_RESTRICTIONS: readonly GenderRestriction[] = ['Any', 'Male', 'Female'];

export type RangeAgeUnit = 'Days' | 'Months' | 'Years';
export const RANGE_AGE_UNITS: readonly RangeAgeUnit[] = ['Days', 'Months', 'Years'];

export type FrequencyCheckAction = 'Alert' | 'Prevent';
export const FREQUENCY_ACTIONS: readonly FrequencyCheckAction[] = ['Alert', 'Prevent'];

export type ServiceMemberKind = 'Test' | 'Service';

export interface CreatedResource {
  readonly id: string;
}

/** The filter of every catalogue-A register; `isActive: null` = all. */
export interface CatalogueListFilter {
  readonly search: string;
  readonly isActive: boolean | null;
  readonly page: number;
  readonly pageSize: number;
}

// ---- clinical groups (CP-39)

export interface ClinicalGroupListItem {
  readonly id: string;
  readonly code: string;
  readonly unitCode: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly reportName: string | null;
  readonly reportTemplateId: string | null;
  readonly displayOrder: number;
  readonly isActive: boolean;
}

export interface ClinicalGroupRequest {
  readonly code: string;
  readonly unitCode: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly reportName: string | null;
  readonly reportTemplateId: string | null;
  readonly displayOrder: number;
}

// ---- sample types (CP-43)

export interface SampleConditionItem {
  readonly id: string | null;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly isActive: boolean;
}

export interface SampleTypeRequest {
  readonly code: string;
  readonly shortName: string;
  readonly nameEn: string;
  readonly nameAr: string;
}

export interface SampleTypeListItem {
  readonly id: string;
  readonly code: string;
  readonly shortName: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly activeConditionCount: number;
  readonly isActive: boolean;
}

export interface SampleConditionView {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly isActive: boolean;
}

export interface SampleTypeDetail {
  readonly id: string;
  readonly code: string;
  readonly shortName: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly isActive: boolean;
  readonly conditions: readonly SampleConditionView[];
}

// ---- units (CP-44) and patient conditions (CP-45)

export interface MeasurementUnitListItem {
  readonly id: string;
  readonly name: string;
  readonly isActive: boolean;
}

export interface MeasurementUnitRequest {
  readonly name: string;
}

export interface PatientConditionListItem {
  readonly id: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly isActive: boolean;
}

export interface PatientConditionRequest {
  readonly nameEn: string;
  readonly nameAr: string;
}

// ---- services (CP-40, CP-42, CP-55)

export interface ServiceMemberItem {
  readonly kind: ServiceMemberKind;
  readonly memberId: string;
  readonly order: number;
  readonly inSum: boolean;
  readonly visible: boolean;
}

/** The editable data of a service (create adds `code`, `type` and `members`). */
export interface ServiceGeneral {
  readonly shortName: string;
  readonly shortcutNo: number | null;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly reportName: string | null;
  readonly loinc: string | null;
  readonly cpt: string | null;
  readonly clinicalGroupId: string | null;
  readonly sampleTypeId: string | null;
  readonly sampleConditionId: string | null;
  readonly header: string | null;
  readonly aliases: readonly string[];
  readonly precaution: string | null;
  readonly gender: GenderRestriction;
  readonly turnaroundHours: number;
  readonly labelCount: number;
  readonly printAlone: boolean;
  readonly isOrderable: boolean;
  readonly showOnWeb: boolean;
  readonly profileSum: number | null;
}

export interface CreateServiceRequest extends ServiceGeneral {
  readonly code: string;
  readonly type: ServiceType;
  readonly members: readonly ServiceMemberItem[];
}

export interface UpdateServiceRequest extends ServiceGeneral {
  readonly type: ServiceType | null;
}

export interface ServiceListItem {
  readonly id: string;
  readonly code: string;
  readonly shortName: string;
  readonly shortcutNo: number | null;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly type: ServiceType;
  readonly clinicalGroupId: string | null;
  readonly clinicalGroupNameEn: string | null;
  readonly clinicalGroupNameAr: string | null;
  readonly sampleTypeId: string | null;
  readonly sampleTypeNameEn: string | null;
  readonly sampleTypeNameAr: string | null;
  readonly gender: GenderRestriction;
  readonly sequence: number;
  readonly memberCount: number;
  readonly isOrderable: boolean;
  readonly printAlone: boolean;
  readonly isActive: boolean;
}

export interface ServiceMemberView {
  readonly kind: ServiceMemberKind;
  readonly memberId: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  /** The member's own test or service type. */
  readonly memberType: string;
  readonly isActive: boolean;
  readonly order: number;
  readonly inSum: boolean;
  readonly visible: boolean;
}

export interface ServiceDetail extends ServiceGeneral {
  readonly id: string;
  readonly code: string;
  readonly type: ServiceType;
  readonly sequence: number;
  readonly isActive: boolean;
  readonly members: readonly ServiceMemberView[];
}

export interface ServiceListFilter extends CatalogueListFilter {
  readonly type: ServiceType | null;
  readonly clinicalGroupId: string | null;
}

export interface ServiceSequenceRow {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly type: ServiceType;
  readonly clinicalGroupNameEn: string | null;
  readonly clinicalGroupNameAr: string | null;
  readonly sequence: number;
  readonly isActive: boolean;
}

export interface ServiceSequenceChange {
  readonly serviceId: string;
  readonly sequence: number;
}

export interface ServiceLookupItem {
  readonly id: string;
  readonly code: string;
  readonly shortName: string;
  readonly shortcutNo: number | null;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly type: ServiceType;
  readonly clinicalGroupId: string | null;
  readonly clinicalGroupNameEn: string | null;
  readonly clinicalGroupNameAr: string | null;
  readonly isOrderable: boolean;
}

// ---- tests (CP-41)

export interface LabTestGeneral {
  readonly nameEn: string;
  readonly nameAr: string;
  readonly reportName: string | null;
  readonly loinc: string | null;
  readonly cpt: string | null;
  readonly clinicalGroupId: string;
  readonly type: LabTestType;
  readonly sampleTypeId: string | null;
  readonly sampleConditionId: string | null;
  readonly unitId: string | null;
  readonly gender: GenderRestriction;
  readonly methodology: string | null;
  readonly decimals: number;
  readonly isHiddenHelper: boolean;
  readonly printReference: boolean;
  readonly labelCount: number;
  readonly prefix: string | null;
}

export interface CreateLabTestRequest extends LabTestGeneral {
  readonly code: string;
}

/** `testDays`: weekday mask, bit 0 = Sunday … bit 6 = Saturday (127 = every day). */
export interface LabTestTiming {
  readonly durationHours: number;
  readonly minDurationHours: number;
  readonly frequencyCheckDays: number | null;
  readonly frequencyAction: FrequencyCheckAction;
  readonly deltaPercent: number | null;
  readonly deltaWindowDays: number | null;
  readonly testDays: number;
}

export interface RangeBandItem {
  readonly sex: GenderRestriction;
  readonly ageFrom: number;
  readonly ageTo: number;
  readonly ageUnit: RangeAgeUnit;
  readonly patientConditionId: string | null;
  readonly low: number | null;
  readonly high: number | null;
  readonly panicLow: number | null;
  readonly panicHigh: number | null;
  readonly reportableLow: number | null;
  readonly reportableHigh: number | null;
  readonly textRange: string | null;
}

export interface ListValueItem {
  readonly value: string;
  readonly isNormal: boolean;
  readonly isCritical: boolean;
  readonly isDefault: boolean;
  readonly isPlaceholder: boolean;
  readonly colour: string | null;
}

export interface ParagraphItem {
  readonly title: string;
  readonly text: string | null;
}

export interface FormulaCheckResult {
  readonly valid: boolean;
  readonly normalized: string | null;
  readonly references: readonly string[];
  readonly code: string | null;
  readonly message: string | null;
  readonly position: number | null;
}

export interface LabTestListItem {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly type: LabTestType;
  readonly clinicalGroupId: string;
  readonly clinicalGroupNameEn: string;
  readonly clinicalGroupNameAr: string;
  readonly unitName: string | null;
  readonly sampleTypeNameEn: string | null;
  readonly sampleTypeNameAr: string | null;
  readonly gender: GenderRestriction;
  readonly isHiddenHelper: boolean;
  readonly isActive: boolean;
}

export interface LabTestDetail extends LabTestGeneral {
  readonly id: string;
  readonly code: string;
  readonly timing: LabTestTiming;
  readonly formulaExpression: string | null;
  readonly formulaReferences: readonly string[];
  readonly rangeBands: readonly RangeBandItem[];
  readonly listValues: readonly ListValueItem[];
  readonly paragraphs: readonly ParagraphItem[];
  readonly isActive: boolean;
}

export interface LabTestListFilter extends CatalogueListFilter {
  readonly type: LabTestType | null;
  readonly clinicalGroupId: string | null;
}

export interface TestLookupItem {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly type: LabTestType;
  readonly clinicalGroupId: string;
  readonly clinicalGroupNameEn: string;
  readonly clinicalGroupNameAr: string;
  readonly unitName: string | null;
}

// ---- configuration per type (mirrors LabTest.Allows* and Service.CarriesSample)

export const TEST_CONFIGURATION = {
  rangeBands: (type: LabTestType): boolean => type === 'Numeric' || type === 'Formula' || type === 'NonNumeric',
  listValues: (type: LabTestType): boolean => type === 'OptionalList' || type === 'RestrictedList',
  formula: (type: LabTestType): boolean => type === 'Formula',
  paragraphs: (type: LabTestType): boolean => type === 'FormattedText',
} as const;

export function serviceCarriesSample(type: ServiceType): boolean {
  return type === 'Test' || type === 'Profile' || type === 'Culture' || type === 'TextReport';
}

/** `Package` holds services, `Additional` nothing, every other type holds tests. */
export function serviceMemberKind(type: ServiceType): ServiceMemberKind | null {
  return type === 'Package' ? 'Service' : type === 'Additional' ? null : 'Test';
}

export const CATALOGUE_A_API = {
  clinicalGroups: '/api/v1/catalogue/clinical-groups',
  sampleTypes: '/api/v1/catalogue/sample-types',
  units: '/api/v1/catalogue/units',
  patientConditions: '/api/v1/catalogue/patient-conditions',
  services: '/api/v1/catalogue/services',
  serviceSequence: '/api/v1/catalogue/services/sequence',
  tests: '/api/v1/catalogue/tests',
  formulaCheck: '/api/v1/catalogue/tests/formula/check',
  lookupServices: '/api/v1/catalogue/lookups/services',
  lookupTests: '/api/v1/catalogue/lookups/tests',
} as const;

/** Permission keys of the catalogue module (shared by both halves; half A uses all five). */
export const CATALOGUE_A_PERMISSIONS = {
  view: 'catalogue.view',
  create: 'catalogue.create',
  edit: 'catalogue.edit',
  manage: 'catalogue.manage',
  export: 'catalogue.export',
} as const;

/** Lookup pages never exceed this size (server limit). */
export const CATALOGUE_LOOKUP_MAX_PAGE_SIZE = 50;
