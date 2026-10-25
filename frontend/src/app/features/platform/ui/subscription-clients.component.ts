import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output } from '@angular/core';

import type { ClientStatus, SubscriptionClientItem, SubscriptionFilter } from '../../../core/api/platform.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import type { BadgeTone } from '../../../shared/ui/badge/status-tones';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { CsvExportService } from '../data/csv-export';
import { SubscriptionClientsFacade } from '../state/subscription-clients.facade';
import { fileStamp, formatDate, formatDateTime, formatMoney, pick } from './format';

/** The badge tone of a subscription level. */
export function subscriptionTone(level: string): BadgeTone {
  return level === 'Expired' ? 'bad' : level === 'Grace' ? 'warn' : level === 'Warning' ? 'info' : 'ok';
}

/**
 * LIS-CR-012 (C-16): the table of the SA-04 "Subscription" filter — the clients whose trial, wallet cover or grace ends within a
 * week, the ones in grace, the expired ones — soonest end first, with the amount that brings the wallet back to one month of the
 * fee, the manager to call and the last reminder sent. The page's search box and status select narrow the rows in the browser.
 */
@Component({
  selector: 'app-subscription-clients',
  imports: [DataTableComponent, CellTemplateDirective, RowActionsTemplateDirective, BadgeComponent, IconComponent],
  providers: [SubscriptionClientsFacade],
  template: `
    <p class="sub-summary small" role="status">
      <app-icon name="alert" [size]="15" />
      <span>{{ summary() }}</span>
      @if (facade.graceDays(); as grace) {
        <span class="muted">· {{ i18n.tf('platform.subscription.graceDays', { days: grace }) }}</span>
      }
    </p>
    @if (facade.list.error()) {
      <p class="problem list-problem" role="alert">{{ i18n.t('platform.subscription.loadError') }}</p>
    }
    <app-data-table [columns]="columns" [rows]="rows()" [loading]="facade.list.isLoading()" [emptyTitle]="i18n.t('platform.subscription.empty')"
                    [caption]="i18n.t('platform.subscription.title')" (rowActivate)="openClient.emit($event.id)">
      <ng-template appCell="name" [appCellOf]="rows()" let-row>
        <strong>{{ name(row) }}</strong>
        <div class="small muted mono">{{ row.portalUrl }}</div>
      </ng-template>
      <ng-template appCell="level" [appCellOf]="rows()" let-row>
        <app-badge [tone]="tone(row.level)">{{ i18n.t('platform.subscription.level.' + row.level) }}</app-badge>
        <div class="small muted">{{ i18n.t('platform.subscription.reason.' + row.reason) }}</div>
      </ng-template>
      <ng-template appCell="daysLeft" [appCellOf]="rows()" let-row>
        <strong class="days">{{ row.daysLeft ?? '—' }}</strong>
        <div class="small muted">{{ day(row.endsAtUtc) }}</div>
      </ng-template>
      <ng-template appCell="administrator" [appCellOf]="rows()" let-row>
        {{ row.administratorName }}
        <div class="small mono" dir="ltr"><a [href]="'tel:' + row.administratorMobile" [title]="i18n.t('platform.clients.silent.call')">{{ row.administratorMobile }}</a></div>
      </ng-template>
      <ng-template appRowActions [appRowActionsOf]="rows()" let-row>
        <button type="button" class="row-btn" (click)="openClient.emit(row.id)" [attr.aria-label]="i18n.t('platform.clients.edit')" [title]="i18n.t('platform.clients.edit')"><app-icon name="edit" [size]="15" /></button>
      </ng-template>
    </app-data-table>
  `,
  styles: `
    .sub-summary {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--sp-2);
      margin: 0;
      padding-block: var(--sp-2);
      padding-inline: var(--sp-4);
      color: var(--warn);
    }

    .days {
      font-variant-numeric: tabular-nums;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SubscriptionClientsComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(SubscriptionClientsFacade);
  private readonly csv = inject(CsvExportService);

  /** The filter: expiring (within a week), grace or expired. */
  readonly state = input.required<SubscriptionFilter>();
  /** The page's search text: name, portal address, manager or mobile. */
  readonly search = input<string | null>(null);
  /** The page's status filter; Inactive matches nothing (an inactive client has no subscription state). */
  readonly status = input<ClientStatus | null>(null);

  readonly openClient = output<string>();

  protected readonly columns: readonly TableColumn<SubscriptionClientItem>[] = [
    { key: 'name', header: 'platform.clients.column.client', value: (row) => this.name(row) },
    { key: 'level', header: 'platform.subscription.column.level', width: '130px', value: (row) => row.level },
    { key: 'daysLeft', header: 'platform.subscription.column.daysLeft', align: 'end', width: '120px', sortable: true, value: (row) => row.daysLeft ?? 0 },
    { key: 'amountToTopUp', header: 'platform.subscription.column.amount', align: 'end', value: (row) => formatMoney(row.amountToTopUp, row.currencyCode, this.i18n.lang()) },
    { key: 'walletBalance', header: 'platform.subscription.column.balance', align: 'end', secondary: true, value: (row) => formatMoney(row.walletBalance, row.currencyCode, this.i18n.lang()) },
    { key: 'administrator', header: 'platform.clients.silent.column.administrator', value: (row) => row.administratorName },
    { key: 'lastReminderAtUtc', header: 'platform.subscription.column.lastReminder', secondary: true, value: (row) => this.reminder(row) },
  ];

  protected readonly rows = computed<readonly SubscriptionClientItem[]>(() => {
    const search = (this.search() ?? '').trim().toLowerCase();
    const status = this.status();
    return this.facade.rows().filter(
      (row) =>
        (status === null || row.status === status) &&
        (search === '' || [row.nameEn, row.nameAr, row.portalUrl, row.administratorName, row.administratorMobile].some((text) => text.toLowerCase().includes(search))),
    );
  });

  protected readonly summary = computed(() => this.i18n.tc('platform.subscription.count.' + this.state(), this.rows().length));

  constructor() {
    void this.i18n.load('platform');
    effect(() => this.facade.setState(this.state()));
  }

  /** Re-reads the list (the page's Refresh). */
  refresh(): void {
    this.facade.refresh();
  }

  /** The rows shown, as a CSV in the reader's language (the page's Export). */
  exportCsv(): void {
    const lang = this.i18n.lang();
    this.csv.download(`clients-${this.state()}-${fileStamp()}`, [
      { header: this.i18n.t('platform.clients.column.nameEn'), value: (row: SubscriptionClientItem) => row.nameEn },
      { header: this.i18n.t('platform.clients.column.nameAr'), value: (row) => row.nameAr },
      { header: this.i18n.t('platform.clients.column.portalUrl'), value: (row) => row.portalUrl },
      { header: this.i18n.t('platform.subscription.column.level'), value: (row) => this.i18n.t('platform.subscription.level.' + row.level) },
      { header: this.i18n.t('platform.subscription.column.reason'), value: (row) => this.i18n.t('platform.subscription.reason.' + row.reason) },
      { header: this.i18n.t('platform.subscription.column.daysLeft'), value: (row) => row.daysLeft ?? '' },
      { header: this.i18n.t('platform.subscription.column.endsAt'), value: (row) => row.endsAtUtc?.slice(0, 10) ?? '' },
      { header: this.i18n.t('platform.subscription.column.amount'), value: (row) => row.amountToTopUp?.toFixed(2) ?? '' },
      { header: this.i18n.t('platform.subscription.column.balance'), value: (row) => row.walletBalance?.toFixed(2) ?? '' },
      { header: this.i18n.t('platform.clients.column.currency'), value: (row) => row.currencyCode ?? '' },
      { header: this.i18n.t('platform.clients.silent.column.administrator'), value: (row) => row.administratorName },
      { header: this.i18n.t('platform.clients.column.managerMobile'), value: (row) => row.administratorMobile },
      { header: this.i18n.t('platform.subscription.column.lastReminder'), value: (row) => (row.lastReminderAtUtc === null ? '' : formatDateTime(row.lastReminderAtUtc, lang)) },
    ], this.rows());
  }

  protected name(row: SubscriptionClientItem): string {
    return pick(row.nameEn, row.nameAr, this.i18n.lang());
  }

  protected tone(level: string): BadgeTone {
    return subscriptionTone(level);
  }

  protected day(iso: string | null): string {
    return formatDate(iso, this.i18n.lang());
  }

  private reminder(row: SubscriptionClientItem): string {
    return row.lastReminderAtUtc === null ? this.i18n.t('platform.clients.silent.never') : formatDateTime(row.lastReminderAtUtc, this.i18n.lang());
  }
}
