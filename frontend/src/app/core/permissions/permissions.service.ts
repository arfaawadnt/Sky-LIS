import { Injectable, inject } from '@angular/core';

import { AuthStore } from '../auth/auth.store';

/** `can(key)` for templates and guards (01 §6.2). Affordance only: hiding a button never replaces the server's decision (pitfall P-20). */
@Injectable({ providedIn: 'root' })
export class PermissionsService {
  private readonly store = inject(AuthStore);

  can(permissionKey: string): boolean {
    return this.store.hasPermission(permissionKey);
  }
}
