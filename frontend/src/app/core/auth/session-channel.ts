import { DOCUMENT } from '@angular/common';
import { InjectionToken, type Provider, inject } from '@angular/core';

/**
 * What one tab tells the others of the same browser (C-01). The refresh cookie is shared by every tab, so a sign-in
 * or a sign-out in one tab changes who the others would act as on their next refresh: they reload instead. A
 * `profile-changed` (R2-01: password changed, language saved) leaves the session as it is; the tabs of the same user
 * re-read their profile (a forced password change, for one, is over).
 */
export type SessionMessage =
  | { readonly kind: 'signed-out' }
  | { readonly kind: 'signed-in'; readonly userId: string; readonly tenantId: string | null }
  | { readonly kind: 'profile-changed'; readonly userId: string; readonly tenantId: string | null };

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

/** A message from another tab is data: only the known shapes are accepted. */
export function readSessionMessage(data: unknown): SessionMessage | null {
  if (typeof data !== 'object' || data === null) {
    return null;
  }

  const record = data as Record<string, unknown>;
  if (record['kind'] === 'signed-out') {
    return { kind: 'signed-out' };
  }

  const kind = record['kind'];
  const tenantId = record['tenantId'];
  if ((kind === 'signed-in' || kind === 'profile-changed') && typeof record['userId'] === 'string' && (typeof tenantId === 'string' || tenantId === null)) {
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
