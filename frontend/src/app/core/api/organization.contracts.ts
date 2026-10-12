/** Organization module contracts mirrored from `Platform.Contracts.Organization` (intake LIS-INT-ORG-001; bootstrap until the OpenAPI generator lands, 01 §6.2). */

import type { Release } from '../release';
import type { SettingKey } from './settings.contracts';

import type { SortRequest } from './paging';
export type { Paged } from './paging';

export interface CreatedResource {
  readonly id: string;
}

// ---- Branches (CP-36) ------------------------------------------------------------------------------------------

export interface BranchListItem {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly countryId: string | null;
  readonly governorateId: string | null;
  readonly phone: string | null;
  readonly email: string | null;
  readonly currency: string;
  readonly isActive: boolean;
}

export interface CreateBranchRequest {
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly countryId: string | null;
  readonly governorateId: string | null;
  readonly phone: string | null;
  readonly email: string | null;
  readonly currency: string;
}

export type UpdateBranchRequest = Omit<CreateBranchRequest, 'code'>;

export interface BranchListFilter extends SortRequest {
  readonly search: string;
  readonly isActive: boolean | null;
  readonly page: number;
  readonly pageSize: number;
}

/** CP-04: a branch the current session may bind to. */
export interface SelectableBranch {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
}

export interface SelectableBranchesResponse {
  readonly currentBranchId: string | null;
  readonly branches: readonly SelectableBranch[];
}

// ---- Roles and privileges (CP-37, SA-06) -----------------------------------------------------------------------

/** `discountCapPercent`: null = the tenant parameter `discountCapPercent`, 0 = no discount, 1–100 = the cap. */
export interface SpecialRights {
  readonly discountCapPercent: number | null;
  readonly canPay: boolean;
  readonly canRefund: boolean;
  readonly canTransfer: boolean;
  readonly canCancel: boolean;
  readonly canModifyVerifiedResult: boolean;
  readonly canPreviewWithoutVerify: boolean;
  readonly canPrintInvoice: boolean;
  readonly canSeeVip: boolean;
}

export type SpecialRightFlag = Exclude<keyof SpecialRights, 'discountCapPercent'>;

export const SPECIAL_RIGHT_FLAGS: readonly SpecialRightFlag[] = [
  'canPay',
  'canRefund',
  'canTransfer',
  'canCancel',
  'canModifyVerifiedResult',
  'canPreviewWithoutVerify',
  'canPrintInvoice',
  'canSeeVip',
];

export const NO_SPECIAL_RIGHTS: SpecialRights = {
  discountCapPercent: 0,
  canPay: false,
  canRefund: false,
  canTransfer: false,
  canCancel: false,
  canModifyVerifiedResult: false,
  canPreviewWithoutVerify: false,
  canPrintInvoice: false,
  canSeeVip: false,
};

/** Result rights on one clinical group in one branch (`branchId` null = every branch). */
export interface ResultRight {
  readonly clinicalGroupId: string;
  readonly branchId: string | null;
  readonly canSee: boolean;
  readonly canReview: boolean;
  readonly canVerify: boolean;
  readonly canEdit: boolean;
  readonly canPrint: boolean;
}

export type ResultRightFlag = 'canSee' | 'canReview' | 'canVerify' | 'canEdit' | 'canPrint';

export const RESULT_RIGHT_FLAGS: readonly ResultRightFlag[] = ['canSee', 'canReview', 'canVerify', 'canEdit', 'canPrint'];

export interface RoleListItem {
  readonly id: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly description: string | null;
  readonly permissionCount: number;
  readonly memberCount: number;
  readonly isActive: boolean;
}

export interface RoleDetail {
  readonly id: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly description: string | null;
  readonly isActive: boolean;
  readonly permissionKeys: readonly string[];
  readonly specialRights: SpecialRights;
  readonly resultRights: readonly ResultRight[];
  readonly memberCount: number;
  readonly holdsPrimaryAdministrator: boolean;
}

export interface CreateRoleRequest {
  readonly nameEn: string;
  readonly nameAr: string;
  readonly description: string | null;
  readonly permissionKeys: readonly string[];
}

export interface UpdateRoleRequest {
  readonly nameEn: string;
  readonly nameAr: string;
  readonly description: string | null;
}

/** `sort`: `nameEn` or `nameAr` (B-03, server allow-list). */
export interface RoleListFilter extends SortRequest {
  readonly search: string;
  readonly isActive: boolean | null;
  readonly page: number;
  readonly pageSize: number;
}

export interface PermissionModule {
  readonly key: string;
  readonly displayName: string;
  readonly actions: readonly string[];
  readonly platformOnly: boolean;
}

export interface PermissionCatalogue {
  readonly modules: readonly PermissionModule[];
  readonly actions: readonly string[];
}

// ---- Users (CP-38, SA-07) --------------------------------------------------------------------------------------

export type UserStatus = 'Active' | 'Locked' | 'Expired' | 'Inactive';

export const USER_STATUSES: readonly UserStatus[] = ['Active', 'Locked', 'Expired', 'Inactive'];

