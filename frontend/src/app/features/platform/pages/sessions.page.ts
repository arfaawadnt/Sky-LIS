import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { type AdminSessionItem, PLATFORM_PERMISSIONS } from '../../../core/api/platform.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { CsvExportService } from '../data/csv-export';
import { SessionsFacade } from '../state/sessions.facade';
import { fileStamp, formatDateTime } from '../ui/format';
import { statusTone } from '../ui/status';

/** SA-08 Sessions: live sessions of the National Technology staff; terminate any but your own (sign out instead). */
@Component({
  selector: 'app-platform-sessions-page',
  imports: [CanDirective, PageHeaderComponent, SectionCardComponent, ToolbarComponent, SearchBoxComponent, DataTableComponent, CellTemplateDirective, RowActionsTemplateDirective, BadgeComponent, IconComponent],
  template: `
    <app-page-header [title]="i18n.t('platform.sessions.title')" [subtitle]="i18n.t('platform.sessions.subtitle')">
      <button pageActions type="button" class="btn ghost" (click)="exportCsv()"><app-icon name="printer" [size]="15" />{{ i18n.t('platform.exportCsv') }}</button>
    </app-page-header>

    <app-section-card [padded]="false">
      <app-toolbar>
        <app-search-box toolbarFilters [placeholder]="i18n.t('platform.sessions.searchPlaceholder')" (searchChange)="facade.setSearch($event)" />
        <button toolbarActions type="button" class="btn ghost" (click)="facade.sessions.reload()"><app-icon name="refresh" [size]="15" />{{ i18n.t('platform.refresh') }}</button>
      </app-toolbar>
      @if (facade.sessions.error()) {
        <p class="problem list-problem" role="alert">{{ i18n.t('platform.sessions.loadError') }}</p>
      }
      <app-data-table [columns]="columns" [rows]="facade.rows()" [rowId]="rowId" [loading]="facade.sessions.isLoading()" [interactive]="false"
                      [emptyTitle]="i18n.t('platform.sessions.empty')" [caption]="i18n.t('platform.sessions.title')">
        <ng-template appCell="user" [appCellOf]="facade.rows()" let-row>
          <strong>{{ row.displayName }}</strong>
          @if (row.isCurrent) {
            <span class="tag">{{ i18n.t('platform.sessions.current') }}</span>
          }
          <div class="small muted mono">{{ row.username }}</div>
        </ng-template>
        <ng-template appCell="status" [appCellOf]="facade.rows()" let-row>
          <app-badge [tone]="tone(row.status)">{{ i18n.t('platform.sessions.status.' + row.status) }}</app-badge>
        </ng-template>
        <ng-template appRowActions [appRowActionsOf]="facade.rows()" let-row>
          @if (!row.isCurrent) {
            <button type="button" class="btn sm danger" *appCan="permissions.platformManage" (click)="terminate(row)" [disabled]="facade.terminate.pending()">{{ i18n.t('platform.sessions.terminate') }}</button>
          }
        </ng-template>
      </app-data-table>
    </app-section-card>
  `,
  styleUrl: './platform-pages.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SessionsPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(SessionsFacade);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly csv = inject(CsvExportService);

  protected readonly permissions = PLATFORM_PERMISSIONS;
  protected readonly rowId = (row: AdminSessionItem): string => row.sessionId;

  protected readonly columns: readonly TableColumn<AdminSessionItem>[] = [
    { key: 'user', header: 'platform.sessions.column.user', value: (row) => row.displayName, sortable: true },
    { key: 'ipAddress', header: 'platform.sessions.column.ip', mono: true },
    { key: 'sinceUtc', header: 'platform.sessions.column.since', value: (row) => formatDateTime(row.sinceUtc, this.i18n.lang()), sortable: true },
    { key: 'lastSeenAtUtc', header: 'platform.sessions.column.lastSeen', value: (row) => formatDateTime(row.lastSeenAtUtc, this.i18n.lang()), secondary: true },
    { key: 'status', header: 'platform.sessions.column.status', width: '110px' },
  ];

  constructor() {
    void this.i18n.load('platform');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14)
  }

  protected tone(status: string): ReturnType<typeof statusTone> {
    return statusTone(status === 'Idle' ? 'Idle' : 'Active');
  }

  protected async terminate(row: AdminSessionItem): Promise<void> {
    const confirmed = await this.confirm.confirm({
      title: this.i18n.t('platform.sessions.terminate'),
      message: this.i18n.tf('platform.sessions.confirmTerminate', { user: row.displayName, ip: row.ipAddress ?? '—' }),
      danger: true,
      confirmLabel: this.i18n.t('platform.sessions.terminate'),
    });
    if (!confirmed) {
      return;
    }

    if ((await this.facade.terminate.execute(row.sessionId)) !== null) {
      this.toast.success(this.i18n.t('platform.sessions.terminated'));
    } else {
      const problem = this.facade.terminate.problem();
      this.toast.error(problem ? problemMessage(this.i18n, 'platform', problem) : this.i18n.t('shared.error.generic'));
    }
  }

  protected exportCsv(): void {
    this.csv.download(`admin-sessions-${fileStamp()}`, [
      { header: this.i18n.t('platform.sessions.column.user'), value: (row: AdminSessionItem) => row.displayName },
      { header: this.i18n.t('platform.sessions.column.username'), value: (row) => row.username },
      { header: this.i18n.t('platform.sessions.column.ip'), value: (row) => row.ipAddress },
      { header: this.i18n.t('platform.sessions.column.since'), value: (row) => row.sinceUtc },
      { header: this.i18n.t('platform.sessions.column.lastSeen'), value: (row) => row.lastSeenAtUtc },
      { header: this.i18n.t('platform.sessions.column.status'), value: (row) => this.i18n.t(`platform.sessions.status.${row.status}`) },
    ], this.facade.rows());
  }
}
