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

  readonly branches = resource({ loader: (): Promise<readonly SelectableBranch[]> => this.api.selectableBranches() });

  readonly currentBranch = this.auth.currentBranch;

  readonly select = mutation((branchId: string) => this.auth.selectBranch(branchId));
}
