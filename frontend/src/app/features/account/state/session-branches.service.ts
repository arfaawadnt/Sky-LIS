import { Injectable, computed, inject, resource } from '@angular/core';

import type { SelectableBranch } from '../../../core/api/organization.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { PortalService } from '../../../core/portal/portal.service';
import { AccountApi } from '../data/account.api';

/**
 * The branches of the signed-in client session for the shell (validation F-05, CP-04 "change branch"): the selectable
 * branches (read once per user and tenant; an unrestricted member has every active branch, so the membership's
 * `branchIds` alone cannot say how many there are), the current one (`AuthStore.currentBranch`, which follows a change in
 * this tab or another) and whether a change is possible (more than one branch). Nothing is read in the Super Admin portal.
 */
@Injectable({ providedIn: 'root' })
export class SessionBranches {
  private readonly api = inject(AccountApi);
  private readonly auth = inject(AuthStore);
  private readonly portal = inject(PortalService);

  /**
   * The identity the list belongs to, as text: a computed of equal text does not notify, so a profile change of the same
   * identity (branch, language) never reloads the list.
   */
  private readonly identity = computed(() => {
    const user = this.auth.currentUser();
    return this.auth.isAuthenticated() && user !== null && this.portal.portal() === 'client' && !this.auth.hasEnded()
      ? `${user.id}|${this.auth.currentTenant() ?? ''}`
      : null;
  });

  readonly branches = resource({
    params: () => this.identity() ?? undefined,
    loader: (): Promise<readonly SelectableBranch[]> => this.api.selectableBranches().catch(() => []),
  });

  private readonly list = computed<readonly SelectableBranch[]>(() => (this.branches.hasValue() ? this.branches.value() : []));

  /** The session's branch, or null before one is selected (or while the list loads). */
  readonly current = computed<SelectableBranch | null>(() => {
    const id = this.auth.currentBranch();
    return id === null ? null : (this.list().find((branch) => branch.id === id) ?? null);
  });

  /** The user may switch: the list holds another branch than the current one. */
  readonly canChange = computed(() => this.list().length > 1);
}