export interface UserListItem {
  readonly userId: string;
  readonly username: string;
  readonly fullNameEn: string;
  readonly fullNameAr: string;
  readonly email: string;
  readonly mobile: string | null;
  readonly designation: string | null;
  readonly roleId: string | null;
  readonly roleNameEn: string | null;
  readonly roleNameAr: string | null;
  /** 0 = every active branch. */
  readonly branchCount: number;
  readonly status: UserStatus;
  readonly mustChangePassword: boolean;
  readonly passwordExpiresAtUtc: string | null;
  readonly isModified: boolean;
  readonly isPrimaryAdministrator: boolean;
  /** Codes of the assigned branches (empty = every branch; UAT R1-06); absent from older servers. */
  readonly branchCodes?: readonly string[] | null;
}

export interface UserOverrides {
  readonly addedPermissionKeys: readonly string[];
  readonly removedPermissionKeys: readonly string[];
  readonly specialRights: SpecialRights | null;
}

export interface EffectivePrivileges {
  readonly permissionKeys: readonly string[];
  readonly specialRights: SpecialRights;
  readonly resultRights: readonly ResultRight[];
}

export interface UserDetail {
  readonly userId: string;
  readonly membershipId: string;
  readonly username: string;
  readonly fullNameEn: string;
  readonly fullNameAr: string;
  readonly email: string;
  readonly mobile: string | null;
  readonly employeeCode: string;
  readonly designation: string | null;
  readonly shifts: readonly string[];
  readonly preferredLanguage: string;
  readonly roleId: string | null;
  readonly roleNameEn: string | null;
  readonly roleNameAr: string | null;
  readonly branchIds: readonly string[];
  readonly status: UserStatus;
  readonly isActive: boolean;
  readonly isLocked: boolean;
  readonly lockoutEndUtc: string | null;
  readonly mustChangePassword: boolean;
  readonly passwordExpiresAtUtc: string | null;
  readonly passwordChangedAtUtc: string;
  readonly signatureFileId: string | null;
  readonly isPrimaryAdministrator: boolean;
  readonly isModified: boolean;
  readonly belongsToOtherTenants: boolean;
  readonly overrides: UserOverrides;
  readonly effective: EffectivePrivileges;
  /** The role's own keys and special rights (the baseline the overrides tab compares with); empty when the role is inactive. */
  readonly rolePermissionKeys: readonly string[];
  readonly roleSpecialRights: SpecialRights;
}

/** The password is a `[CredentialField]`: the envelope interceptor seals it (see the integrator note in the report). */
export interface CreateUserRequest {
  readonly username: string;
  readonly password: string;
  readonly mustChangePassword: boolean;
  readonly fullNameEn: string;
  readonly fullNameAr: string;
  readonly email: string;
  readonly mobile: string | null;
  readonly designation: string | null;
  readonly shifts: readonly string[];
  readonly roleId: string | null;
  readonly branchIds: readonly string[];
  /** B-02: `true` = every active branch (`branchIds` empty), `false` = only `branchIds` (at least one, else ORG-018). */
  readonly allBranches?: boolean;
  readonly passwordExpiresAtUtc: string | null;
  readonly employeeCode: string | null;
  readonly preferredLanguage: string;
}

export interface UpdateUserRequest {
  readonly fullNameEn: string;
  readonly fullNameAr: string;
  readonly email: string;
  readonly mobile: string | null;
  readonly designation: string | null;
  readonly shifts: readonly string[];
  readonly preferredLanguage: string;
  readonly passwordExpiresAtUtc: string | null;
  readonly mustChangePassword: boolean;
}

/** `temporaryPassword` is a `[CredentialField]`. */
export interface ResetUserPasswordRequest {
  readonly temporaryPassword: string;
  readonly mustChangePassword: boolean;
}

export interface SetUserOverridesRequest {
  readonly addedPermissionKeys: readonly string[];
  readonly removedPermissionKeys: readonly string[];
  readonly specialRights: SpecialRights | null;
}

/** `sort`: `username`, `nameEn` or `nameAr` (B-03, server allow-list). */
export interface UserListFilter extends SortRequest {
  readonly search: string;
  readonly roleId: string | null;
  readonly status: UserStatus | null;
  readonly page: number;
  readonly pageSize: number;
}

export interface MyPrivileges {
  readonly userId: string;
  readonly tenantId: string | null;
  readonly branchId: string | null;
  readonly isPrimaryAdministrator: boolean;
  readonly mustChangePassword: boolean;
  readonly passwordExpiresAtUtc: string | null;
  readonly privileges: EffectivePrivileges;
}

// ---- Lookups of other modules (read only) ----------------------------------------------------------------------

/** A row of a reference list of another module (countries, governorates, clinical groups), normalised by the feature API. */
export interface LookupOption {
  readonly id: string;
  readonly code: string | null;
  readonly nameEn: string;
  readonly nameAr: string;
  /** The parent (the country of a governorate). */
  readonly parentId: string | null;
}

// ---- Parameters (CP-57) ----------------------------------------------------------------------------------------

export type ParameterInput = 'switch' | 'number' | 'select' | 'text';

