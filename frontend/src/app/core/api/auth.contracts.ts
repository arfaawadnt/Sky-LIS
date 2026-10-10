/**
 * Auth contracts mirrored from `Platform.Contracts.Identity` (camelCase as the API serialises them).
 * Bootstrap only: 01 §6.2 generates `core/api` from the OpenAPI document once the API publishes one, and CI
 * then fails on any drift (pitfall P-62). Nothing here is decoded from a token (D-02).
 */

export interface AuthProfile {
  readonly userId: string;
  readonly displayName: string;
  readonly tenantId: string | null;
  readonly roleName: string | null;
  readonly permissions: readonly string[];
  readonly language: string;
  readonly scope: string;
  /** The branch the session is bound to (CP-04); `null` until selected. The API always sends it; optional here only until the generator lands. */
  readonly branchId?: string | null;
  /** The branches the membership may choose from; empty = unrestricted (the organization module lists the tenant's branches). */
  readonly branchIds?: readonly string[];
  /** An administrator reset the password: the SPA sends the user to the change-password page first. */
  readonly mustChangePassword?: boolean;
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

export interface TenantChoice {
  readonly tenantId: string;
  readonly displayName: string;
}

export type AuthStep = 'signed-in' | 'select-tenant' | 'mfa';

export interface AuthResponse {
  readonly step: AuthStep;
  readonly accessToken: string | null;
  readonly expiresAtUtc: string | null;
  readonly refreshToken: string | null;
  readonly profile: AuthProfile | null;
  readonly tenants: readonly TenantChoice[] | null;
}

export interface SignInRequest {
  readonly tenant: string | null;
  readonly username: string;
  readonly password: string;
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
