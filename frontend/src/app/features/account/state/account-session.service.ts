import { Injectable, computed, inject, signal } from '@angular/core';

import type { MyAccountProfile } from '../../../core/api/account.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { AccountApi } from '../data/account.api';

/**
 * The signed-in user's own profile, loaded once per session for the guards and the pages that need it
 * (`mustChangePassword`, the language, the laboratory record). Kept in memory only and bound to the session's user
 * and tenant, so a sign-out or another sign-in never sees the previous profile; `invalidate()` after a change
 * (language, password) makes the next read ask the server again.
 */
@Injectable({ providedIn: 'root' })
export class AccountSession {
  private readonly api = inject(AccountApi);
  private readonly auth = inject(AuthStore);
  private inFlight: Promise<MyAccountProfile | null> | null = null;
  private readonly cached = signal<MyAccountProfile | null>(null);

  /** The cached profile while it belongs to the current session's user and tenant. */
  readonly profile = computed(() => {
    const profile = this.cached();
    return profile !== null && profile.userId === this.auth.currentUser()?.id && profile.tenantId === this.auth.currentTenant() ? profile : null;
  });

  /** The cached profile, or one call to the server; `null` when it cannot be read (the caller decides what that means). */
  ensure(): Promise<MyAccountProfile | null> {
    const current = this.profile();
    if (current !== null) {
      return Promise.resolve(current);
    }

    this.inFlight ??= this.api
      .profile()
      .then((profile) => {
        this.cached.set(profile);
        return profile;
      })
      .catch(() => null)
      .finally(() => {
        this.inFlight = null;
      });
    return this.inFlight;
  }

  set(profile: MyAccountProfile): void {
    this.cached.set(profile);
  }

  invalidate(): void {
    this.cached.set(null);
  }
}
