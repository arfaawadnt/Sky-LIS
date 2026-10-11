import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';

import type { SelectableBranch } from '../../../core/api/organization.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PortalService } from '../../../core/portal/portal.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { EmptyStateComponent } from '../../../shared/ui/empty-state/empty-state.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ACCOUNT_PATHS } from '../account.guards';
import { pick } from '../data/account-format';
import { BranchSelectionFacade } from '../state/branch-selection.facade';

/**
 * Branch selection (SRS CP-04): the session works in one of the user's branches and every transaction is stamped
 * with it. A single selectable branch is selected without asking; several are offered as cards (code and names);
 * none means the administrator has not assigned one. `?change=1` (the shell's "change branch") keeps the page open
 * when a branch is already selected; `?returnUrl=` is where the user goes next.
 */
@Component({
  selector: 'app-select-branch-page',
  imports: [PageHeaderComponent, EmptyStateComponent, IconComponent],
  templateUrl: './select-branch.page.html',
  styleUrl: './select-branch.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SelectBranchPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(BranchSelectionFacade);
  private readonly auth = inject(AuthStore);
  private readonly portal = inject(PortalService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  readonly returnUrl = input<string | undefined>();
  readonly change = input<string | undefined>();

  private readonly autoSelected = signal(false);

  protected readonly branches = computed<readonly SelectableBranch[]>(() => (this.facade.branches.hasValue() ? this.facade.branches.value() : []));

  protected readonly current = this.facade.currentBranch;

  constructor() {
    void this.i18n.load('account');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14, A-01/C-13)

    // Nothing to choose: a session that already has its branch continues, unless the user asked to change it;
    // a single selectable branch is taken at once.
    effect(() => {
      if (this.change() === undefined && this.current() !== null && !this.autoSelected()) {
        this.autoSelected.set(true);
        void this.continue();
        return;
      }

      const branches = this.branches();
      if (this.change() === undefined && this.current() === null && branches.length === 1 && !this.autoSelected()) {
        const only = branches[0];
        if (only) {
          this.autoSelected.set(true);
          void this.choose(only);
        }
      }
    });
  }

  protected name(branch: SelectableBranch): string {
    return pick(branch.nameEn, branch.nameAr, this.i18n.lang());
  }

  protected secondName(branch: SelectableBranch): string {
    return this.i18n.lang() === 'ar' ? branch.nameEn : branch.nameAr;
  }

  protected async choose(branch: SelectableBranch): Promise<void> {
    if (this.facade.select.pending()) {
      return;
    }

    if ((await this.facade.select.execute(branch.id)) !== null) {
      this.toast.success(this.i18n.tf('account.branch.selected', { branch: this.name(branch) }));
      await this.continue();
    }
  }

  protected async signOut(): Promise<void> {
    await this.auth.signOut();
    // AuthStore.signOut() ends in a full page load of /login itself: no navigation afterwards (UAT A-01/C-13)
  }

  protected problemText(): string {
    return problemMessage(this.i18n, 'account', this.facade.select.problem());
  }

  private async continue(): Promise<void> {
    const requested = this.returnUrl();
    const safe = requested && requested.startsWith('/') && !requested.startsWith('//') && !requested.startsWith(ACCOUNT_PATHS.selectBranch) ? requested : null;
    await this.router.navigateByUrl(safe ?? (this.portal.portal() === 'admin' ? '/admin' : ACCOUNT_PATHS.dashboard));
  }
}
