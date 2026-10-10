import { Injectable } from '@angular/core';

/**
 * The identity this browser presents when it asks for treasury access (intake LIS-INT-TRS-001 §3): a random id
 * minted once per browser profile and kept in local storage. It is a device label, not session state and not a
 * credential: the server binds the approval to it and still authenticates every request by bearer token.
 */
@Injectable({ providedIn: 'root' })
export class DeviceIdentityService {
  private static readonly KEY = 'lis.device-id';
  private cached: string | null = null;

  /** Stable per browser profile; falls back to a per-session value when storage is unavailable (private window). */
  deviceId(): string {
    if (this.cached !== null) {
      return this.cached;
    }

    try {
      // eslint-disable-next-line no-restricted-globals -- a device label is not session state (see class comment)
      const storage = localStorage;
      const existing = storage.getItem(DeviceIdentityService.KEY);
      if (existing !== null && existing.length > 0) {
        this.cached = existing;
        return existing;
      }

      const created = crypto.randomUUID();
      storage.setItem(DeviceIdentityService.KEY, created);
      this.cached = created;
      return created;
    } catch {
      this.cached ??= crypto.randomUUID();
      return this.cached;
    }
  }
}
