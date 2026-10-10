import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { ACCOUNT_PERMISSIONS, type BranchDashboard, type DelayedTestItem } from '../../../core/api/account.contracts';
import type { AuditEntryItem } from '../../../core/api/audit.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { I18nService } from '../../../core/i18n/i18n.service';
import { type Release, inReleaseScope } from '../../../core/release';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { IconComponent, type IconName } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { StatCardComponent, type StatTone } from '../../../shared/ui/stat-card/stat-card.component';
import { formatDateTime, formatMoney, formatNumber, formatTime, pick, weekday } from '../data/account-format';
import { DashboardFacade } from '../state/dashboard.facade';
import { type BarDatum, BarChartComponent } from '../ui/bar-chart.component';

interface Kpi {
  readonly key: string;
  readonly value: string;
  readonly hint: string;
  readonly icon: IconName;
  readonly tone: StatTone;
  /** The release that shows the tile (the R1 prototype hides the sample figures). */
  readonly release?: Release;
}

interface QuickAction {
  readonly key: string;
  readonly path: string;
  readonly icon: IconName;
  readonly permission: string;
  readonly release: Release;
}

/** Every quick action of both releases; the R2 samples page stays hidden under RELEASE_SCOPE = R1. */
const QUICK_ACTIONS: readonly QuickAction[] = [
  { key: 'registration', path: '/registration', icon: 'clipboard', permission: 'registration.create', release: 'R1' },
  { key: 'searchRegistrations', path: '/registration/visits', icon: 'search', permission: 'registration.view', release: 'R1' },
  { key: 'resultsByAccession', path: '/results/by-accession', icon: 'flask', permission: 'results.view', release: 'R1' },
  { key: 'worklist', path: '/results/worklist', icon: 'layers', permission: 'results.view', release: 'R1' },
  { key: 'payments', path: '/billing/payments', icon: 'coins', permission: 'billing.view', release: 'R1' },
  { key: 'priceQuery', path: '/registration/price-query', icon: 'coins', permission: 'registration.view', release: 'R1' },
  { key: 'samples', path: '/samples', icon: 'barcode', permission: 'samples.view', release: 'R2' },
];

/**
 * The dashboard (SRS CP-05): the session branch's day at a glance — eight KPIs (a figure whose module is not live
 * yet reads "—"), the 7-day registrations chart, the delayed tests (BR-13, filled by Results), the recent activity
 * (audit log) and quick actions to the main pages. The date picker shows any past day of the branch.
 */
