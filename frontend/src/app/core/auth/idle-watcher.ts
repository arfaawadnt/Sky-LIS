import { DOCUMENT } from '@angular/common';
import { DestroyRef, type EnvironmentProviders, Injectable, computed, inject, provideAppInitializer, signal } from '@angular/core';

import { AuthStore } from './auth.store';
import { SESSION_CHANNEL, type SessionMessage } from './session-channel';

/** The code default of the tenant setting `sessionTimeoutMinutes` (`SettingKeys.SessionTimeoutMinutes`). */
export const DEFAULT_SESSION_TIMEOUT_MINUTES = 30;

/** Local activity is registered (and told to the other tabs) at most this often. */
export const ACTIVITY_THROTTLE_MS = 5_000;

const TICK_MS = 1_000;
const MINUTE_MS = 60_000;

/** The user events that count as activity; a tab becoming visible counts too. */
const ACTIVITY_EVENTS = ['mousemove', 'mousedown', 'keydown', 'touchstart', 'wheel', 'scroll'] as const;

/** The warning shows a minute before the end; with a timeout under two minutes, at half of it. */
export function warningLeadMs(timeoutMs: number): number {
  return Math.min(MINUTE_MS, timeoutMs / 2);
}

/**
 * How stale the server's last renewal may get while the user is active before the watcher renews it with one refresh:
 * a quarter of the timeout, at least 15 s and at most a minute. The server counts idleness from the last refresh, so an
 * active user's session is refreshed at most once a minute (once per 15 s at the one-minute minimum).
 */
export function renewalIntervalMs(timeoutMs: number): number {
  return Math.min(MINUTE_MS, Math.max(15_000, timeoutMs / 4));
}

/**
 * The client half of the tenant's idle timeout (validation F-01, setting `sessionTimeoutMinutes`, `AuthProfile`). The server
 * is the authority: it refuses a refresh after the session went unrenewed longer than the timeout (`AUTH-017`, see
 * `RefreshSessionCommand`). The watcher keeps the two counts together:
 *
 * - **Activity** is a mouse, keyboard, touch, wheel or scroll event, or the tab becoming visible, in any tab of the browser:
 *   each tab tells the others through the session channel (`activity`), so working in one tab keeps the others alive.
 * - **Renewal**: while the user is active the watcher refreshes the session once the last renewal (`AuthStore.renewedAt`,
 *   any tab, told through `renewed`) is older than `renewalIntervalMs`, so the server never sees an active user as idle.
 * - **The end** is counted from the earlier of the last activity and the last renewal, so the client never outlives the
 *   server. `warningLeadMs` before it the warning dialog shows (`IdleWarningComponent`) with a countdown: *Stay signed in*
 *   refreshes, *Sign out* signs out; while it shows, local movement no longer counts — the user must answer it (activity
 *   in another tab still closes it). At the end the session is signed out with a full page load of `/login?reason=idle`,
 *   which says "Your session ended after inactivity", in every tab.
 *
 * Mounted by `provideIdleWatch()` in `app.config.ts`; the dialog is placed in the application root. Timers read
 * `Date.now()` on every tick, so a sleeping laptop or a throttled background tab ends the session on its next tick.
 */
