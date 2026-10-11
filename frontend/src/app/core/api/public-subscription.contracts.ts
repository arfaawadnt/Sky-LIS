/**
 * The public subscription wizard (SRS CP-02), mirrored from `Platform.Contracts.PlatformAdmin` (SubscriptionContracts).
 * Anonymous endpoints, rate-limited like sign-in; one hour from `start` to `complete`. The wizard pages are built by
 * the account module from this file.
 *
 * Flow: `GET countries` → `GET governorates?countryId` → `POST start` (code sent to the manager's mobile, SUB-…) →
 * `POST verify` (or `POST resend`) → `GET check-username` while typing → `POST complete` (password sealed by the
 * credential envelope; the confirmation is compared in the browser) → `portalUrl`.
 */

export interface PublicCountry {
  readonly id: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly isoCode: string;
  /** `+20` */
  readonly dialCode: string;
  /** Digits of a national mobile number without the trunk prefix (Egypt 10). */
  readonly mobileLength: number;
  readonly flag: string | null;
  readonly currencyCode: string;
  readonly language: string;
}

export interface PublicGovernorate {
  readonly id: string;
  readonly countryId: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly district: string | null;
}

export interface StartSubscriptionRequest {
  readonly labNameEn: string;
  readonly labNameAr: string;
  readonly countryId: string;
  readonly governorateId: string;
  readonly address: string;
  /** Local (`01012345678`) or international (`+20 101 234 5678`); validated by the country's length (GEO-010). */
  readonly mobile: string;
  readonly licenceNo: string | null;
  readonly managerName: string;
  readonly managerMobile: string;
  readonly email: string | null;
  /** `en` or `ar`: the language of the SMS messages. */
  readonly language: string | null;
}

export interface SubscriptionStarted {
  readonly subscriptionId: string;
  readonly expiresAtUtc: string;
  /** `+201******678` */
  readonly maskedMobile: string;
  readonly codeLifetimeSeconds: number;
}

export interface VerifySubscriptionRequest {
  readonly subscriptionId: string;
  readonly code: string;
}

export interface ResendSubscriptionCodeRequest {
  readonly subscriptionId: string;
}

/** `password` is a `[CredentialField]`: list `{ method: 'POST', path: PUBLIC_SUBSCRIPTION_API.complete, fields: ['password'] }` in `credential-fields.ts`. */
export interface CompleteSubscriptionRequest {
  readonly subscriptionId: string;
  readonly username: string;
  readonly password: string;
}

export interface SubscriptionCompletedResponse {
  readonly tenantId: string;
  readonly portalUrl: string;
  readonly username: string;
}

export interface UsernameAvailability {
  readonly username: string;
  readonly valid: boolean;
  readonly available: boolean;
}

export const PUBLIC_SUBSCRIPTION_API = {
  countries: '/api/v1/public/countries',
  governorates: '/api/v1/public/governorates',
  start: '/api/v1/public/subscriptions/start',
  resend: '/api/v1/public/subscriptions/resend',
  verify: '/api/v1/public/subscriptions/verify',
  complete: '/api/v1/public/subscriptions/complete',
  checkUsername: '/api/v1/public/subscriptions/check-username',
} as const;

/**
 * The password rule of the wizard (decision 2026-10-09, architecture pack 02 §6.1): at least the platform's
 * `passwordMinLength` (default 8), at most 128, no composition rules, must not contain the username.
 */
export const SUBSCRIPTION_PASSWORD_RULE = { minLength: 8, maxLength: 128 } as const;

/**
 * Refusal codes the wizard shows: SUB-001 expired (start again), SUB-002 not pending, SUB-003 not verified,
 * SUB-004 too many codes, SUB-010 wrong code, SUB-404 unknown request, GEO-010 laboratory mobile length, GEO-014 manager
 * mobile length, GEO-011/012/013 place, CLI-040 password too short, CLI-041 password contains the username, CLI-042 username shape,
 * CLI-101 username taken, RATE-429 too many attempts.
 */
export const SUBSCRIPTION_CODES = {
  expired: 'SUB-001',
  notPending: 'SUB-002',
  notVerified: 'SUB-003',
  tooManyCodes: 'SUB-004',
  codeRejected: 'SUB-010',
  notFound: 'SUB-404',
  mobileInvalid: 'GEO-010',
  managerMobileInvalid: 'GEO-014',
  passwordTooShort: 'CLI-040',
  passwordContainsUsername: 'CLI-041',
  usernameInvalid: 'CLI-042',
  usernameTaken: 'CLI-101',
} as const;
