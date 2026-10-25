/**
 * Auth contracts mirrored from `Platform.Contracts.Identity` (camelCase as the API serialises them).
 * Bootstrap only: 01 §6.2 generates `core/api` from the OpenAPI document once the API publishes one, and CI
 * then fails on any drift (pitfall P-62). Nothing here is decoded from a token (D-02).
 */
import type { ResultsDensity } from './account.contracts';

export interface AuthProfile {
  readonly userId: string;
  /** The name in the user's preferred language; `displayNameEn` / `displayNameAr` the member's full name in each language (validation v0.5.0 R-12). Pick with `displayNameFor`. */
  readonly displayName: string;
  readonly displayNameEn?: string | null;
  readonly displayNameAr?: string | null;
  readonly tenantId: string | null;
  /** The role's English name; `roleNameAr` its Arabic name (validation v0.5.0 B-09). Pick with `roleNameFor`. */
  readonly roleName: string | null;
  readonly roleNameAr?: string | null;
  readonly permissions: readonly string[];
  readonly language: string;
  readonly scope: string;
  /** The branch the session is bound to (CP-04); `null` until selected. The API always sends it; optional here only until the generator lands. */
  readonly branchId?: string | null;
  /** The branches the membership may choose from; empty = unrestricted (the organization module lists the tenant's branches). */
  readonly branchIds?: readonly string[];
  /** An administrator reset the password: the SPA sends the user to the change-password page first. */
  readonly mustChangePassword?: boolean;
  /**
   * Validation F-01: the idle timeout of the session's tenant (`sessionTimeoutMinutes`, 1–720; the platform's value when the
   * laboratory set none, else 30). The idle watcher warns a minute before it and signs out at it; the server refuses a
   * refresh after it with `AUTH-017`. Optional only until the generator lands; absent means 30.
   */
  readonly sessionTimeoutMinutes?: number | null;
  /** CP-19 (results page, 2026-10-14): the member's row density of the results grid; absent or null = comfortable. */
  readonly resultsDensity?: ResultsDensity | null;
  /** CP-38: the session's membership is the primary administrator of its laboratory (the account that subscribed or was provisioned as the main administrator). */
  readonly isPrimaryAdministrator?: boolean;
  /**
   * LIS-CR-005: the slug of the session's laboratory (its sign-in address is `/lab/<slug>`); `null` for the platform tenant.
   * The SPA sends a signed-out or timed-out user back to that address. Optional only until the generator lands.
   */
  readonly portalSlug?: string | null;
  /**
   * LIS-CR-012 (C-16): the subscription state of the session's laboratory, read with the session; absent or null when there is
   * nothing to say. The shell shows `app-subscription-banner` (administrators) and `app-subscription-modal` from it.
   */
  readonly subscriptionNotice?: SubscriptionNotice | null;
}

/** LIS-CR-012: how urgent the subscription is (`Ok` never reaches the SPA: the notice is then null). */
export type SubscriptionLevel = 'Warning' | 'Grace' | 'Expired';

/** LIS-CR-012: what drives the level — the trial end, a wallet covering less than a week, or a wallet at or below zero. */
export type SubscriptionReason = 'Trial' | 'Wallet' | 'WalletAtZero';

/**
 * LIS-CR-012 (C-16, D-3): `daysLeft` whole days to `endsAtUtc` (trial end, end of the wallet cover, start or end of the grace);
 * `amountToTopUp` in `currencyCode` brings the wallet back to one month of the plan fee; `canManage` = the user administers
 * the laboratory (sees the banner; once expired still signs in, to the wallet only — `SUB-021` elsewhere); `showModal` = fewer
 * than three days left, in grace or expired.
 */
export interface SubscriptionNotice {
  readonly level: SubscriptionLevel;
  readonly reason: SubscriptionReason;
  readonly daysLeft: number | null;
  readonly endsAtUtc: string | null;
  readonly amountToTopUp: number | null;
  readonly currencyCode: string | null;
  readonly trialEndsAtUtc: string | null;
  readonly canManage: boolean;
  readonly showModal: boolean;
}

/** CP-04: binds the current session to one of `AuthProfile.branchIds`. */
export interface SelectBranchRequest {
  readonly branchId: string;
}

/** CP-03 step 1: always answered 202, whether or not the account exists. */
export interface StartPasswordRecoveryRequest {
  readonly usernameOrMobile: string;
  readonly language: string | null;
}

/** CP-03 step 2: `newPassword` is sealed by the envelope interceptor (D-03). */
export interface CompletePasswordRecoveryRequest {
  readonly usernameOrMobile: string;
  readonly code: string;
  readonly newPassword: string;
}

/** LIS-CR-005: the tenant choice of a multi-laboratory user is gone; a laboratory address names one tenant. */
export type AuthStep = 'signed-in' | 'mfa';

export interface AuthResponse {
  readonly step: AuthStep;
  readonly accessToken: string | null;
  readonly expiresAtUtc: string | null;
  readonly refreshToken: string | null;
  readonly profile: AuthProfile | null;
  /** No longer sent (LIS-CR-005 removed the tenant choice); optional only so older spec fixtures still type-check. */
  readonly tenants?: null;
}

/**
 * LIS-CR-005: where the user signs in. A laboratory address (`/lab/<slug>`) posts the laboratory's slug, the Super Admin
 * address (`/admin/login`) posts `admin: true`; exactly one of the two (the server answers VALIDATION-400 otherwise). An
 * unknown laboratory, another laboratory's member or a non-staff user at the admin address is refused like a wrong password (AUTH-002).
 */
export type SignInTarget = { readonly laboratory: string } | { readonly admin: true };

export interface SignInRequest {
  readonly username: string;
  readonly password: string;
  readonly laboratory?: string;
  readonly admin?: true;
}

export interface RsaPublicJwk {
  readonly kty: string;
  readonly n: string;
  readonly e: string;
  readonly alg: string;
  readonly use: string;
  readonly kid: string;
}

export interface CredentialChallengeResponse {
  readonly kid: string;
  readonly publicKey: RsaPublicJwk;
  readonly publicKeyPem: string;
  readonly algorithm: string;
  readonly encryption: string;
  readonly nonce: string;
  readonly expiresAtUtc: string;
}

export interface SessionResponse {
  readonly sessionId: string;
  readonly userId: string;
  readonly displayName: string;
  readonly tenantId: string | null;
  readonly scope: string;
  readonly kind: string;
  readonly expiresAtUtc: string;
  readonly absoluteExpiresAtUtc: string;
  readonly reauthenticatedAtUtc: string | null;
  readonly permissions: readonly string[];
  readonly branchId?: string | null;
  readonly branchIds?: readonly string[];
}

export const AUTH_API = {
  base: '/api/v1/auth',
  challenge: '/api/v1/auth/credential-challenge',
  signIn: '/api/v1/auth/sign-in',
  refresh: '/api/v1/auth/refresh',
  signOut: '/api/v1/auth/sign-out',
  me: '/api/v1/auth/me',
  selectBranch: '/api/v1/auth/select-branch',
  recoveryStart: '/api/v1/auth/recovery/start',
  recoveryComplete: '/api/v1/auth/recovery/complete',
} as const;
