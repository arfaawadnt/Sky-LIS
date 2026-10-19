import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';

import {
  type ClientListItem,
  type ClientLoginLogItem,
  LOGIN_LOG_DEVICES,
  LOGIN_LOG_STATUSES,
  type LoginLogDevice,
  type LoginLogStatus,
} from '../../../core/api/platform.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import type { BadgeTone } from '../../../shared/ui/badge/status-tones';
import { CellTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { CsvExportService } from '../data/csv-export';
import { EMPTY_LOGIN_LOG_CRITERIA, type LoginLogCriteria, LoginLogsFacade } from '../state/login-logs.facade';
import { formatNumber, pick } from '../ui/format';
import { formatMinutes, formatUtcDateTime, utcInputToIso } from '../ui/utc-time';

const STATUS_TONES: Readonly<Record<LoginLogStatus, BadgeTone>> = { Active: 'ok', SignedOut: 'neutral', Revoked: 'bad', Expired: 'warn' };

/**
 * SA-08b Client login logs (change request LIS-CR-003 §2): the history of the client users' browser sessions, one row
 * per session with client, user, login / logout (UTC), address, device, duration and how it ended. The filter card
 * (client, user, two windows, device, duration bounds, status) applies on submit; the table sorts and pages on the
 * server; the CSV comes from the server with the same filter and order.
 */
@Component({
  selector: 'app-platform-login-logs-page',
  imports: [ReactiveFormsModule, PageHeaderComponent, SectionCardComponent, ToolbarComponent, DataTableComponent, CellTemplateDirective, PaginatorComponent, BadgeComponent, FormFieldComponent, IconComponent],
  templateUrl: './login-logs.page.html',
  styleUrl: './platform-pages.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginLogsPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(LoginLogsFacade);
  private readonly toast = inject(ToastService);
  private readonly csv = inject(CsvExportService);

  protected readonly devices = LOGIN_LOG_DEVICES;
  protected readonly statuses = LOGIN_LOG_STATUSES;
  protected readonly rowId = (row: ClientLoginLogItem): string => row.familyId;

  /** A filter the server would refuse (a reversed window, crossed minutes) is said here and not sent. */
  protected readonly criteriaProblem = signal<string | null>(null);

  protected readonly form = new FormGroup({
    tenantId: new FormControl('', { nonNullable: true }),
    search: new FormControl('', { nonNullable: true }),
    loginFrom: new FormControl('', { nonNullable: true }),
    loginTo: new FormControl('', { nonNullable: true }),
    logoutFrom: new FormControl('', { nonNullable: true }),
    logoutTo: new FormControl('', { nonNullable: true }),
    device: new FormControl('', { nonNullable: true }),
    status: new FormControl('', { nonNullable: true }),
    minMinutes: new FormControl('', { nonNullable: true }),
    maxMinutes: new FormControl('', { nonNullable: true }),
  });

  protected readonly columns: readonly TableColumn<ClientLoginLogItem>[] = [
    { key: 'client', header: 'platform.loginLogs.column.client', sortable: true, value: (row) => this.clientName(row) },
    { key: 'user', header: 'platform.loginLogs.column.user', sortable: true, value: (row) => row.displayName },
    { key: 'loginAtUtc', header: 'platform.loginLogs.column.login', sortable: true, value: (row) => formatUtcDateTime(row.loginAtUtc) },
    { key: 'logoutAtUtc', header: 'platform.loginLogs.column.logout', sortable: true, value: (row) => formatUtcDateTime(row.logoutAtUtc) },
    { key: 'ipAddress', header: 'platform.loginLogs.column.ip', mono: true, secondary: true, value: (row) => row.ipAddress ?? '—' },
    { key: 'device', header: 'platform.loginLogs.column.device' },
    { key: 'durationMinutes', header: 'platform.loginLogs.column.duration', align: 'end', sortable: true, width: '110px', value: (row) => formatMinutes(row.durationMinutes) },
    { key: 'status', header: 'platform.loginLogs.column.status', sortable: true, width: '130px' },
  ];

  constructor() {
    void this.i18n.load('platform');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14)
  }

  protected clientName(row: ClientLoginLogItem): string {
    const name = pick(row.clientNameEn, row.clientNameAr, this.i18n.lang());
    return name.length > 0 ? name : this.i18n.t('platform.loginLogs.noClientRecord');
  }

  protected optionName(client: ClientListItem): string {
    return pick(client.nameEn, client.nameAr, this.i18n.lang());
  }

  protected tone(status: LoginLogStatus): BadgeTone {
    return STATUS_TONES[status];
  }

  protected count(): string {
    const total = this.facade.totalCount();
    return this.i18n.tc('platform.loginLogs.count', total, { count: formatNumber(total, this.i18n.lang()) });
  }

  /** The filter card: every empty field means "any"; the two windows and the minutes must be ordered. */
  protected apply(): void {
    const value = this.form.getRawValue();
    const criteria: LoginLogCriteria = {
      tenantId: value.tenantId === '' ? null : value.tenantId,
      search: value.search.trim() === '' ? null : value.search.trim(),
      loginFromUtc: utcInputToIso(value.loginFrom),
      loginToUtc: utcInputToIso(value.loginTo),
      logoutFromUtc: utcInputToIso(value.logoutFrom),
      logoutToUtc: utcInputToIso(value.logoutTo),
      device: value.device === '' ? null : (value.device as LoginLogDevice),
      status: value.status === '' ? null : (value.status as LoginLogStatus),
      minMinutes: minutes(value.minMinutes),
      maxMinutes: minutes(value.maxMinutes),
    };

    const reversedWindow = (criteria.loginFromUtc !== null && criteria.loginToUtc !== null && criteria.loginFromUtc > criteria.loginToUtc)
      || (criteria.logoutFromUtc !== null && criteria.logoutToUtc !== null && criteria.logoutFromUtc > criteria.logoutToUtc);
    const crossedMinutes = criteria.minMinutes !== null && criteria.maxMinutes !== null && criteria.minMinutes > criteria.maxMinutes;
    if (reversedWindow || crossedMinutes) {
      this.criteriaProblem.set(this.i18n.t(reversedWindow ? 'platform.loginLogs.reversedWindow' : 'platform.loginLogs.crossedMinutes'));
      return;
    }

    this.criteriaProblem.set(null);
    this.facade.applyCriteria(criteria);
  }

  protected reset(): void {
    this.form.reset();
    this.criteriaProblem.set(null);
    this.facade.applyCriteria(EMPTY_LOGIN_LOG_CRITERIA);
  }

  protected async exportCsv(): Promise<void> {
    const file = await this.facade.export.execute();
    if (file !== null) {
      this.csv.downloadBlob(file.fileName, file.blob);
      return;
    }

    const problem = this.facade.export.problem();
    this.toast.error(problem ? problemMessage(this.i18n, 'platform', problem) : this.i18n.t('platform.loginLogs.exportError'));
  }
}

/** A typed minutes bound: a non-negative whole number, else none. */
function minutes(value: string): number | null {
  const text = value.trim();
  if (text === '' || !/^\d+$/.test(text)) {
    return null;
  }

  return Number.parseInt(text, 10);
}
