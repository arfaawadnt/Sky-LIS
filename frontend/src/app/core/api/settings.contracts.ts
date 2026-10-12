/** Tenant parameter contracts mirrored from `Platform.Contracts.Settings` (SRS CP-57; bootstrap until the OpenAPI generator lands, 01 §6.2). */

export type SettingType = 'Bool' | 'Int' | 'String';

/**
 * Where an effective value comes from (`Platform.Contracts.Settings.TenantSettingSources`): the laboratory stored it, the
 * platform parameters (SA-19) store it (the platform's own row, or the value a laboratory inherits), or nobody did and the
 * code default applies.
 */
export type TenantSettingSource = 'Laboratory' | 'Platform' | 'Default';

/**
 * One parameter and the descriptor the Parameters page renders. `value` is the effective value, exactly as the modules
 * read it: the tenant's own value, else the platform's, else the code default (validation 2026-10-11 C-09); `isDefault`
 * is true when the tenant holds no value of its own; `inheritedValue` is what the tenant reads without one (the value a
 * "use the default" action returns to). The server always sends `source` and `inheritedValue`; they are optional here only
 * so the fixtures written before them still compile.
 */
export interface TenantSettingItem {
  readonly key: string;
  readonly type: SettingType;
  readonly value: string;
  readonly defaultValue: string;
  readonly description: string;
  readonly isDefault: boolean;
  readonly updatedAtUtc: string | null;
  readonly updatedByUserId: string | null;
  readonly source?: TenantSettingSource;
  readonly inheritedValue?: string;
}

export interface TenantSettingsResponse {
  readonly settings: readonly TenantSettingItem[];
}

/** A value to store, in its text form; the server validates it against the key (`SET-001` unknown key, `SET-002` invalid value). */
export interface SettingValueItem {
  readonly key: string;
  readonly value: string;
}

export interface UpdateTenantSettingsRequest {
  readonly settings: readonly SettingValueItem[];
}

/** The catalogue keys (`Platform.Domain.Settings.SettingKeys`), so pages never spell a key by hand. */
export const SETTING_KEYS = {
  mandatoryMobile: 'mandatoryMobile',
  mandatoryDoctor: 'mandatoryDoctor',
  mandatoryNationalId: 'mandatoryNationalId',
  similarNameWarning: 'similarNameWarning',
  discountCapPercent: 'discountCapPercent',
  accessionFormat: 'accessionFormat',
  autoPrintLabels: 'autoPrintLabels',
  autoPrintJobOrder: 'autoPrintJobOrder',
  autoPrintReceipt: 'autoPrintReceipt',
  printUnverified: 'printUnverified',
  labelCopies: 'labelCopies',
  resultLanguage: 'resultLanguage',
  autoCollect: 'autoCollect',
  autoReceive: 'autoReceive',
  newBarcodeOnReject: 'newBarcodeOnReject',
  reviewBeforeVerify: 'reviewBeforeVerify',
  delayWarnPercent: 'delayWarnPercent',
  delayLatePercent: 'delayLatePercent',
  sessionTimeoutMinutes: 'sessionTimeoutMinutes',
  otpLength: 'otpLength',
  passwordMinLength: 'passwordMinLength',
  trialDays: 'trialDays',
  defaultCurrency: 'defaultCurrency',
  defaultLanguage: 'defaultLanguage',
  masterDataUpdatePolicy: 'masterDataUpdatePolicy',
  timeZone: 'timeZone',
} as const;

export type SettingKey = (typeof SETTING_KEYS)[keyof typeof SETTING_KEYS];

export const SETTINGS_API = {
  settings: '/api/v1/settings',
} as const;

export const SETTINGS_PERMISSIONS = {
  view: 'settings.view',
  manage: 'settings.manage',
} as const;
