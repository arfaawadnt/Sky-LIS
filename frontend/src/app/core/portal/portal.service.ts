import { Injectable, computed, inject } from '@angular/core';

import { AuthStore } from '../auth/auth.store';
import { PLATFORM_TENANT_ID } from '../tenancy/tenant-ids';
import type { Portal } from './portal';

/**
 * Which portal the signed-in session belongs to (MODULES.md §1 "Two portals, one SPA"): `admin` when the current
 * tenant is the platform tenant, `client` otherwise. Derived from the session, so it follows sign-in and sign-out.
 */
@Injectable({ providedIn: 'root' })
export class PortalService {
  private readonly auth = inject(AuthStore);

  readonly portal = computed<Portal>(() => (this.auth.currentTenant() === PLATFORM_TENANT_ID ? 'admin' : 'client'));

  readonly isPlatform = computed(() => this.portal() === 'admin');

  /** The landing route of the current portal. */
  readonly homePath = computed(() => (this.portal() === 'admin' ? '/admin' : '/'));
}
