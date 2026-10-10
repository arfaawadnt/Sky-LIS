import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import {
  AUTH_API,
  type AuthProfile,
  type AuthResponse,
  type CompletePasswordRecoveryRequest,
  type SelectBranchRequest,
  type SignInRequest,
  type StartPasswordRecoveryRequest,
} from '../api/auth.contracts';

export interface CurrentUser {
  readonly id: string;
  readonly displayName: string;
  readonly language: string;
  readonly scope: string;
}

/**
 * The session as the SPA knows it (01 §6.1, §6.4; D-01, D-02, pitfall P-61). The access token lives in a signal
 * and nowhere else: never web storage, never a cookie the script can read, never decoded. Identity, tenant and
 * permissions come from the sign-in and refresh responses (`profile`), so the token stays opaque to the client.
 * Refresh is single-flight: concurrent 401s share one call to `/api/v1/auth/refresh`, which the browser sends
 * with the httpOnly `__Secure-rt` cookie.
 */
@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly http = inject(HttpClient);

  private readonly accessToken = signal<string | null>(null);
  private readonly expiresAtUtc = signal<Date | null>(null);
  private readonly profileState = signal<AuthProfile | null>(null);
  private readonly hydrated = signal(false);
  private refreshInFlight: Promise<string | null> | null = null;

  /** The bearer token for the auth interceptor; `null` when signed out. */
  readonly token = this.accessToken.asReadonly();

  readonly expiresAt = this.expiresAtUtc.asReadonly();

  readonly profile = this.profileState.asReadonly();

  /** True once the start-up refresh has settled, either way. */
  readonly isHydrated = this.hydrated.asReadonly();

  readonly isAuthenticated = computed(() => this.accessToken() !== null && this.profileState() !== null);

  readonly currentUser = computed<CurrentUser | null>(() => {
    const profile = this.profileState();
    return profile ? { id: profile.userId, displayName: profile.displayName, language: profile.language, scope: profile.scope } : null;
  });

  readonly currentTenant = computed(() => this.profileState()?.tenantId ?? null);

  /** The branch the session is bound to (CP-04), or `null` until the user selects one. */
  readonly currentBranch = computed(() => this.profileState()?.branchId ?? null);

  /** The branches the membership may choose from; empty when unrestricted. */
  readonly branchIds = computed<readonly string[]>(() => this.profileState()?.branchIds ?? []);

  readonly permissions = computed(() => new Set(this.profileState()?.permissions ?? []));

  /** Affordance only (pitfall P-20): the server decides on every request. */
  hasPermission(permission: string): boolean {
    return this.permissions().has(permission);
  }

  /** Start-up: one silent refresh restores the session from the cookie. Always resolves; failure means signed out. */
  async hydrate(): Promise<void> {
    try {
      await this.refresh();
    } finally {
      this.hydrated.set(true);
    }
  }

  /** The password is sealed by the envelope interceptor before it leaves the browser (D-03). */
  async signIn(username: string, password: string, tenant: string | null = null): Promise<AuthResponse> {
    const body: SignInRequest = { tenant, username, password };
    const response = await firstValueFrom(this.http.post<AuthResponse>(AUTH_API.signIn, body));
    this.apply(response);
    return response;
  }

  /** Single-flight refresh; resolves to the new token, or `null` when the session is gone (state cleared). */
  refresh(): Promise<string | null> {
    this.refreshInFlight ??= this.refreshOnce().finally(() => {
      this.refreshInFlight = null;
    });

    return this.refreshInFlight;
  }

  /** CP-04: binds the session to a branch; the server answers with the refreshed profile, which replaces the current one. */
  async selectBranch(branchId: string): Promise<AuthProfile> {
    const body: SelectBranchRequest = { branchId };
    const profile = await firstValueFrom(this.http.post<AuthProfile>(AUTH_API.selectBranch, body));
    this.profileState.set(profile);
    return profile;
  }

  /** CP-03 step 1: asks for a one-time code; resolves either way (the server never says whether the account exists). */
  async startPasswordRecovery(usernameOrMobile: string, language: string | null = null): Promise<void> {
    const body: StartPasswordRecoveryRequest = { usernameOrMobile, language };
    await firstValueFrom(this.http.post<void>(AUTH_API.recoveryStart, body));
  }

  /** CP-03 step 2: the code and the new password (sealed in transit); a refusal arrives as `AUTH-013`. */
  async completePasswordRecovery(usernameOrMobile: string, code: string, newPassword: string): Promise<void> {
    const body: CompletePasswordRecoveryRequest = { usernameOrMobile, code, newPassword };
    await firstValueFrom(this.http.post<void>(AUTH_API.recoveryComplete, body));
  }

  async signOut(): Promise<void> {
    try {
      await firstValueFrom(this.http.post<void>(AUTH_API.signOut, null));
    } catch {
      // the server clears the cookie either way; local state is cleared below
    } finally {
      this.clear();
    }
  }

  clear(): void {
    this.accessToken.set(null);
    this.expiresAtUtc.set(null);
    this.profileState.set(null);
  }

  private async refreshOnce(): Promise<string | null> {
    try {
      const response = await firstValueFrom(this.http.post<AuthResponse>(AUTH_API.refresh, null));
      this.apply(response);
      return response.accessToken;
    } catch {
      this.clear();
      return null;
    }
  }

  private apply(response: AuthResponse): void {
    if (response.step !== 'signed-in' || response.accessToken === null || response.profile === null) {
      return;
    }

    this.accessToken.set(response.accessToken);
    this.expiresAtUtc.set(response.expiresAtUtc ? new Date(response.expiresAtUtc) : null);
    this.profileState.set(response.profile);
  }
}
