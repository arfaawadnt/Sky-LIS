import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
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
import { ACCOUNT_API, type ResultsDensity } from '../api/account.contracts';
import { ProblemCodes, ProblemError, toProblemDetails } from '../api/problem-details';
import { I18nService } from '../i18n/i18n.service';
import { REFRESH_LOCK } from './refresh-lock';
import { SESSION_CHANNEL, type SessionMessage } from './session-channel';
import { BROWSER_NAVIGATION, SIGN_IN_PATH, type SessionEndReason, idleSignInUrl, signInUrl } from './session-navigation';

export interface CurrentUser {
  readonly id: string;
  readonly displayName: string;
  readonly language: string;
  readonly scope: string;
  /** The primary administrator of the laboratory (CP-38): the SOD-TRS-001 exception on the treasury access page. */
  readonly isPrimaryAdministrator?: boolean;
}

/**
 * The session as the SPA knows it (01 §6.1, §6.4; D-01, D-02, pitfall P-61). The access token lives in a signal
 * and nowhere else: never web storage, never a cookie the script can read, never decoded. Identity, tenant and
 * permissions come from the sign-in and refresh responses (`profile`), so the token stays opaque to the client.
 * Refresh is single-flight: concurrent 401s share one call to `/api/v1/auth/refresh`, which the browser sends
 * with the httpOnly `__Secure-rt` cookie. Across the tabs of the browser it is serialised by `REFRESH_LOCK` (Web Locks,
 * C-02), so two tabs never present the same cookie at the same moment.
 *
 * That cookie is shared by every tab of the browser, so a refresh may come back as **another user** (C-01: tab B signed
 * in as Y after tab A's session of X ended). A refresh is therefore accepted only for the user and tenant the tab was
 * built for; on a change the state is cleared and the tab reloads the sign-in page. Every end of a session (sign-out,
 * lost session, identity change, a sign-in or sign-out in another tab) is a **full page load** (A-01): it discards the
 * in-memory state of every module, which a router navigation would keep. Tabs tell each other through `SESSION_CHANNEL`.
 */