@Component({
  selector: 'app-dashboard-page',
  imports: [RouterLink, CanDirective, PageHeaderComponent, SectionCardComponent, StatCardComponent, DataTableComponent, CellTemplateDirective, IconComponent, BarChartComponent],
  templateUrl: './dashboard.page.html',
  styleUrl: './dashboard.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DashboardPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(DashboardFacade);
  private readonly auth = inject(AuthStore);

  /** The quick actions of the delivered release (the R1 prototype: registration, search, results, worklist, payments, price query). */
  protected readonly quickActions: readonly QuickAction[] = QUICK_ACTIONS.filter((action) => inReleaseScope(action.release));

  protected readonly delayedColumns: readonly TableColumn<DelayedTestItem>[] = [
    { key: 'accession', header: 'account.dashboard.accession', mono: true, width: '150px' },
    { key: 'patientName', header: 'account.dashboard.patient' },
    { key: 'testName', header: 'account.dashboard.test' },
    { key: 'delay', header: 'account.dashboard.delay', width: '110px', value: (row) => this.delay(row.delayMinutes) },
    { key: 'status', header: 'account.dashboard.status', width: '120px', secondary: true },
  ];

  protected readonly delayedRowId = (row: DelayedTestItem): string => `${row.visitId}:${row.testName}`;

  protected readonly data = computed<BranchDashboard | null>(() => (this.facade.dashboard.hasValue() ? this.facade.dashboard.value() : null));

  protected readonly branchName = computed(() => {
    const branch = this.facade.branch();
    return branch ? `${branch.code} · ${pick(branch.nameEn, branch.nameAr, this.i18n.lang())}` : '';
  });

  protected readonly subtitle = computed(() => {
    const date = this.facade.query().date;
    const branch = this.branchName();
    return branch ? `${branch} · ${date}` : date;
  });

  protected readonly kpis = computed<readonly Kpi[]>(() => {
    const data = this.data();
    const lang = this.i18n.lang();
    const kpis = data?.kpis;
    const notYet = this.i18n.t('account.dashboard.notAvailable');
    const today = this.i18n.t('account.dashboard.today');
    const count = (value: number | null | undefined, hint: string): { value: string; hint: string } =>
      value === null || value === undefined ? { value: '—', hint: notYet } : { value: formatNumber(value, lang), hint };

    const all: Kpi[] = [
      { key: 'registrations', icon: 'clipboard', tone: 'brand', ...count(kpis?.registrations, today) },
      {
        key: 'collectedToday',
        icon: 'coins',
        tone: 'ok',
        value: formatMoney(kpis?.collectedToday, data?.currencyCode, lang),
        hint: kpis?.collectedToday === null || kpis?.collectedToday === undefined ? notYet : today,
      },
      { key: 'samplesReceived', icon: 'barcode', tone: 'info', release: 'R2', ...count(kpis?.samplesReceived, today) },
      { key: 'pendingResults', icon: 'flask', tone: 'warn', ...count(kpis?.pendingResults, this.i18n.t('account.dashboard.awaitingEntry')) },
      { key: 'unverifiedBacklog', icon: 'check', tone: 'warn', ...count(kpis?.unverifiedBacklog, this.i18n.t('account.dashboard.awaitingSignOff')) },
      { key: 'delayedTests', icon: 'alert', tone: (kpis?.delayedTests ?? 0) > 0 ? 'bad' : 'ok', ...count(kpis?.delayedTests, this.i18n.t('account.dashboard.pastTurnaround')) },
      { key: 'rejectedSamples', icon: 'x', tone: (kpis?.rejectedSamples ?? 0) > 0 ? 'bad' : 'ok', release: 'R2', ...count(kpis?.rejectedSamples, today) },
    ];
    const list = all.filter((kpi) => inReleaseScope(kpi.release ?? 'R1'));

    if (this.auth.hasPermission(ACCOUNT_PERMISSIONS.walletView)) {
      list.push({
        key: 'walletBalance',
        icon: 'vault',
        tone: (kpis?.walletBalance ?? 1) <= 0 ? 'bad' : 'brand',
        value: formatMoney(kpis?.walletBalance, kpis?.walletCurrency, lang),
        hint: kpis?.walletBalance === null || kpis?.walletBalance === undefined ? notYet : this.i18n.t('account.dashboard.prepaid'),
      });
    }

    return list;
  });

  protected readonly chart = computed<readonly BarDatum[]>(() =>
    (this.data()?.registrationsByDay ?? []).map((bar) => ({
      label: weekday(bar.date, this.i18n.lang()),
      value: bar.count,
      description: this.i18n.tf('account.dashboard.chartBar', { date: bar.date, count: bar.count }),
    })),
  );

  protected readonly delayedRows = computed<readonly DelayedTestItem[]>(() => this.data()?.delayedTests ?? []);

  protected readonly resultsLive = computed(() => this.data()?.kpis.pendingResults !== null && this.data() !== null);

  protected readonly activity = computed<readonly AuditEntryItem[]>(() => (this.facade.activity.hasValue() ? (this.facade.activity.value()?.items ?? []) : []));

  constructor() {
    void this.i18n.load('account');
  }

  protected onDate(event: Event): void {
    this.facade.setDate((event.target as HTMLInputElement).value);
  }

  protected time(value: string): string {
    return formatTime(value, this.i18n.lang());
  }

  protected when(value: string): string {
    return formatDateTime(value, this.i18n.lang());
  }

  protected delay(minutes: number): string {
    const hours = Math.floor(minutes / 60);
    return hours > 0 ? this.i18n.tf('account.dashboard.delayHours', { hours, minutes: minutes % 60 }) : this.i18n.tf('account.dashboard.delayMinutes', { minutes });
  }
}