@Injectable({ providedIn: 'root' })
export class IdleWatcher {
  private readonly auth = inject(AuthStore);
  private readonly channel = inject(SESSION_CHANNEL);
  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);

  private readonly warningState = signal(false);
  private readonly secondsLeftState = signal(0);

  private started = false;
  private watching = false;
  private expired = false;
  private renewing = false;
  private lastActivity = 0;

  /** True while the "your session will end" dialog should show. */
  readonly warning = this.warningState.asReadonly();

  /** Whole seconds left before the session ends, while the warning shows. */
  readonly secondsLeft = this.secondsLeftState.asReadonly();

  /** The tenant's timeout in milliseconds, from the session profile (30 minutes when the server sent none). */
  readonly timeoutMs = computed(() => {
    const minutes = this.auth.profile()?.sessionTimeoutMinutes;
    return (typeof minutes === 'number' && minutes > 0 ? minutes : DEFAULT_SESSION_TIMEOUT_MINUTES) * MINUTE_MS;
  });

  /** Starts listening (idempotent); stops with the application. */
  start(): void {
    if (this.started) {
      return;
    }

    this.started = true;
    this.lastActivity = Date.now();
    const onActivity = (): void => this.onLocalActivity();
    const onVisibility = (): void => {
      if (this.document.visibilityState === 'visible') {
        this.check();
        this.onLocalActivity();
      }
    };

    for (const name of ACTIVITY_EVENTS) {
      this.document.addEventListener(name, onActivity, { capture: true, passive: true });
    }

    this.document.addEventListener('visibilitychange', onVisibility);
    const stopListening = this.channel.listen((message) => this.onOtherTab(message));
    const timer = setInterval(() => this.check(), TICK_MS);

    this.destroyRef.onDestroy(() => {
      for (const name of ACTIVITY_EVENTS) {
        this.document.removeEventListener(name, onActivity, { capture: true });
      }

      this.document.removeEventListener('visibilitychange', onVisibility);
      stopListening();
      clearInterval(timer);
    });
  }

  /** *Stay signed in*: the user is here; the session is renewed on the server at once. */
  stay(): void {
    if (!this.watching || this.expired) {
      return;
    }

    this.lastActivity = Date.now();
    this.warningState.set(false);
    this.announce();
    void this.renew();
  }

  /** *Sign out* from the warning: an ordinary sign-out. */
  signOut(): void {
    this.expired = true;
    this.warningState.set(false);
    void this.auth.signOut();
  }

  /** One tick: starts the count with a new session, shows or hides the warning, ends the session at the deadline. */
  check(): void {
    const now = Date.now();
    if (!this.auth.isAuthenticated() || this.auth.hasEnded()) {
      this.watching = false;
      this.warningState.set(false);
      return;
    }

    if (!this.watching) {
      // a session began in this tab (sign-in, start-up refresh): the count starts now
      this.watching = true;
      this.expired = false;
      this.lastActivity = now;
    }

    if (this.expired || this.renewing) {
      return;   // a renewal in flight decides by itself (its end runs the next check)
    }

    const timeout = this.timeoutMs();
    const anchor = Math.min(this.lastActivity, this.auth.renewedAt() ?? now);
    const left = anchor + timeout - now;
    if (left <= 0) {
      this.expire();
      return;
    }

    const warn = left <= warningLeadMs(timeout);
    this.warningState.set(warn);
    if (warn) {
      this.secondsLeftState.set(Math.ceil(left / 1000));
    }
  }

  private onLocalActivity(): void {
    const now = Date.now();
    if (now - this.lastActivity < ACTIVITY_THROTTLE_MS) {
      return;
    }

    if (!this.watching) {
      this.lastActivity = now;   // the sign-in page: typing the password is activity too
      return;
    }

    if (this.warningState() || this.expired) {
      return;   // the warning asks for an answer
    }

    this.lastActivity = now;
    this.announce();
    if (now - (this.auth.renewedAt() ?? 0) >= renewalIntervalMs(this.timeoutMs())) {
      void this.renew();
    }
  }

  private onOtherTab(message: SessionMessage): void {
    const profile = this.auth.profile();
    if ((message.kind !== 'activity' && message.kind !== 'renewed') || profile === null || message.userId !== profile.userId || message.tenantId !== profile.tenantId) {
      return;
    }

    if (message.kind === 'activity') {
      this.lastActivity = Date.now();
    }

    this.check();   // `renewed` already moved AuthStore.renewedAt (the store listens first)
  }

  private announce(): void {
    const profile = this.auth.profile();
    if (profile !== null) {
      this.channel.post({ kind: 'activity', userId: profile.userId, tenantId: profile.tenantId });
    }
  }

  private async renew(): Promise<void> {
    if (this.renewing) {
      return;
    }

    this.renewing = true;
    try {
      await this.auth.refresh();   // null: the store is already loading the sign-in page
    } finally {
      this.renewing = false;
      this.check();
    }
  }

  private expire(): void {
    this.expired = true;
    this.warningState.set(false);
    void this.auth.signOut('idle');
  }
}

/** Starts the idle watcher with the application (`app.config.ts`); the dialog is `IdleWarningComponent` in the root. */
export function provideIdleWatch(): EnvironmentProviders {
  return provideAppInitializer(() => inject(IdleWatcher).start());
}
