/** Tenant parameter contracts mirrored from `Platform.Contracts.Settings` (SRS CP-57; bootstrap until the OpenAPI generator lands, 01 §6.2). */

export type SettingType = 'Bool' | 'Int' | 'String';

/** One parameter with its effective text value (stored or default) and the descriptor the Parameters page renders. */
export interface TenantSettingItem {
  readonly key: string;
  readonly type: SettingType;
  readonly value: string;
  readonly defaultValue: string;
  readonly description: string;
  readonly isDefault: boolean;
  readonly updatedAtUtc: string | null;
  readonly updatedByUserId: string | null;
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
} as const;

export type SettingKey = (typeof SETTING_KEYS)[keyof typeof SETTING_KEYS];

export const SETTINGS_API = {
  settings: '/api/v1/settings',
} as const;

export const SETTINGS_PERMISSIONS = {
  view: 'settings.view',
  manage: 'settings.manage',
} as const;
