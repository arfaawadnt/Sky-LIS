import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { DASHBOARD_PERIODS, type DashboardPeriod, type RecentClient } from '../../../core/api/platform.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CellTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { StatCardComponent } from '../../../shared/ui/stat-card/stat-card.component';
import { DashboardFacade } from '../state/dashboard.facade';
import { type BarItem, BarListComponent } from '../ui/bar-list.component';
import { formatDate, formatMoney, formatNumber, pick } from '../ui/format';
import { statusTone } from '../ui/status';

/** SA-03 Dashboard: client counts, activity of the period, wallets at zero, sources, plan distribution and recent clients. */
@Component({
  selector: 'app-platform-dashboard-page',
  imports: [RouterLink, PageHeaderComponent, SectionCardComponent, StatCardComponent, DataTableComponent, CellTemplateDirective, BadgeComponent, IconComponent, BarListComponent],
  templateUrl: './dashboard.page.html',
  styleUrl: './platform-pages.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DashboardPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(DashboardFacade);
  protected readonly periods = DASHBOARD_PERIODS;

  protected readonly data = computed(() => (this.facade.dashboard.hasValue() ? this.facade.dashboard.value() : null));

  protected readonly planBars = computed<readonly BarItem[]>(() =>
    (this.data()?.planDistribution ?? []).map((plan) => ({ label: `${plan.code} · ${pick(plan.nameEn, plan.nameAr, this.i18n.lang())}`, value: plan.clients })),
  );

  protected readonly clientBars = computed<readonly BarItem[]>(() =>
    (this.data()?.registrationsByClient ?? []).map((client) => ({ label: pick(client.nameEn, client.nameAr, this.i18n.lang()), value: client.registrations })),
  );

  protected readonly recentColumns: readonly TableColumn<RecentClient>[] = [
    { key: 'name', header: 'platform.clients.column.client' },
    { key: 'country', header: 'platform.clients.column.country', value: (row) => pick(row.countryNameEn, row.countryNameAr, this.i18n.lang()), secondary: true },
    { key: 'planCode', header: 'platform.clients.column.plan', mono: true },
    { key: 'walletBalance', header: 'platform.clients.column.wallet', align: 'end', value: (row) => formatMoney(row.walletBalance, row.currencyCode, this.i18n.lang()) },
    { key: 'createdAtUtc', header: 'platform.clients.column.created', value: (row) => formatDate(row.createdAtUtc, this.i18n.lang()), secondary: true },
    { key: 'source', header: 'platform.clients.column.source' },
    { key: 'status', header: 'platform.clients.column.status' },
  ];

  protected readonly recentRows = computed<readonly RecentClient[]>(() => this.data()?.recentClients ?? []);

  constructor() {
    void this.i18n.load('platform');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14)
  }

  protected onPeriod(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setPeriod(this.periods.find((period) => period === value) ?? 'month');
  }

  protected periodLabel(period: DashboardPeriod): string {
    return this.i18n.t(`platform.dashboard.period.${period}`);
  }

  protected number(value: number | null | undefined): string {
    return formatNumber(value, this.i18n.lang());
  }

  protected clientName(row: RecentClient): string {
    return pick(row.nameEn, row.nameAr, this.i18n.lang());
  }

  protected tone(status: string): ReturnType<typeof statusTone> {
    return statusTone(status);
  }
}
