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
import { formatDateTime, formatDay, formatMoney, formatNumber, formatTime, humanize, localDay, pick, weekday } from '../data/account-format';
import { DashboardFacade } from '../state/dashboard.facade';
import { type BarDatum, BarChartComponent } from '../ui/bar-chart.component';

interface Kpi {
  readonly key: string;
  /** The tile's caption key (`collectedToday` reads "Collected" on a past day). */
  readonly label: string;
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
 * (audit log) and quick actions to the main pages. The date picker shows today or any past day of the branch.
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

  /** The client audit log page (CP-68) is Release 2: the "Open audit" link shows only when it is in scope. */
  protected readonly auditPageOffered = inReleaseScope('R2');

  protected readonly delayedColumns: readonly TableColumn<DelayedTestItem>[] = [
    { key: 'accession', header: 'account.dashboard.accession', mono: true, width: '150px' },
    { key: 'patientName', header: 'account.dashboard.patient' },
    { key: 'testName', header: 'account.dashboard.test', value: (row) => pick(row.testName, row.testNameAr, this.i18n.lang()) },
    // UAT R3-03: when the result was due (expected time, local), beside how late it is.
    { key: 'expectedAtUtc', header: 'account.dashboard.due', width: '160px', value: (row) => this.when(row.expectedAtUtc) },
    { key: 'delay', header: 'account.dashboard.delay', width: '110px', value: (row) => this.delay(row.delayMinutes) },
    { key: 'status', header: 'account.dashboard.status', width: '120px', secondary: true, value: (row) => this.resultStatus(row.status) },
  ];

  /** The chart's caption with the day count in the interface's digits (UAT R1-06: "آخر 7 أيام" kept a Latin 7). */
  protected readonly chartTitle = computed(() => this.i18n.tf('account.dashboard.last7', { days: formatNumber(7, this.i18n.lang()) }));

  protected readonly delayedRowId = (row: DelayedTestItem): string => `${row.visitId}:${row.testName}`;

  protected readonly data = computed<BranchDashboard | null>(() => (this.facade.dashboard.hasValue() ? this.facade.dashboard.value() : null));

  protected readonly branchName = computed(() => {
    const branch = this.facade.branch();
    return branch ? `${branch.code} · ${pick(branch.nameEn, branch.nameAr, this.i18n.lang())}` : '';
  });

  /** The day shown is today (the captions say "today"), or a past day picked in the date box (UAT A-12). */
  protected readonly isToday = computed(() => this.facade.query().date === localDay());

  /** The day in the user's language and digits, as the tiles print their figures (UAT A-12). */
  protected readonly day = computed(() => formatDay(this.facade.query().date, this.i18n.lang()));

  protected readonly subtitle = computed(() => {
    const branch = this.branchName();
    return branch ? `${branch} · ${this.day()}` : this.day();
  });