export interface ParameterDescriptor {
  readonly key: SettingKey;
  readonly input: ParameterInput;
  readonly min?: number;
  readonly max?: number;
  readonly options?: readonly string[];
  readonly maxLength?: number;
  /** Shown only in the Super Admin portal (platform defaults, SA-19). */
  readonly platformOnly?: boolean;
  /**
   * A laboratory may change it only from this release (shown locked before). Auto-collect / auto-receive: Release 1 has
   * no sample pages, so switching either off would leave samples uncollected or not received and block result entry
   * (BR-03, RES-010) with no screen to fix it (decision 2026-10-12).
   */
  readonly editableFrom?: Release;
}

export interface ParameterGroup {
  readonly id: string;
  /** The release in which a laboratory sees the group (the Super Admin portal sees every group). */
  readonly release: Release;
  readonly parameters: readonly ParameterDescriptor[];
}

/**
 * The 25 foundation keys grouped by topic with their input kind and bounds, mirroring the descriptors of
 * `Platform.Domain.Settings.SettingKeys` (the server validates again: `SET-001`, `SET-002`).
 */
export const PARAMETER_GROUPS: readonly ParameterGroup[] = [
  {
    id: 'registration',
    release: 'R1',
    parameters: [
      { key: 'mandatoryMobile', input: 'switch' },
      { key: 'mandatoryDoctor', input: 'switch' },
      { key: 'mandatoryNationalId', input: 'switch' },
      { key: 'similarNameWarning', input: 'switch' },
      { key: 'discountCapPercent', input: 'number', min: 0, max: 100 },
      { key: 'accessionFormat', input: 'text', maxLength: 200 },
      { key: 'timeZone', input: 'text', maxLength: 64 },
    ],
  },
  {
    id: 'printing',
    release: 'R1',
    parameters: [
      { key: 'autoPrintLabels', input: 'switch' },
      { key: 'autoPrintJobOrder', input: 'switch' },
      { key: 'autoPrintReceipt', input: 'switch' },
      { key: 'printUnverified', input: 'switch' },
      { key: 'labelCopies', input: 'number', min: 1, max: 10 },
      { key: 'resultLanguage', input: 'select', options: ['en', 'ar'] },
    ],
  },
  {
    id: 'samples',
    release: 'R1',
    parameters: [
      { key: 'autoCollect', input: 'switch', editableFrom: 'R2' },
      { key: 'autoReceive', input: 'switch', editableFrom: 'R2' },
      { key: 'newBarcodeOnReject', input: 'switch' },
      { key: 'reviewBeforeVerify', input: 'switch' },
      { key: 'delayWarnPercent', input: 'number', min: 0, max: 1000 },
      { key: 'delayLatePercent', input: 'number', min: 0, max: 1000 },
    ],
  },
  {
    id: 'security',
    release: 'R1',
    parameters: [
      { key: 'sessionTimeoutMinutes', input: 'number', min: 1, max: 720 },
      { key: 'otpLength', input: 'number', min: 4, max: 10 },
      { key: 'passwordMinLength', input: 'number', min: 8, max: 64 },
      { key: 'defaultLanguage', input: 'select', options: ['en', 'ar'] },
      { key: 'defaultCurrency', input: 'text', maxLength: 3 },
    ],
  },
  {
    id: 'subscription',
    release: 'R2',
    parameters: [
      { key: 'trialDays', input: 'number', min: 0, max: 365, platformOnly: true },
      { key: 'masterDataUpdatePolicy', input: 'select', options: ['push', 'propose', 'ignore'] },
    ],
  },
];

// ---- Endpoints and permissions ---------------------------------------------------------------------------------

export const ORGANIZATION_API = {
  branches: '/api/v1/organization/branches',
  selectableBranches: '/api/v1/organization/branches/selectable',
  roles: '/api/v1/organization/roles',
  users: '/api/v1/organization/users',
  permissionCatalogue: '/api/v1/organization/permission-catalogue',
  myPrivileges: '/api/v1/organization/me/privileges',
  settings: '/api/v1/settings',
  /** Lookups owned by other modules (agents `plat` and `cat`). */
  countries: '/api/v1/public/countries',
  governorates: '/api/v1/public/governorates',
  clinicalGroups: '/api/v1/catalogue/clinical-groups',
} as const;

/** Permission keys the pages gate their affordances on (the server decides, D-04). */
export const ORGANIZATION_PERMISSIONS = {
  view: 'organization.view',
  create: 'organization.create',
  edit: 'organization.edit',
  manage: 'organization.manage',
  /** CSV export of the branches, roles and users registers (UAT R1-05). */
  export: 'organization.export',
  usersView: 'identity.view',
  usersCreate: 'identity.create',
  usersEdit: 'identity.edit',
  usersManage: 'identity.manage',
  settingsView: 'settings.view',
  settingsManage: 'settings.manage',
} as const;

/** Username rule of a staff account (INV-ORG-013): 4–30 lower-case letters, digits, dots and underscores. */
export const STAFF_USERNAME_PATTERN = /^[a-z0-9._]{4,30}$/;

export const PASSWORD_MAX_LENGTH = 128;
