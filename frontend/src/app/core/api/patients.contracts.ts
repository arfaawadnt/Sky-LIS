/** Patients module contracts mirrored from `Platform.Contracts.Patients` (bootstrap until the OpenAPI generator lands, 01 §6.2). Dates of birth are ISO dates (`yyyy-MM-dd`). */

export type { Paged } from './paging';

export type PatientTitle = 'Mr' | 'Mrs' | 'Miss' | 'Ms' | 'Dr' | 'Master' | 'Baby' | 'Prof';

export const PATIENT_TITLES: readonly PatientTitle[] = ['Mr', 'Mrs', 'Miss', 'Ms', 'Dr', 'Master', 'Baby', 'Prof'];

export type PatientGender = 'Male' | 'Female';

export const PATIENT_GENDERS: readonly PatientGender[] = ['Male', 'Female'];

export type PatientStatus = 'Active' | 'Merged';

export const PATIENT_STATUSES: readonly PatientStatus[] = ['Active', 'Merged'];

/** How a similar file resembles the typed patient (BR-01): Exact = name and mobile, Mobile = same mobile, Name = similar name. */
export type SimilarMatch = 'Exact' | 'Mobile' | 'Name';

export interface PatientAge {
  readonly years: number;
  readonly months: number;
  readonly days: number;
}

/** T1. A date of birth, or an age when the date is unknown; `isVip` only with `patients.viewConfidential` (send `null` otherwise). */
export interface RegisterPatientRequest {
  readonly title: PatientTitle;
  readonly fullName: string;
  readonly nameAr: string | null;
  readonly gender: PatientGender;
  readonly dateOfBirth: string | null;
  readonly age: PatientAge | null;
  readonly patientConditionId: string | null;
  readonly mobile: string | null;
  readonly nationalId: string | null;
  readonly nationality: string | null;
  readonly email: string | null;
  readonly governorateId: string | null;
  readonly city: string | null;
  readonly address: string | null;
  readonly comments: string | null;
  readonly isVip: boolean | null;
}

/** T2: the same fields plus the row version of the detail the user edited (stale → CONCURRENCY-409). */
export interface UpdatePatientRequest extends RegisterPatientRequest {
  readonly rowVersion: number | null;
}

export interface CreatedPatient {
  readonly id: string;
  readonly number: string;
}

export interface PatientListItem {
  readonly id: string;
  readonly number: string;
  readonly title: PatientTitle;
  readonly fullName: string;
  readonly nameAr: string | null;
  readonly gender: PatientGender;
  /** The known or the estimated date of birth (see `dateOfBirthIsEstimated`). */
  readonly dateOfBirth: string | null;
  readonly dateOfBirthIsEstimated: boolean;
  /** Computed by the server for today. */
  readonly age: PatientAge;
  readonly mobile: string | null;
  readonly nationalId: string | null;
  readonly patientConditionId: string | null;
  /** `null` for users without `patients.viewConfidential`. */
  readonly isVip: boolean | null;
  readonly status: PatientStatus;
  readonly mergedIntoPatientId: string | null;
  readonly mergedIntoNumber: string | null;
  readonly registeredOn: string;
}

export interface PatientMergeView {
  readonly survivorId: string;
  readonly survivorNumber: string;
  readonly mergedAtUtc: string;
  readonly mergedByUserId: string;
  readonly mergedByName: string | null;
  readonly reason: string;
}

export interface MergedPatientReference {
  readonly id: string;
  readonly number: string;
  readonly mergedAtUtc: string | null;
}

export interface PatientDetail {
  readonly id: string;
  readonly number: string;
  readonly title: PatientTitle;
  readonly fullName: string;
  readonly nameAr: string | null;
  readonly gender: PatientGender;
  readonly dateOfBirth: string | null;
  readonly dateOfBirthIsEstimated: boolean;
  readonly age: PatientAge;
  readonly statedAge: PatientAge | null;
  readonly ageRecordedOn: string | null;
  readonly registeredOn: string;
  readonly patientConditionId: string | null;
  readonly mobile: string | null;
  readonly nationalId: string | null;
  readonly nationality: string | null;
  readonly email: string | null;
  readonly governorateId: string | null;
  readonly city: string | null;
  readonly address: string | null;
  readonly comments: string | null;
  readonly isVip: boolean | null;
  readonly status: PatientStatus;
  readonly merge: PatientMergeView | null;
  readonly mergedFiles: readonly MergedPatientReference[];
  readonly createdAtUtc: string;
  readonly createdByName: string | null;
  readonly modifiedAtUtc: string | null;
  readonly modifiedByName: string | null;
  readonly rowVersion: number;
}