  protected readonly kpis = computed<readonly Kpi[]>(() => {
    const data = this.data();
    const lang = this.i18n.lang();
    const kpis = data?.kpis;
    const notYet = this.i18n.t('account.dashboard.notAvailable');
    const today = this.isToday() ? this.i18n.t('account.dashboard.today') : this.i18n.tf('account.dashboard.onDay', { date: this.day() });
    const label = (key: string): string => `account.dashboard.kpi.${key}`;
    const count = (value: number | null | undefined, hint: string): { value: string; hint: string } =>
      value === null || value === undefined ? { value: '—', hint: notYet } : { value: formatNumber(value, lang), hint };

    const all: Kpi[] = [
      { key: 'registrations', label: label('registrations'), icon: 'clipboard', tone: 'brand', ...count(kpis?.registrations, today) },
      {
        key: 'collectedToday',
        label: label(this.isToday() ? 'collectedToday' : 'collected'),
        icon: 'coins',
        tone: 'ok',
        value: formatMoney(kpis?.collectedToday, data?.currencyCode, lang),
        hint: kpis?.collectedToday === null || kpis?.collectedToday === undefined ? notYet : today,
      },
      { key: 'samplesReceived', label: label('samplesReceived'), icon: 'barcode', tone: 'info', release: 'R2', ...count(kpis?.samplesReceived, today) },
      { key: 'pendingResults', label: label('pendingResults'), icon: 'flask', tone: 'warn', ...count(kpis?.pendingResults, this.i18n.t('account.dashboard.awaitingEntry')) },
      { key: 'unverifiedBacklog', label: label('unverifiedBacklog'), icon: 'check', tone: 'warn', ...count(kpis?.unverifiedBacklog, this.i18n.t('account.dashboard.awaitingSignOff')) },
      { key: 'delayedTests', label: label('delayedTests'), icon: 'alert', tone: (kpis?.delayedTests ?? 0) > 0 ? 'bad' : 'ok', ...count(kpis?.delayedTests, this.i18n.t('account.dashboard.pastTurnaround')) },
      { key: 'rejectedSamples', label: label('rejectedSamples'), icon: 'x', tone: (kpis?.rejectedSamples ?? 0) > 0 ? 'bad' : 'ok', release: 'R2', ...count(kpis?.rejectedSamples, today) },
    ];
    const seesMoney = this.auth.hasPermission(ACCOUNT_PERMISSIONS.billingView);   // validation observation O-9
    const list = all.filter((kpi) => inReleaseScope(kpi.release ?? 'R1') && (kpi.key !== 'collectedToday' || seesMoney));

    if (this.auth.hasPermission(ACCOUNT_PERMISSIONS.walletView)) {
      list.push({
        key: 'walletBalance',
        label: label('walletBalance'),
        icon: 'vault',
        tone: (kpis?.walletBalance ?? 1) <= 0 ? 'bad' : 'brand',
        value: formatMoney(kpis?.walletBalance, kpis?.walletCurrency, lang),
        hint: kpis?.walletBalance === null || kpis?.walletBalance === undefined ? notYet : this.i18n.t('account.dashboard.prepaid'),
      });
    }

    return list;
  });

  protected readonly chart = computed<readonly BarDatum[]>(() =>
    (this.data()?.registrationsByDay ?? []).map((bar) => {
      const lang = this.i18n.lang();
      const count = formatNumber(bar.count, lang);
      // R-10: one registration reads in the singular ('1 registration'), in both languages
      const key = bar.count === 1 ? 'account.dashboard.chartBarOne' : 'account.dashboard.chartBar';
      return { label: weekday(bar.date, lang), value: bar.count, display: count, description: this.i18n.tf(key, { date: formatDay(bar.date, lang), count }) };
    }),
  );

  protected readonly delayedRows = computed<readonly DelayedTestItem[]>(() => this.data()?.delayedTests ?? []);

  protected readonly resultsLive = computed(() => this.data()?.kpis.pendingResults !== null && this.data() !== null);

  protected readonly activity = computed<readonly AuditEntryItem[]>(() => (this.facade.activity.hasValue() ? (this.facade.activity.value()?.items ?? []) : []));

  constructor() {
    void this.i18n.load('account');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14, A-01/C-13)
  }

  /** A cleared or future day returns to today and the box shows it (validation A-10). */
  protected onDate(event: Event): void {
    const input = event.target as HTMLInputElement;
    input.value = this.facade.setDate(input.value);
  }

  protected time(value: string): string {
    return formatTime(value, this.i18n.lang());
  }

  protected when(value: string): string {
    return formatDateTime(value, this.i18n.lang());
  }

  /** A result status in words (Ordered, Complete, Reviewed), else the status split into words. */
  protected resultStatus(status: string): string {
    const key = `account.dashboard.resultStatus.${status}`;
    return this.i18n.has(key) ? this.i18n.t(key) : humanize(status);
  }

  protected delay(minutes: number): string {
    const lang = this.i18n.lang();
    const hours = Math.floor(minutes / 60);
    return hours > 0
      ? this.i18n.tf('account.dashboard.delayHours', { hours: formatNumber(hours, lang), minutes: formatNumber(minutes % 60, lang) })
      : this.i18n.tf('account.dashboard.delayMinutes', { minutes: formatNumber(minutes, lang) });
  }

  /** Recent activity in words, not type names (UAT A-12): the dictionary's label, else the name split into words. */
  protected actionName(entry: AuditEntryItem): string {
    const key = `account.audit.actionName.${entry.action}`;
    return this.i18n.has(key) ? this.i18n.t(key) : humanize(entry.action);
  }

  protected entityName(entry: AuditEntryItem): string {
    const key = `account.audit.entityName.${entry.entityType}`;
    return this.i18n.has(key) ? this.i18n.t(key) : humanize(entry.entityType);
  }
}
