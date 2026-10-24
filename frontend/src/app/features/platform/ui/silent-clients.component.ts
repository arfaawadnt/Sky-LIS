import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output } from '@angular/core';

import type { ClientStatus, SilentClientItem } from '../../../core/api/platform.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { CsvExportService } from '../data/csv-export';
import { SilentClientsFacade } from '../state/silent-clients.facade';
import { formatDate, formatDateTime, pick } from './format';
import { statusTone } from './status';

/**
 * LIS-CR-007 A-01: the table of the SA-04 "Silent" filter — the Trial and Active clients with no sign-in and no
 * registration for `days` days, longest silence first, with the administrator to call. The clients page's search box and
 * status select narrow the rows in the browser (the list is small and never paged); `openClient` asks the page to open
 * the client's drawer. Export goes through `exportCsv()` (the server's CSV in the reader's language).
 */
@Component({
  selector: 'app-silent-clients',
  imports: [DataTableComponent, CellTemplateDirective, RowActionsTemplateDirective, BadgeComponent, IconComponent],
  providers: [SilentClientsFacade],
  template: `
    <p class="silent-summary small" role="status">
      <app-icon name="alert" [size]="15" />
      <span>{{ summary() }}</span>
      @if (facade.trial() > 0) {
        <span class="muted">· {{ i18n.tc('platform.dashboard.silentTrial', facade.trial()) }}</span>
      }
    </p>
    @if (facade.list.error()) {
      <p class="problem list-problem" role="alert">{{ i18n.t('platform.clients.silent.loadError') }}</p>
    }
    <app-data-table [columns]="columns" [rows]="rows()" [loading]="facade.list.isLoading()" [emptyTitle]="i18n.t('platform.clients.silent.empty')"
                    [caption]="i18n.t('platform.clients.silent.title')" (rowActivate)="openClient.emit($event.id)">
      <ng-template appCell="name" [appCellOf]="rows()" let-row>
        <strong>{{ name(row) }}</strong>
        <div class="small muted mono">{{ row.portalUrl }}</div>
      </ng-template>
      <ng-template appCell="status" [appCellOf]="rows()" let-row>
        <app-badge [tone]="tone(row.status)">{{ i18n.t('platform.status.' + row.status) }}</app-badge>
      </ng-template>
      <ng-template appCell="administrator" [appCellOf]="rows()" let-row>
        {{ row.administratorName }}
        <div class="small mono" dir="ltr"><a [href]="'tel:' + row.administratorMobile" [title]="i18n.t('platform.clients.silent.call')">{{ row.administratorMobile }}</a></div>
      </ng-template>
      <ng-template appCell="silentDays" [appCellOf]="rows()" let-row>
        <strong class="silent-days">{{ row.silentDays }}</strong>
      </ng-template>
      <ng-template appRowActions [appRowActionsOf]="rows()" let-row>
        <button type="button" class="row-btn" (click)="openClient.emit(row.id)" [attr.aria-label]="i18n.t('platform.clients.edit')" [title]="i18n.t('platform.clients.edit')"><app-icon name="edit" [size]="15" /></button>
      </ng-template>
    </app-data-table>
  `,
  styles: `
    .silent-summary {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--sp-2);
      margin: 0;
      padding-block: var(--sp-2);
      padding-inline: var(--sp-4);
      color: var(--bad);
    }

    .silent-days {
      color: var(--bad);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SilentClientsComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(SilentClientsFacade);
  private readonly csv = inject(CsvExportService);
  private readonly toast = inject(ToastService);

  /** The window: 1…90 days (the page offers 3 / 7 / 14 / 30). */
  readonly days = input.required<number>();
  /** The page's search text: name, portal address, administrator or mobile. */
  readonly search = input<string | null>(null);
  /** The page's status filter; Inactive matches nothing (inactive clients are never silent). */
  readonly status = input<ClientStatus | null>(null);

  readonly openClient = output<string>();

  protected readonly columns: readonly TableColumn<SilentClientItem>[] = [
    { key: 'name', header: 'platform.clients.column.client', value: (row) => this.name(row) },
    { key: 'status', header: 'platform.clients.column.status', width: '100px' },
    { key: 'administrator', header: 'platform.clients.silent.column.administrator', value: (row) => row.administratorName },
    { key: 'lastSignInAtUtc', header: 'platform.clients.silent.column.lastSignIn', value: (row) => this.when(row.lastSignInAtUtc), secondary: true },
    { key: 'lastRegistrationAtUtc', header: 'platform.clients.silent.column.lastRegistration', value: (row) => this.when(row.lastRegistrationAtUtc), secondary: true },
    { key: 'lastActivityAtUtc', header: 'platform.clients.silent.column.lastActivity', value: (row) => this.when(row.lastActivityAtUtc) },
    { key: 'silentDays', header: 'platform.clients.silent.column.days', align: 'end', width: '90px', sortable: true, value: (row) => row.silentDays },
    { key: 'createdAtUtc', header: 'platform.clients.column.created', value: (row) => formatDate(row.createdAtUtc, this.i18n.lang()), secondary: true },
  ];

  protected readonly rows = computed<readonly SilentClientItem[]>(() => {
    const search = (this.search() ?? '').trim().toLowerCase();
    const status = this.status();
    return this.facade.rows().filter(
      (row) =>
        (status === null || row.status === status) &&
        (search === '' || [row.nameEn, row.nameAr, row.portalUrl, row.administratorName, row.administratorMobile].some((text) => text.toLowerCase().includes(search))),
    );
  });

  protected readonly summary = computed(() => this.i18n.tc('platform.clients.silent.count', this.rows().length, { days: this.days() }));

  constructor() {
    void this.i18n.load('platform');
    effect(() => this.facade.setDays(this.days()));
  }

  /** The server's CSV of the window, handed to the browser's download. */
  async exportCsv(): Promise<void> {
    const file = await this.facade.export.execute();
    if (file !== null) {
      this.csv.downloadBlob(file.fileName, file.blob);
      return;
    }

    const problem = this.facade.export.problem();
    this.toast.error(problem ? problemMessage(this.i18n, 'platform', problem) : this.i18n.t('platform.clients.silent.exportError'));
  }

  /** Re-reads the list (the page's Refresh). */
  refresh(): void {
    this.facade.refresh();
  }

  protected name(row: SilentClientItem): string {
    return pick(row.nameEn, row.nameAr, this.i18n.lang());
  }

  protected tone(status: string): ReturnType<typeof statusTone> {
    return statusTone(status);
  }

  private when(iso: string | null): string {
    return iso === null ? this.i18n.t('platform.clients.silent.never') : formatDateTime(iso, this.i18n.lang());
  }
}