@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly http = inject(HttpClient);
  private readonly i18n = inject(I18nService);
  private readonly navigation = inject(BROWSER_NAVIGATION);
  private readonly channel = inject(SESSION_CHANNEL);
  private readonly refreshLock = inject(REFRESH_LOCK);

  private readonly accessToken = signal<string | null>(null);
  private readonly expiresAtUtc = signal<Date | null>(null);
  private readonly profileState = signal<AuthProfile | null>(null);
  private readonly hydrated = signal(false);
  private readonly profileRevisionState = signal(0);
  private readonly endedState = signal(false);
  private readonly renewedAtState = signal<number | null>(null);
  private refreshInFlight: Promise<string | null> | null = null;

  /**
   * Validation F-01: when the session was last renewed on the server (`Date.now()` of the last sign-in or refresh, in this
   * tab or — told through the channel — in another tab of the same identity). The server counts the idle timeout from that
   * moment; the idle watcher keeps its own count no later than it.
   */
  readonly renewedAt = this.renewedAtState.asReadonly();

  /** The bearer token for the auth interceptor; `null` when signed out. */
  readonly token = this.accessToken.asReadonly();

  readonly expiresAt = this.expiresAtUtc.asReadonly();

  readonly profile = this.profileState.asReadonly();

  /**
   * R2-01: bumped when another tab of the same user and tenant changed the user's own profile (password, language).
   * Whoever caches profile data (the account session, the password-change guard) re-reads it when this moves.
   */
  readonly profileRevision = this.profileRevisionState.asReadonly();

  /**
   * Round 2 R2-05: true once this tab's session ended by a sign-out (here or in another tab) and the sign-in page is
   * loading. The profile stays until the page unloads, so no page's resource re-runs for a changed user or branch; the
   * auth interceptor sends no API request any more and `refresh` answers `null` without asking the server.
   */
  readonly hasEnded = this.endedState.asReadonly();

  /** True once the start-up refresh has settled, either way. */
  readonly isHydrated = this.hydrated.asReadonly();

  readonly isAuthenticated = computed(() => this.accessToken() !== null && this.profileState() !== null);

  readonly currentUser = computed<CurrentUser | null>(() => {
    const profile = this.profileState();
    return profile
      ? { id: profile.userId, displayName: profile.displayName, language: profile.language, scope: profile.scope, isPrimaryAdministrator: profile.isPrimaryAdministrator === true }
      : null;
  });

  readonly currentTenant = computed(() => this.profileState()?.tenantId ?? null);

  /** The branch the session is bound to (CP-04), or `null` until the user selects one. */
  readonly currentBranch = computed(() => this.profileState()?.branchId ?? null);

  /** The branches the membership may choose from; empty when unrestricted. */
  readonly branchIds = computed<readonly string[]>(() => this.profileState()?.branchIds ?? []);

  readonly permissions = computed(() => new Set(this.profileState()?.permissions ?? []));

  constructor() {
    const stop = this.channel.listen((message) => this.onOtherTab(message));
    inject(DestroyRef).onDestroy(stop);
  }

  /** Affordance only (pitfall P-20): the server decides on every request. */
  hasPermission(permission: string): boolean {
    return this.permissions().has(permission);
  }

  /** Start-up: one silent refresh restores the session from the cookie. Always resolves; failure means signed out. */
  async hydrate(): Promise<void> {
    try {
      await this.refresh();
      // CP-65: the interface opens in the language saved on the membership (the sign-in page applies it the same way).
      const language = this.profileState()?.language;
      if (language) {
        this.i18n.setLanguage(language);
      }
    } finally {
      this.hydrated.set(true);
    }
  }

  /**
   * CP-65: switches the interface and saves the language on the membership of the session, so the next sign-in and every
   * other device open in it. The switch itself never waits for the server; a refused save leaves the profile as it was.
   */
  async updateLanguage(language: string): Promise<void> {
    this.i18n.setLanguage(language);
    const profile = this.profileState();
    if (!profile) {
      return;
    }

    await firstValueFrom(this.http.put<void>(ACCOUNT_API.preferences, { language }), { defaultValue: undefined });
    const current = this.profileState();
    if (current) {
      this.profileState.set({ ...current, language });
    }
  }

  /**
   * CP-19 (results page, 2026-10-14): saves the row density of the results grid on the membership of the session, so
   * every device of the user opens the grid the same way; the profile follows a successful save.
   */
  async updateResultsDensity(resultsDensity: ResultsDensity): Promise<void> {
    await firstValueFrom(this.http.put<void>(ACCOUNT_API.preferences, { resultsDensity }), { defaultValue: undefined });
    const current = this.profileState();
    if (current) {
      this.profileState.set({ ...current, resultsDensity });
    }
  }

  /**
   * R2-01: tells the other tabs that this user's own profile changed (`profileChangeInterceptor` calls it after a
   * successful password change or language save). This tab's own caches are refreshed by the caller.
   */
  announceProfileChange(): void {
    const profile = this.profileState();
    if (profile !== null) {
      this.channel.post({ kind: 'profile-changed', userId: profile.userId, tenantId: profile.tenantId });
    }
  }

  /** The password is sealed by the envelope interceptor before it leaves the browser (D-03). */
  async signIn(username: string, password: string, tenant: string | null = null): Promise<AuthResponse> {
    const body: SignInRequest = { tenant, username, password };
    const response = await firstValueFrom(this.http.post<AuthResponse>(AUTH_API.signIn, body));
    this.apply(response);
    const profile = this.profileState();
    if (response.step === 'signed-in' && profile !== null) {
      this.channel.post({ kind: 'signed-in', userId: profile.userId, tenantId: profile.tenantId });
    }

    return response;
  }

  /**
   * Single-flight refresh; resolves to the new token, or `null` when the session is gone or now belongs to another user
   * or tenant (state cleared). When the tab had a session, `null` also means the tab is reloading the sign-in page: the
   * caller must not replay anything.
   */
  refresh(): Promise<string | null> {
    if (this.endedState()) {
      return Promise.resolve(null);
    }

    this.refreshInFlight ??= this.refreshOnce().finally(() => {
      this.refreshInFlight = null;
    });

    return this.refreshInFlight;
  }

  /**
   * CP-04: binds the session to a branch; the server answers with the refreshed profile, which replaces the current one.
   * The session (and so the branch) is shared by every tab: the others are told (`branch-changed`, validation F-05).
   */
  async selectBranch(branchId: string): Promise<AuthProfile> {
    const body: SelectBranchRequest = { branchId };
    const profile = await firstValueFrom(this.http.post<AuthProfile>(AUTH_API.selectBranch, body));
    this.profileState.set(profile);
    if (profile.branchId) {
      this.channel.post({ kind: 'branch-changed', userId: profile.userId, tenantId: profile.tenantId, branchId: profile.branchId });
    }

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

  /**
   * Ends the session on the server, tells the other tabs and reloads the sign-in page without a return URL (A-01, B-11).
   * The token goes at once; the profile stays until the page unloads (round 2 R2-05: clearing it re-ran the open page's
   * resources, keyed by the user or the branch, and each reload was refused with a 401 before `/login` arrived).
   * `reason: 'idle'` (the idle watcher, F-01) lands every tab on `/login?reason=idle`, which says why.
   */
  async signOut(reason: SessionEndReason | null = null): Promise<void> {
    try {
      await firstValueFrom(this.http.post<void>(AUTH_API.signOut, null));
    } catch {
      // the server clears the cookie either way; the token is dropped below
    } finally {
      this.channel.post(reason === 'idle' ? { kind: 'signed-out', reason } : { kind: 'signed-out' });
      this.leave(reason === 'idle' ? idleSignInUrl() : SIGN_IN_PATH);
    }
  }

  clear(): void {
    this.accessToken.set(null);
    this.expiresAtUtc.set(null);
    this.profileState.set(null);
  }

  private async refreshOnce(): Promise<string | null> {
    const before = this.profileState();
    let response: AuthResponse;
    try {
      // C-02: one refresh at a time across the tabs; a waiting tab sends the cookie the previous refresh has just set
      response = await this.refreshLock(() => firstValueFrom(this.http.post<AuthResponse>(AUTH_API.refresh, null)));
    } catch (error: unknown) {
      if (before !== null && problemCodeOf(error) === ProblemCodes.sessionIdleTimeout) {
        // F-01: the server ended the session after inactivity; every tab lands on the sign-in page that says so
        this.channel.post({ kind: 'signed-out', reason: 'idle' });
        this.leave(idleSignInUrl());
        return null;
      }

      this.endSession(before, true);
      return null;
    }

    if (response.step !== 'signed-in' || response.accessToken === null || response.profile === null) {
      this.endSession(before, true);
      return null;
    }

    if (before !== null && (response.profile.userId !== before.userId || response.profile.tenantId !== before.tenantId)) {
      // C-01: the shared cookie now belongs to someone else; a tab built for another identity never adopts it
      this.endSession(before, false);
      return null;
    }

    this.apply(response);
    // F-01: the server counts the idle timeout from this refresh; the other tabs' watchers count from it too
    this.channel.post({ kind: 'renewed', userId: response.profile.userId, tenantId: response.profile.tenantId });
    return response.accessToken;
  }

  /**
   * Clears the state; a tab that had a session reloads the sign-in page (full page load, A-01). A lost session keeps
   * where the user was as the return URL; an identity change does not (the page belonged to the previous user).
   */
  private endSession(before: AuthProfile | null, keepReturnUrl: boolean): void {
    this.clear();
    if (before !== null) {
      this.navigation.load(signInUrl(keepReturnUrl ? this.navigation.currentUrl() : null));
    }
  }

  /**
   * C-01: another tab signed out, or signed in as someone else; this tab's in-memory session is stale. R2-01: another tab
   * of the same identity changed the profile; the session stands, the profile readers re-read.
   */
  private onOtherTab(message: SessionMessage): void {
    const profile = this.profileState();
    if (profile === null || this.endedState()) {
      return;
    }

    const sameIdentity = message.kind !== 'signed-out' && message.userId === profile.userId && message.tenantId === profile.tenantId;
    if (message.kind === 'activity') {
      return;   // the idle watcher's business (F-01)
    }

    if (message.kind === 'renewed') {
      if (sameIdentity) {
        this.renewedAtState.set(Date.now());
      }

      return;
    }

    if (message.kind === 'signed-out' && message.reason === 'idle') {
      this.leave(idleSignInUrl());
      return;
    }

    if (message.kind === 'branch-changed') {
      // F-05: another tab changed the branch of the shared session; the pages keyed by `currentBranch` re-read
      if (sameIdentity && profile.branchId !== message.branchId) {
        this.profileState.set({ ...profile, branchId: message.branchId });
      }

      return;
    }

    if (message.kind === 'profile-changed') {
      if (sameIdentity) {
        this.profileRevisionState.update((revision) => revision + 1);
      }

      return;
    }

    if (sameIdentity) {
      return;
    }

    this.leave(SIGN_IN_PATH);
  }

  /** The end of a session that this tab did not lose by itself: no token, nothing requested any more, then the full page load. */
  private leave(url: string): void {
    this.endedState.set(true);
    this.accessToken.set(null);
    this.expiresAtUtc.set(null);
    this.navigation.load(url);
  }

  private apply(response: AuthResponse): void {
    if (response.step !== 'signed-in' || response.accessToken === null || response.profile === null) {
      return;
    }

    this.endedState.set(false);
    this.accessToken.set(response.accessToken);
    this.expiresAtUtc.set(response.expiresAtUtc ? new Date(response.expiresAtUtc) : null);
    this.profileState.set(response.profile);
    this.renewedAtState.set(Date.now());
  }
}

/** The problem code of a failed refresh, whether the problem interceptor already typed it or not. */
function problemCodeOf(error: unknown): string | null {
  if (error instanceof ProblemError) {
    return error.code;
  }

  return error instanceof HttpErrorResponse ? toProblemDetails(error).code : null;
}
