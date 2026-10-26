import { InjectionToken } from '@angular/core';

import type { Portal } from '../portal/portal';

/**
 * Runs `task` while holding the refresh lock of `portal` (the laboratory portal when omitted); resolves or rejects with the
 * task's own outcome.
 */
export type RefreshLock = <T>(task: () => Promise<T>, portal?: Portal) => Promise<T>;

/** The stem of the Web Locks names; one lock per portal of the origin (LIS-CR-014 G1). */
export const REFRESH_LOCK_NAME = 'platform-session-refresh';

/**
 * The lock of a portal (`platform-session-refresh:client`, `…:admin`): each portal has its own refresh cookie, so a Super Admin
 * refresh never waits for a laboratory one (LIS-CR-014 G1); the tabs of one portal still take turns (C-02).
 */
export function refreshLockName(portal: Portal): string {
  return `${REFRESH_LOCK_NAME}:${portal}`;
}

/** How long a tab waits for another tab's refresh before refreshing anyway (the server's grace covers that overlap). */
export const REFRESH_LOCK_WAIT_MS = 15_000;

/** No coordination: the task runs at once. Used where the Web Locks API is missing (older browsers, jsdom). */
export const directRefreshLock: RefreshLock = (task) => task();

/**
 * Validation 2026-10-11 C-02: the tabs of one browser share the httpOnly refresh cookie. Two tabs that refresh at the same
 * moment (both reloaded, a restored browser session) used to present the same token twice, which the server treats as
 * token theft and answers by revoking the session family: every tab was signed out. The refresh is therefore serialised
 * across the tabs of the origin with the Web Locks API (`navigator.locks`, exclusive mode): the second tab waits until the
 * first one's response has set the new cookie, then refreshes with it. No web storage is involved (01 §6.1); the lock holds
 * nothing but its name (one per portal). The server keeps a short grace for the immediate predecessor (`RefreshReuseGrace`),
 * so a browser without Web Locks, or a tab that gave up waiting, is still safe.
 *
 * A lock that is not granted within `waitMs` (a tab stuck mid-request) is abandoned and the task runs anyway; a task that
 * started under the lock is never run twice — its own error is the caller's.
 */
export function browserRefreshLock(locks: LockManager | undefined = globalThis.navigator?.locks, waitMs = REFRESH_LOCK_WAIT_MS): RefreshLock {
  if (locks === undefined || typeof locks.request !== 'function') {
    return directRefreshLock;
  }

  return async <T>(task: () => Promise<T>, portal: Portal = 'client'): Promise<T> => {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), waitMs);
    let started = false;
    try {
      return (await locks.request(refreshLockName(portal), { mode: 'exclusive', signal: controller.signal }, () => {
        clearTimeout(timer);
        started = true;
        return task();
      })) as T;
    } catch (error) {
      clearTimeout(timer);
      if (started) {
        throw error;
      }

      return task();
    }
  };
}

export const REFRESH_LOCK = new InjectionToken<RefreshLock>('REFRESH_LOCK', {
  providedIn: 'root',
  factory: () => browserRefreshLock(),
});