export interface SimilarPatient {
  readonly id: string;
  readonly number: string;
  readonly title: PatientTitle;
  readonly fullName: string;
  readonly nameAr: string | null;
  readonly gender: PatientGender;
  readonly dateOfBirth: string | null;
  readonly dateOfBirthIsEstimated: boolean;
  readonly age: PatientAge;
  readonly mobile: string | null;
  readonly nationalId: string | null;
  readonly match: SimilarMatch;
}

/** At most 10 candidates; `warningEnabled` is the tenant parameter `similarNameWarning`. */
export interface SimilarPatientsResponse {
  readonly warningEnabled: boolean;
  readonly candidates: readonly SimilarPatient[];
}

/** Tenant parameters a patient form honours (CP-57). */
export interface PatientFormSettings {
  readonly mandatoryMobile: boolean;
  readonly mandatoryNationalId: boolean;
  readonly similarNameWarning: boolean;
}

/** T3 on `POST /patients/{survivorId}/merge`. */
export interface MergePatientsRequest {
  readonly mergedPatientId: string;
  readonly reason: string;
  readonly confirmGenderMismatch: boolean;
}

export interface PatientDocumentItem {
  readonly id: string;
  readonly patientId: string;
  /** The file the document was attached to (a file merged into this one shows its own number). */
  readonly patientNumber: string;
  readonly fileName: string;
  readonly contentType: string;
  readonly sizeBytes: number;
  readonly note: string | null;
  readonly uploadedByUserId: string;
  readonly uploadedByName: string | null;
  readonly uploadedAtUtc: string;
  readonly isActive: boolean;
  readonly deactivatedByName: string | null;
  readonly deactivatedAtUtc: string | null;
  readonly deactivationReason: string | null;
}

export interface DeactivatePatientDocumentRequest {
  readonly reason: string;
}

export interface PatientListFilter {
  readonly q: string;
  readonly gender: PatientGender | null;
  /** Sent only by users holding `patients.viewConfidential` (PAT-031 otherwise). */
  readonly vip: boolean | null;
  readonly status: PatientStatus | null;
  readonly page: number;
  readonly pageSize: number;
}

/**
 * What the patient picker emits (and Registration consumes): enough to show the file in a visit header and to order
 * services (gender, age). `isVip` is `null` when the user may not see it.
 */
export interface PatientSummary {
  readonly id: string;
  readonly number: string;
  readonly title: PatientTitle;
  readonly fullName: string;
  readonly nameAr: string | null;
  readonly gender: PatientGender;
  readonly dateOfBirth: string | null;
  readonly dateOfBirthIsEstimated: boolean;
  readonly age: PatientAge;
  readonly mobile: string | null;
  readonly nationalId: string | null;
  readonly isVip: boolean | null;
}

/** A country of the platform list (anonymous `/api/v1/public/countries`); `mobileLength` is the local mobile length when published. */
export interface CountryOption {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly mobileLength: number | null;
}

/** A governorate of the platform list (anonymous `/api/v1/public/governorates`, every country when unfiltered). */
export interface GovernorateOption {
  readonly id: string;
  readonly nameEn: string;
  readonly nameAr: string;
}

/** A patient condition of the catalogue (`/api/v1/catalogue/patient-conditions`, agent `cat`). */
export interface ConditionOption {
  readonly id: string;
  readonly nameEn: string;
  readonly nameAr: string;
}

/** A visit of the file as the Registration module (wave 2) will list it; read tolerantly until that API exists. */
export interface PatientVisitItem {
  readonly id: string;
  readonly accession: string;
  readonly registeredAtUtc: string | null;
  readonly status: string | null;
  readonly branchName: string | null;
  readonly servicesCount: number | null;
}

export const PATIENTS_API = {
  patients: '/api/v1/patients',
  documents: '/api/v1/patients/documents',
  countries: '/api/v1/public/countries',
  governorates: '/api/v1/public/governorates',
  conditions: '/api/v1/catalogue/patient-conditions',
  visits: '/api/v1/registration/visits',
} as const;

/** Permission keys the module uses, as the catalogue defines them. */
export const PATIENTS_PERMISSIONS = {
  view: 'patients.view',
  create: 'patients.create',
  edit: 'patients.edit',
  manage: 'patients.manage',
  viewConfidential: 'patients.viewConfidential',
  export: 'patients.export',
  filesView: 'files.view',
  filesCreate: 'files.create',
  filesManage: 'files.manage',
  registrationCreate: 'registration.create',
  registrationView: 'registration.view',
} as const;

/** Upload limits of patient documents (INV-PAT-008); the server re-checks extension, signature and size. */
export const PATIENT_DOCUMENT_RULES = {
  maxBytes: 10 * 1024 * 1024,
  extensions: ['pdf', 'jpg', 'jpeg', 'png'],
  accept: '.pdf,.jpg,.jpeg,.png,application/pdf,image/jpeg,image/png',
} as const;
