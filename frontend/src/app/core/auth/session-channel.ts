import { DOCUMENT } from '@angular/common';
import { InjectionToken, type Provider, inject } from '@angular/core';

/**
 * What one tab tells the others of the same browser (C-01). The refresh cookie is shared by every tab, so a sign-in
 * or a sign-out in one tab changes who the others would act as on their next refresh: they reload instead. A
 * `profile-changed` (R2-01: password changed, language saved) leaves the session as it is; the tabs of the same user
 * re-read their profile (a forced password change, for one, is over).
 *
 * Validation F-01 (idle timeout): `activity` says the user is active in another tab, so this tab's idle watcher counts
 * from now; `renewed` says another tab refreshed the session, so the server counts from now. A `signed-out` with
 * `reason: 'idle'` ends the other tabs on the sign-in page with the inactivity notice.
 *
 * Validation F-05 (change of branch, CP-04): the branch belongs to the server session, which every tab shares; `branch-changed`
 * tells the other tabs of the same identity to show the new branch, and the pages keyed by it re-read.
 */
export type SessionMessage =
  | { readonly kind: 'signed-out'; readonly reason?: 'idle' }
  | { readonly kind: 'signed-in'; readonly userId: string; readonly tenantId: string | null }
  | { readonly kind: 'profile-changed'; readonly userId: string; readonly tenantId: string | null }
  | { readonly kind: 'activity'; readonly userId: string; readonly tenantId: string | null }
  | { readonly kind: 'renewed'; readonly userId: string; readonly tenantId: string | null }
  /** Validation F-05: the session (shared by every tab through the refresh cookie) now works in another branch. */
  | { readonly kind: 'branch-changed'; readonly userId: string; readonly tenantId: string | null; readonly branchId: string };

/** The messages that concern one identity (everything but `signed-out`). */
export type IdentityMessage = Exclude<SessionMessage, { readonly kind: 'signed-out' }>;

export interface SessionChannel {
  post(message: SessionMessage): void;
  /** Registers the handler; returns the function that removes it. */
  listen(handler: (message: SessionMessage) => void): () => void;
}

/** The channel's name; one per origin. */
export const SESSION_CHANNEL_NAME = 'platform-session';

const NO_CHANNEL: SessionChannel = { post: () => undefined, listen: () => () => undefined };

/** The default is silent (unit specs, server rendering); `provideSessionSync()` in `app.config.ts` wires the browser's channel. */
export const SESSION_CHANNEL = new InjectionToken<SessionChannel>('SESSION_CHANNEL', { providedIn: 'root', factory: () => NO_CHANNEL });

/** The identity messages that carry nothing but the identity. */
type PlainIdentityKind = Exclude<IdentityMessage, { readonly kind: 'branch-changed' }>['kind'];

const IDENTITY_KINDS: readonly PlainIdentityKind[] = ['signed-in', 'profile-changed', 'activity', 'renewed'];

function isIdentityKind(kind: unknown): kind is PlainIdentityKind {
  return IDENTITY_KINDS.some((known) => known === kind);
}

/** A message from another tab is data: only the known shapes are accepted. */
export function readSessionMessage(data: unknown): SessionMessage | null {
  if (typeof data !== 'object' || data === null) {
    return null;
  }

  const record = data as Record<string, unknown>;
  if (record['kind'] === 'signed-out') {
    return record['reason'] === 'idle' ? { kind: 'signed-out', reason: 'idle' } : { kind: 'signed-out' };
  }

  const kind = record['kind'];
  const tenantId = record['tenantId'];
  if (kind === 'branch-changed' && typeof record['userId'] === 'string' && (typeof tenantId === 'string' || tenantId === null) && typeof record['branchId'] === 'string') {
    return { kind, userId: record['userId'], tenantId, branchId: record['branchId'] };
  }

  if (isIdentityKind(kind) && typeof record['userId'] === 'string' && (typeof tenantId === 'string' || tenantId === null)) {
    return { kind, userId: record['userId'], tenantId };
  }

  return null;
}

/** `BroadcastChannel` between the tabs of this origin (no web storage involved); silent where the browser has none. */
export function broadcastSessionChannel(view: (Window & typeof globalThis) | null): SessionChannel {
  const channel = view && typeof view.BroadcastChannel === 'function' ? new view.BroadcastChannel(SESSION_CHANNEL_NAME) : null;
  if (channel === null) {
    return NO_CHANNEL;
  }

  return {
    post: (message) => channel.postMessage(message),
    listen: (handler) => {
      const listener = (event: MessageEvent<unknown>): void => {
        const message = readSessionMessage(event.data);
        if (message !== null) {
          handler(message);
        }
      };
      channel.addEventListener('message', listener);
      return () => channel.removeEventListener('message', listener);
    },
  };
}

/** Wires the tabs of the browser together (C-01); `app.config.ts` only, so specs keep the silent default. */
export function provideSessionSync(): Provider {
  return { provide: SESSION_CHANNEL, useFactory: () => broadcastSessionChannel(inject(DOCUMENT).defaultView) };
}
