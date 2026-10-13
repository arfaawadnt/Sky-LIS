import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { WorkAmountGrouping, WorkAmountQuery, WorkAmountReport } from '../../../core/api/reporting.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { mutation } from '../../../core/state/mutation';
import { ReportingApi } from '../data/reporting.api';

/** `2026-10-13` of a date in the browser's own calendar (the page's default "today"; the server echoes the laboratory's). */
export function isoDay(date: Date): string {
  const pad = (value: number): string => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** `isoDay` shifted by `days` (negative = back). */
export function shiftDay(day: string, days: number): string {
  const [year, month, date] = day.split('-').map(Number);
  return isoDay(new Date(year ?? 1970, (month ?? 1) - 1, (date ?? 1) + days));
}

/**
 * Work amount report (change request 2026-10-13 item 3; 01 §6.3): the query in a signal, the report through
 * `resource()` (re-read on every change of the query), the selectable branches and the member's privileges for the
 * days limit, the CSV export through `mutation()`. Provided at the feature route.
 */
@Injectable()
export class WorkAmountFacade {
  private readonly api = inject(ReportingApi);
  private readonly auth = inject(AuthStore);

  /** The query the report shows; the page runs it on open with today's day and the Day grouping. */
  readonly query = signal<WorkAmountQuery>({ from: isoDay(new Date()), to: isoDay(new Date()), groupBy: 'Day', branchId: null });

  readonly report = resource({
    params: () => this.query(),
    loader: ({ params, abortSignal }) => this.api.workAmount(params, abortSignal),
  });

  readonly branches = resource({
    loader: ({ abortSignal }) => this.api.selectableBranches(abortSignal),
  });

  readonly privileges = resource({
    loader: ({ abortSignal }) => this.api.myPrivileges(abortSignal),
  });

  /** The branch picker appears when the member may select more than one branch (CP-04). */
  readonly selectableBranches = computed(() => (this.branches.hasValue() ? this.branches.value() : []));

  readonly currentBranchId = computed(() => this.auth.currentBranch());

  /** The member's report days limit (CP-37 special right): 0 = today only, null = no limit, undefined until read. */
  readonly reportDaysLimit = computed<number | null | undefined>(() => (this.privileges.hasValue() ? this.privileges.value().privileges.specialRights.reportDaysLimit : undefined));

  /** The earliest day the member may report on, in the browser's calendar, or null without a limit. */
  readonly earliestDay = computed<string | null>(() => {
    const limit = this.reportDaysLimit();
    return limit === null || limit === undefined ? null : shiftDay(isoDay(new Date()), -limit);
  });

  readonly current = computed<WorkAmountReport | null>(() => (this.report.hasValue() ? this.report.value() : null));

  readonly exportCsv = mutation((query: WorkAmountQuery) => this.api.workAmountCsv(query));

  run(query: WorkAmountQuery): void {
    this.query.set(query);
  }

  setGrouping(groupBy: WorkAmountGrouping): void {
    this.query.update((current) => ({ ...current, groupBy }));
  }

  /** Re-reads the report and the pickers when the page opens again: the facade outlives the page (route provider; UAT D-14). */
  refresh(): void {
    this.report.reload();
    this.branches.reload();
    this.privileges.reload();
  }
}
