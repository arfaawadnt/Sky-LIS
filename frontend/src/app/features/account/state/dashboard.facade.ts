import { Injectable, computed, inject, resource, signal } from '@angular/core';

import { ACCOUNT_PERMISSIONS, type BranchDashboardQuery } from '../../../core/api/account.contracts';
import type { AuditEntriesPage } from '../../../core/api/audit.contracts';
import type { SelectableBranch } from '../../../core/api/organization.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { AccountApi } from '../data/account.api';
import { localDay, utcOffsetMinutes } from '../data/account-format';

const RECENT_ACTIVITY = 8;

/**
 * CP-05 state: the dashboard of the session branch for one local day (default today), the branch's name (from the
 * selectable branches), and the recent activity (audit log, only with `audit.view`). Reloads on date change.
 */
@Injectable()
export class DashboardFacade {
  private readonly api = inject(AccountApi);
  private readonly auth = inject(AuthStore);

  readonly query = signal<BranchDashboardQuery>({ date: localDay(), utcOffsetMinutes: utcOffsetMinutes() });

  readonly dashboard = resource({
    params: () => ({ ...this.query(), branch: this.auth.currentBranch() }),
    loader: ({ params }) => this.api.dashboard({ date: params.date, utcOffsetMinutes: params.utcOffsetMinutes }),
  });

  readonly branches = resource({ loader: (): Promise<readonly SelectableBranch[]> => this.api.selectableBranches().catch(() => []) });

  readonly canSeeActivity = computed(() => this.auth.hasPermission(ACCOUNT_PERMISSIONS.auditView));

  readonly activity = resource({
    params: () => (this.canSeeActivity() ? { branch: this.auth.currentBranch(), date: this.query().date } : undefined),
    loader: (): Promise<AuditEntriesPage> =>
      this.api.auditEntries({ from: null, to: null, userId: null, entityType: null, entityId: null, page: 1, pageSize: RECENT_ACTIVITY }),
  });

  readonly branch = computed<SelectableBranch | null>(() => {
    const id = this.auth.currentBranch();
    return (this.branches.hasValue() ? this.branches.value() : []).find((branch) => branch.id === id) ?? null;
  });

  /**
   * Shows a day and returns the day actually shown: a past day or today as picked; an empty, malformed or future day
   * returns to today (validation A-10: the server refuses a future day with ACC-030).
   */
  setDate(date: string): string {
    const today = localDay();
    const day = /^\d{4}-\d{2}-\d{2}$/.test(date) && date <= today ? date : today;
    const [year, month, dayOfMonth] = day.split('-').map(Number);
    const offset = utcOffsetMinutes(new Date(year ?? 1970, (month ?? 1) - 1, dayOfMonth ?? 1, 12));
    this.query.set({ date: day, utcOffsetMinutes: offset });
    return day;
  }

  /** The latest day the date box offers: the user's today. */
  maxDate(): string {
    return localDay();
  }

  /** Re-reads the day, the branch names and the activity when the page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14, A-01/C-13). A load in flight is left alone. */
  refresh(): void {
    this.dashboard.reload();
    this.branches.reload();
    if (this.canSeeActivity()) {
      this.activity.reload();
    }
  }
}
