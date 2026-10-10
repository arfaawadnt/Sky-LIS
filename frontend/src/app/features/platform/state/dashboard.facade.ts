import { Injectable, inject, resource, signal } from '@angular/core';

import type { DashboardPeriod } from '../../../core/api/platform.contracts';
import { PlatformApi } from '../data/platform.api';

/** SA-03: the platform KPIs for a period (today, week, month, year). */
@Injectable()
export class DashboardFacade {
  private readonly api = inject(PlatformApi);

  readonly period = signal<DashboardPeriod>('month');

  readonly dashboard = resource({
    params: () => this.period(),
    loader: ({ params, abortSignal }) => this.api.dashboard(params, abortSignal),
  });

  /** Re-reads the dashboard when a page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14). A load in flight is left alone. */
  refresh(): void {
    this.dashboard.reload();
  }

  setPeriod(period: DashboardPeriod): void {
    this.period.set(period);
  }
}
