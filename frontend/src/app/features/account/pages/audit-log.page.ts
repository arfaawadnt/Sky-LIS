import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';

import { ACCOUNT_PERMISSIONS, type AuditActorItem } from '../../../core/api/account.contracts';
import type { AuditClientItem, AuditEntryItem } from '../../../core/api/audit.contracts';
import type { PageRequest } from '../../../core/api/paging';
import type { ProblemDetails } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PortalService } from '../../../core/portal/portal.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { DrawerComponent } from '../../../shared/ui/drawer/drawer.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { formatDateTime, formatNumber } from '../data/account-format';
import { saveFile } from '../data/file-download';
import { AuditLogFacade, EMPTY_AUDIT_FILTER } from '../state/audit-log.facade';

/** At most this many characters of the details column; the drawer shows everything. */
const SUMMARY_LENGTH = 140;

/**
 * The audit log (SRS CP-68 for a laboratory, SA-09 for the platform tenant): filters (period, user, entity type,
 * entity id), the unified entries (field changes with before/after and trail events with their payload), a detail
 * drawer with the before/after diff, the CSV export (`audit.export`, newest 5 000 rows) and the tamper-evidence check
 * of the trail ("verify chain"). Immutable: nothing here edits a record (BR-15).
 */
@Component({
  selector: 'app-audit-log-page',
  imports: [
    ReactiveFormsModule,
    CanDirective,
    PageHeaderComponent,
    SectionCardComponent,
    DataTableComponent,
    CellTemplateDirective,
    PaginatorComponent,
    BadgeComponent,
    DrawerComponent,
    FormFieldComponent,
    IconComponent,
  ],
  templateUrl: './audit-log.page.html',
  styleUrl: './audit-log.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuditLogPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(AuditLogFacade);
  private readonly portal = inject(PortalService);
  private readonly toast = inject(ToastService);
  private readonly document = inject(DOCUMENT);

  protected readonly permissions = ACCOUNT_PERMISSIONS;

  protected readonly form = new FormGroup({
    fromDay: new FormControl('', { nonNullable: true }),
    toDay: new FormControl('', { nonNullable: true }),
    userId: new FormControl('', { nonNullable: true }),
    entityType: new FormControl('', { nonNullable: true }),
    entityId: new FormControl('', { nonNullable: true }),
    clientTenantId: new FormControl('', { nonNullable: true }),
  });

  protected readonly isPlatform = this.portal.isPlatform;

  /** SA-09 adds the client the entry concerns (UAT C-18); a laboratory's own log has no such column. */
  protected readonly columns = computed<readonly TableColumn<AuditEntryItem>[]>(() => [
    { key: 'atUtc', header: 'account.audit.time', width: '170px', value: (row) => formatDateTime(row.atUtc, this.i18n.lang()) },
    { key: 'actorName', header: 'account.audit.user', width: '160px', value: (row) => row.actorName ?? this.i18n.t('account.audit.system') },
    ...(this.isPlatform() ? [{ key: 'client', header: 'account.audit.client', width: '170px', value: (row: AuditEntryItem) => this.client(row) }] : []),
    { key: 'action', header: 'account.audit.action', width: '190px' },
    { key: 'entityType', header: 'account.audit.entity', width: '170px' },
    { key: 'details', header: 'account.audit.details', value: (row) => this.summary(row) },
    { key: 'entityId', header: 'account.audit.entityId', mono: true, secondary: true, width: '120px', value: (row) => this.shortId(row.entityId) },
  ]);

  protected readonly rows = computed<readonly AuditEntryItem[]>(() => (this.facade.entries.hasValue() ? this.facade.entries.value().items : []));
  protected readonly totalCount = computed(() => (this.facade.entries.hasValue() ? this.facade.entries.value().totalCount : 0));
  protected readonly actors = computed<readonly AuditActorItem[]>(() => (this.facade.actors.hasValue() ? this.facade.actors.value() : []));
  protected readonly clients = computed<readonly AuditClientItem[]>(() => (this.facade.clients.hasValue() ? this.facade.clients.value() : []));

  /** Entity types seen on the page, offered as suggestions (the field stays free text). */
  protected readonly entityTypes = computed(() => [...new Set(this.rows().map((row) => row.entityType))].sort((a, b) => a.localeCompare(b)));

  constructor() {
    void this.i18n.load('account');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14, A-01/C-13)
  }

  protected apply(): void {
    this.facade.apply(this.form.getRawValue());
  }

  protected reset(): void {
    this.form.reset(EMPTY_AUDIT_FILTER);
    this.facade.reset();
  }

  protected onPage(page: PageRequest): void {
    this.facade.setPage(page);
  }

  protected open(row: AuditEntryItem): void {
    this.facade.selected.set(row);
  }

  protected onDrawer(open: boolean): void {
    if (!open) {
      this.facade.selected.set(null);
    }
  }

  protected showRecord(entry: AuditEntryItem): void {
    this.form.reset({ ...EMPTY_AUDIT_FILTER, entityType: entry.entityType, entityId: entry.entityId });
    this.facade.showRecord(entry.entityType, entry.entityId);
  }

  protected async verify(): Promise<void> {
    await this.facade.verify.execute(undefined);
  }

  protected async exportCsv(): Promise<void> {
    const file = await this.facade.export.execute(undefined);
    if (file !== null) {
      saveFile(this.document, file);
      this.toast.success(this.i18n.t('account.audit.exported'));
    }
  }

  protected summary(row: AuditEntryItem): string {
    const text = row.changes && row.changes.length > 0 ? row.changes.map((change) => `${change.field}: ${change.before ?? '∅'} → ${change.after ?? '∅'}`).join('; ') : (row.payload ?? '');
    return text.length > SUMMARY_LENGTH ? `${text.slice(0, SUMMARY_LENGTH)}…` : text;
  }

  protected pretty(payload: string | null): string {
    if (!payload) {
      return '';
    }

    try {
      return JSON.stringify(JSON.parse(payload) as unknown, null, 2);
    } catch {
      return payload;
    }
  }

  protected when(value: string): string {
    return formatDateTime(value, this.i18n.lang());
  }

  protected count(value: number): string {
    return formatNumber(value, this.i18n.lang());
  }

  /** The client's name in the interface language (SA-09); "—" for the platform's own records. */
  protected client(row: AuditEntryItem): string {
    if (!row.clientTenantId) {
      return '—';
    }

    const name = this.i18n.lang() === 'ar' ? (row.clientNameAr ?? row.clientNameEn) : (row.clientNameEn ?? row.clientNameAr);
    return name ?? this.shortId(row.clientTenantId);
  }

  protected clientName(client: AuditClientItem): string {
    return this.i18n.lang() === 'ar' ? client.nameAr || client.nameEn : client.nameEn || client.nameAr;
  }

  protected kindLabel(kind: string): string {
    return this.i18n.t(kind === 'Event' ? 'account.audit.kind.event' : 'account.audit.kind.change');
  }

  protected problemText(problem: ProblemDetails | null): string {
    return problemMessage(this.i18n, 'account', problem);
  }

  private shortId(id: string): string {
    return id.length > 13 ? `${id.slice(0, 8)}…` : id;
  }
}
