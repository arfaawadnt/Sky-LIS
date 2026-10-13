import { Injectable, inject } from '@angular/core';

import { AuthStore } from '../auth/auth.store';

/** The device ids of this browser profile, one per user id (validation v0.5.0 O-10). */
type DeviceIdMap = Record<string, string>;

/**
 * The identity this browser presents for a user when they ask for treasury access or take money (intake
 * LIS-INT-TRS-001 §3, Q1): a random id minted once per **user and browser profile**. The server binds an approval to
 * (user, treasury, device), so two people who share a browser each need their own approval, and the id the second person
 * presents is not the first person's (validation v0.5.0 O-10: one id per profile made approvals follow the browser).
 *
 * Storage: this is the only allowed use of `localStorage` in the SPA (MODULE-PATTERN; ESLint `no-restricted-globals`
 * is disabled on that one line). The single entry `lis.device-id` holds a JSON map `{ "<user id>": "<device id>" }`.
 * A device id is a label, not session state and not a credential: no token, no name, nothing the server trusts on
 * its own; every request is still authenticated by its bearer token. An entry from before O-10 (a bare id) is handed
 * to the first user who asks on this browser and then kept in the map. When storage is unavailable (private window)
 * the id lives for the page only.
 */
@Injectable({ providedIn: 'root' })
export class DeviceIdentityService {
  static readonly KEY = 'lis.device-id';
  /** More users than this on one browser profile is not a real desk; the oldest entries make room. */
  static readonly MAX_USERS = 50;

  private readonly auth = inject(AuthStore);
  private readonly cached = new Map<string, string>();
  private anonymous: string | null = null;

  /**
   * The id of the signed-in user on this browser (reactive: a computed that calls it follows a user change). Without a
   * user (never the case on the pages that call it) a per-page id that is not stored.
   */
  deviceId(): string {
    const userId = this.auth.currentUser()?.id ?? null;
    if (userId === null) {
      this.anonymous ??= crypto.randomUUID();
      return this.anonymous;
    }

    const known = this.cached.get(userId);
    if (known !== undefined) {
      return known;
    }

    const id = this.storedOrMinted(userId);
    this.cached.set(userId, id);
    return id;
  }

  private storedOrMinted(userId: string): string {
    try {
      // eslint-disable-next-line no-restricted-globals -- the one allowed web-storage use: device labels per user (see class comment)
      const storage = localStorage;
      const { map, legacy } = DeviceIdentityService.read(storage.getItem(DeviceIdentityService.KEY));
      const existing = map[userId];
      if (existing !== undefined) {
        return existing;
      }

      const id = legacy ?? crypto.randomUUID();
      const entries = Object.entries(map).filter(([key]) => key !== userId);
      const kept = entries.slice(Math.max(0, entries.length - (DeviceIdentityService.MAX_USERS - 1)));
      storage.setItem(DeviceIdentityService.KEY, JSON.stringify(Object.fromEntries([...kept, [userId, id]])));
      return id;
    } catch {
      return crypto.randomUUID();
    }
  }

  /** The stored map, or the bare id of the pre-O-10 format (adopted once), or nothing. */
  private static read(raw: string | null): { readonly map: DeviceIdMap; readonly legacy: string | null } {
    if (raw === null || raw.trim() === '') {
      return { map: {}, legacy: null };
    }

    try {
      const parsed: unknown = JSON.parse(raw);
      if (parsed !== null && typeof parsed === 'object' && !Array.isArray(parsed)) {
        const map: DeviceIdMap = {};
        for (const [key, value] of Object.entries(parsed)) {
          if (typeof value === 'string' && value.length > 0 && value.length <= 128) {
            map[key] = value;
          }
        }

        return { map, legacy: null };
      }
    } catch {
      // not JSON: the bare id written before O-10
    }

    return { map: {}, legacy: raw.length <= 128 ? raw : null };
  }
}
