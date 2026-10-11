import { Injectable, inject, resource } from '@angular/core';

import type { SelectableBranch } from '../../../core/api/organization.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { mutation } from '../../../core/state/mutation';
import { AccountApi } from '../data/account.api';

/** CP-04 state: the branches the session may choose (organization module) and the selection (foundation, `AuthStore.selectBranch`). */
@Injectable()
export class BranchSelectionFacade {
  private readonly api = inject(AccountApi);
  private readonly auth = inject(AuthStore);

  /** Keyed by the signed-in user, so another sign-in never auto-selects from the previous user's list (UAT A-01/C-13). */
  readonly branches = resource({
    params: () => ({ userId: this.auth.currentUser()?.id ?? '' }),
    loader: (): Promise<readonly SelectableBranch[]> => this.api.selectableBranches(),
  });

  readonly currentBranch = this.auth.currentBranch;

  readonly select = mutation((branchId: string) => this.auth.selectBranch(branchId));

  /** Re-reads the branches when the page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14, A-01/C-13). A load in flight is left alone. */
  refresh(): void {
    this.branches.reload();
    this.select.reset();
  }
}
