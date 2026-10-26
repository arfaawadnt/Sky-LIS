import { DOCUMENT } from '@angular/common';
import { InjectionToken, type Provider, inject } from '@angular/core';

import type { Portal } from '../portal/portal';
import { PORTALS, SESSION_PORTAL } from './session-portal';

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
 *
 * LIS-CR-014 G1 (one browser, two portals): each portal has its own refresh cookie and so its own session, and every message
 * travels on the channel **of the tab's portal** (`platform-session:client`, `platform-session:admin`). Tabs of one portal
 * follow each other (sign-in, sign-out, branch, profile, idle); a tab of the other portal never hears them.
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

/** The stem of the channels' names; one channel per portal of the origin (LIS-CR-014 G1). */
export const SESSION_CHANNEL_NAME = 'platform-session';

/** The channel of a portal: `platform-session:client`, `platform-session:admin`. */
export function sessionChannelName(portal: Portal): string {
  return `${SESSION_CHANNEL_NAME}:${portal}`;
}

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

/**
 * `BroadcastChannel` between the tabs of this origin (no web storage involved); silent where the browser has none. One channel
 * per portal (LIS-CR-014 G1): a message goes out on the channel of the portal the tab is in when it posts, and a handler hears
 * a message only from the channel of the portal the tab is in when it arrives (`portal` is read each time, so a tab that moves
 * between portals follows).
 */
export function broadcastSessionChannel(view: (Window & typeof globalThis) | null, portal: () => Portal = () => 'client'): SessionChannel {
  if (!view || typeof view.BroadcastChannel !== 'function') {
    return NO_CHANNEL;
  }

  const channels = new Map<Portal, BroadcastChannel>();
  const channelOf = (of: Portal): BroadcastChannel => {
    let channel = channels.get(of);
    if (channel === undefined) {
      channel = new view.BroadcastChannel(sessionChannelName(of));
      channels.set(of, channel);
    }

    return channel;
  };

  return {
    post: (message) => channelOf(portal()).postMessage(message),
    listen: (handler) => {
      const stops = PORTALS.map((of) => {
        const channel = channelOf(of);
        const listener = (event: MessageEvent<unknown>): void => {
          if (portal() !== of) {
            return;   // the other portal's session: never this tab's business
          }

          const message = readSessionMessage(event.data);
          if (message !== null) {
            handler(message);
          }
        };
        channel.addEventListener('message', listener);
        return () => channel.removeEventListener('message', listener);
      });
      return () => stops.forEach((stop) => stop());
    },
  };
}

/** Wires the tabs of the browser together (C-01), per portal (LIS-CR-014 G1); `app.config.ts` only, so specs keep the silent default. */
export function provideSessionSync(): Provider {
  return { provide: SESSION_CHANNEL, useFactory: () => broadcastSessionChannel(inject(DOCUMENT).defaultView, inject(SESSION_PORTAL)) };
}
