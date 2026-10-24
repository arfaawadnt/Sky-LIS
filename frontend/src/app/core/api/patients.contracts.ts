/** Patients module contracts mirrored from `Platform.Contracts.Patients` (bootstrap until the OpenAPI generator lands, 01 §6.2). Dates of birth are ISO dates (`yyyy-MM-dd`). */

import type { SortRequest } from './paging';

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
  /** The picked city, district and area of the platform's location hierarchy (A2): each must chain to the governorate (PAT-014…016); omitted = none. */
  readonly cityId?: string | null;
  readonly districtId?: string | null;
  readonly areaId?: string | null;
  /** A second phone (C3): cleaned like the mobile, ≤ 16 digits, no uniqueness (PAT-017). */
  readonly secondPhone?: string | null;
}

/**
 * T2: the same fields plus the row version of the detail the user edited (stale → CONCURRENCY-409). `visitId`: the visit the
 * user edits from (Search registrations, CP-10): the result-status lock on the identity fields is judged on it, on every visit
 * of the patient when absent (PAT-032; see `PatientDemographicsLock`).
 */
export interface UpdatePatientRequest extends RegisterPatientRequest {
  readonly rowVersion: number | null;
  readonly visitId?: string | null;
}

/** The result statuses a demographics edit limit is expressed on, lowest first (the `DemographicsEditUpTo` special right, CP-37). */
export type ResultStatusLimit = 'Ordered' | 'Complete' | 'Reviewed' | 'Verified' | 'Printed';

/**
 * `GET /patients/{id}/demographics-lock?visitId=` (change request 2026-10-13 item 1): the highest live result status of the
 * visit (of the patient without a visit), the user's own limit, and whether the identity fields (title, names, gender, date of
 * birth or age, condition, national id, nationality) may still change; contact fields always may with `patients.edit`.
 */
export interface PatientDemographicsLock {
  readonly highestStatus: ResultStatusLimit;
  readonly canEditIdentity: boolean;
  readonly editableUpTo: ResultStatusLimit;
}

export interface CreatedPatient {
  readonly id: string;
  readonly number: string;
}

export interface PatientListItem {
  readonly id: string;
  readonly number: string;
  readonly title: PatientTitle;
  /** Empty when `identityMasked`. */
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
  /** CP-11 "visit count" (from Registration; UAT A-09). Optional only for lookups that do not carry it. */
  readonly visitCount?: number;
  /** CP-11 "last visit": the latest visit's registration instant (UTC), `null` without visits. */
  readonly lastVisitAtUtc?: string | null;
  /**
   * A VIP file listed for a user without the See-VIP special right (validation A-03): the server sends no name, Arabic
   * name, mobile or national id; show `patients.vipMasked` instead. Opening the file shows the real identity.
   */
  readonly identityMasked?: boolean;
  /** C3: the second phone (null when masked). */
  readonly secondPhone?: string | null;
  /** A2: the picked city / district / area with their names; null when none is set. */
  readonly location?: PatientLocation | null;
}

/**
 * The picked city, district and area of a file (A2): ids and bilingual names from the platform's location hierarchy; a level not
 * set is null; the whole record is null when no level is set. The free-text `city` of the file carries the picked city's English name too.
 */
export interface PatientLocation {
  readonly cityId: string | null;
  readonly cityNameEn: string | null;
  readonly cityNameAr: string | null;
  readonly districtId: string | null;
  readonly districtNameEn: string | null;
  readonly districtNameAr: string | null;
  readonly areaId: string | null;
  readonly areaNameEn: string | null;
  readonly areaNameAr: string | null;
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
  /** C3: the second phone. */
  readonly secondPhone?: string | null;
  /** A2: the picked city / district / area with their names; null when none is set. */
  readonly location?: PatientLocation | null;
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
  /**
   * A VIP file found for a user without the See-VIP special right (validation R-06): listed so no duplicate file is
   * created, with the number and the match; no name, Arabic name, mobile or national id. Show `patients.vipMasked`.
   */
  readonly identityMasked?: boolean;
  /** C3: the second phone (null when masked); a candidate matches on it like on the mobile. */
  readonly secondPhone?: string | null;
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
  /** The laboratory's country (ISO code of the client record's country): the mobile country the form opens on (F-08). */
  readonly countryCode?: string | null;
  readonly countryMobileLength?: number | null;
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

/** `sort`: `number` or `name` (B-03, server allow-list); absent = newest files first. */
export interface PatientListFilter extends SortRequest {
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
  /** `+20`: the dial code the mobile control shows (LIS-CR-007 C-05); absent when the list does not give one. */
  readonly dialCode?: string | null;
}

/** A governorate of the platform list (anonymous `/api/v1/public/governorates`, every country when unfiltered). */
export interface GovernorateOption {
  readonly id: string;
  readonly nameEn: string;
  readonly nameAr: string;
  /** The country the governorate belongs to (matches `CountryOption.id`); the national-id fill only looks in Egypt's. */
  readonly countryId?: string | null;
}

/** An active city of a governorate (anonymous `/api/v1/public/cities?governorateId=`, A2). */
export interface CityOption {
  readonly id: string;
  readonly governorateId: string;
  readonly nameEn: string;
  readonly nameAr: string;
}

/** An active district of a city (anonymous `/api/v1/public/districts?cityId=`). */
export interface DistrictOption {
  readonly id: string;
  readonly cityId: string;
  readonly nameEn: string;
  readonly nameAr: string;
}

/** An active area of a district (anonymous `/api/v1/public/areas?districtId=`). */
export interface AreaOption {
  readonly id: string;
  readonly districtId: string;
  readonly nameEn: string;
  readonly nameAr: string;
}

/** An active patient condition (`GET /api/v1/patients/conditions`, `patients.view`; round 2 R2-04: the catalogue's own list needs `catalogue.view`). */
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
  /** The branch's Arabic name when the registration API sent it (validation re-test R3-01). */
  readonly branchNameAr?: string | null;
  readonly servicesCount: number | null;
}

export const PATIENTS_API = {
  patients: '/api/v1/patients',
  documents: '/api/v1/patients/documents',
  countries: '/api/v1/public/countries',
  governorates: '/api/v1/public/governorates',
  cities: '/api/v1/public/cities',
  districts: '/api/v1/public/districts',
  areas: '/api/v1/public/areas',
  conditions: '/api/v1/patients/conditions',
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
