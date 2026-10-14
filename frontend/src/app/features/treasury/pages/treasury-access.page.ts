import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import type { PageRequest } from '../../../core/api/paging';
import { TREASURY_ACCESS_STATUSES, TREASURY_PERMISSIONS, type TreasuryAccessRequestListItem, type TreasuryAccessStatus } from '../../../core/api/treasury.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { formatDateTime } from '../../../shared/patient/patient-demographics';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { csvFileName, downloadCsv, toCsv } from '../data/csv-export';
import { TreasuryAccessFacade } from '../state/treasury-access.facade';
import { TreasuryExportButtonComponent } from '../ui/export-button.component';

/** Refused by the server when the treasury is outside the user's branches (O-4); shown under the treasury field. */
const OUTSIDE_USER_BRANCHES = 'TRS-015';

/**
 * Treasury access (SRS CP-29): request access from this device, and decide on requests. The reference page of the
 * UI kit: page header, section cards, toolbar, data table with cell and row-action templates, paginator, form
 * fields, confirm/prompt dialogs and toasts. Every action is gated by `*appCan` and by the row's state; the
 * server still decides (D-04).
 */
@Component({
  selector: 'app-treasury-access-page',
  imports: [
    ReactiveFormsModule,
    CanDirective,
    SaveShortcutDirective,
    PageHeaderComponent,
    SectionCardComponent,
    ToolbarComponent,
    DataTableComponent,
    CellTemplateDirective,
    RowActionsTemplateDirective,
    PaginatorComponent,
    BadgeComponent,
    FormFieldComponent,
    IconComponent,
    TreasuryExportButtonComponent,
  ],
  templateUrl: './treasury-access.page.html',
  styleUrl: './treasury-access.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TreasuryAccessPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(TreasuryAccessFacade);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);

  protected readonly permissions = TREASURY_PERMISSIONS;
  protected readonly statuses = TREASURY_ACCESS_STATUSES;

  protected readonly columns: readonly TableColumn<TreasuryAccessRequestListItem>[] = [
    { key: 'number', header: 'treasury.number', mono: true, width: '150px' },
    { key: 'treasury', header: 'treasury.treasury' },
    { key: 'requestedByName', header: 'treasury.requestedBy' },
    { key: 'device', header: 'treasury.device', value: (row) => row.deviceLabel ?? row.deviceId, secondary: true },
    { key: 'status', header: 'treasury.status', width: '120px' },
    // the decision: who and when (UAT-ADM-07 expects the approver name and time on the row)
    { key: 'decidedByName', header: 'treasury.decidedBy', secondary: true, value: (row) => this.decision(row) },
    // D-05: the approval comment and the reason of a rejection, revocation or cancellation, for the requester and the approver
    { key: 'note', header: 'treasury.note', value: (row) => this.note(row) },
  ];

  /** "Comment: … · Reason: …" (either part only when present). */
  protected note(row: TreasuryAccessRequestListItem): string {
    const parts: string[] = [];
    if (row.approvalComment) {
      parts.push(this.i18n.tf('treasury.noteComment', { text: row.approvalComment }));
    }

    if (row.decisionReason) {
      parts.push(this.i18n.tf('treasury.noteReason', { text: row.decisionReason }));
    }

    return parts.join(' · ');
  }

  private decision(row: TreasuryAccessRequestListItem): string {
    if (row.decidedByName === null) {
      return '';
    }

    return row.decidedAtUtc === null ? row.decidedByName : `${row.decidedByName} · ${formatDateTime(row.decidedAtUtc, this.i18n.lang())}`;
  }

  protected readonly rows = computed<readonly TreasuryAccessRequestListItem[]>(() => (this.facade.requests.hasValue() ? this.facade.requests.value().items : []));

  protected readonly totalCount = computed(() => (this.facade.requests.hasValue() ? this.facade.requests.value().totalCount : 0));

  protected readonly form = new FormGroup({
    treasuryId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    deviceLabel: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(100)] }),
  });

  constructor() {
    void this.i18n.load('treasury');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT B-07, D-14)
  }

  protected treasuryName(item: TreasuryAccessRequestListItem): string {
    return `${item.treasuryCode} · ${localizedPick({ en: item.treasuryNameEn, ar: item.treasuryNameAr }, this.i18n.lang())}`;
  }

  protected onStatusChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setStatus(this.statuses.find((status) => status === value) ?? null);
  }

  protected onMineChange(event: Event): void {
    this.facade.setMine((event.target as HTMLInputElement).checked);
  }

  protected onPageChange(page: PageRequest): void {
    this.facade.setPage(page);
  }

  protected async submitRequest(): Promise<void> {
    if (!markAllTouched(this.form) || this.facade.request.pending()) {
      return;
    }

    const { treasuryId, deviceLabel } = this.form.getRawValue();
    const created = await this.facade.request.execute({ treasuryId, deviceLabel: deviceLabel.trim() === '' ? null : deviceLabel.trim() });
    if (created !== null) {
      this.form.controls.treasuryId.reset();
      this.toast.success(this.i18n.t('treasury.requested'));
      return;
    }

    if (this.facade.request.problem()?.code === OUTSIDE_USER_BRANCHES) {
      // O-4: a field error, under the treasury the server refused
      const control = this.form.controls.treasuryId;
      control.setErrors({ server: this.problemText(OUTSIDE_USER_BRANCHES) });
      control.markAsTouched();
    }
  }

  /** The banner of the request form; TRS-015 is shown under the treasury field instead. */
  protected readonly requestProblem = computed(() => {
    const problem = this.facade.request.problem();
    return problem === null || problem.code === OUTSIDE_USER_BRANCHES ? null : problem;
  });

  /** CSV of the requests of the current filter, with the approval comments and the decision reasons (D-05). */
  protected readonly exportCsv = async (): Promise<void> => {
    const rows = await this.facade.allRequests();
    const t = (key: string): string => this.i18n.t(`treasury.${key}`);
    const when = (value: string | null): string => (value === null ? '' : formatDateTime(value, this.i18n.lang()));
    downloadCsv(
      csvFileName('treasury-access-requests'),
      toCsv(rows, [
        { header: t('number'), value: (row) => row.number },
        { header: t('treasury'), value: (row) => this.treasuryName(row) },
        { header: t('requestedBy'), value: (row) => row.requestedByName },
        { header: t('csv.deviceLabel'), value: (row) => row.deviceLabel },
        { header: t('csv.deviceId'), value: (row) => row.deviceId },
        { header: t('status'), value: (row) => this.statusLabel(row.status) },
        { header: t('csv.requestedAt'), value: (row) => when(row.requestedAtUtc) },
        { header: t('decidedBy'), value: (row) => row.decidedByName },
        { header: t('csv.decidedAt'), value: (row) => when(row.decidedAtUtc) },
        { header: t('csv.approvalComment'), value: (row) => row.approvalComment },
        { header: t('csv.decisionReason'), value: (row) => row.decisionReason },
      ]),
    );
  };

  /** T2 with the optional comment of the approval (D-05); the dialog's empty answer approves without one. */
  protected async approve(item: TreasuryAccessRequestListItem): Promise<void> {
    const comment = await this.confirm.promptReason({
      title: this.i18n.t('treasury.approve'),
      message: this.i18n.tf('treasury.confirmApprove', { number: item.number }),
      label: this.i18n.t('treasury.approvalComment'),
      confirmLabel: this.i18n.t('treasury.approve'),
      required: false,
      maxLength: 1000,
    });
    if (comment === null) {
      return;
    }

    if ((await this.facade.approve.execute({ id: item.id, comment: comment === '' ? null : comment })) !== null) {
      this.toast.success(this.i18n.t('treasury.approved'));
    }
  }

  protected async reject(item: TreasuryAccessRequestListItem): Promise<void> {
    const reason = await this.confirm.promptReason({ title: this.i18n.t('treasury.reject'), message: this.i18n.tf('treasury.promptReject', { number: item.number }), danger: true });
    if (reason === null) {
      return;
    }

    if ((await this.facade.reject.execute({ id: item.id, reason })) !== null) {
      this.toast.success(this.i18n.t('treasury.rejected'));
    }
  }

  protected async revoke(item: TreasuryAccessRequestListItem): Promise<void> {
    const reason = await this.confirm.promptReason({ title: this.i18n.t('treasury.revoke'), message: this.i18n.tf('treasury.promptRevoke', { number: item.number }), danger: true });
    if (reason === null) {
      return;
    }

    if ((await this.facade.revoke.execute({ id: item.id, reason })) !== null) {
      this.toast.success(this.i18n.t('treasury.revoked'));
    }
  }

  protected async cancel(item: TreasuryAccessRequestListItem): Promise<void> {
    const reason = await this.confirm.promptReason({ title: this.i18n.t('treasury.cancel'), message: this.i18n.tf('treasury.promptCancel', { number: item.number }), required: false });
    if (reason === null) {
      return;
    }

    if ((await this.facade.cancel.execute({ id: item.id, reason: reason === '' ? null : reason })) !== null) {
      this.toast.success(this.i18n.t('treasury.cancelled'));
    }
  }

  /** Approve on another user's pending row, and on the administrator's own one (SOD-TRS-001 exception); Reject never on an own row. */
  protected canApprove(item: TreasuryAccessRequestListItem): boolean {
    return item.status === 'Pending' && (!this.facade.isOwn(item) || this.facade.isPrimaryAdministrator());
  }

  protected canReject(item: TreasuryAccessRequestListItem): boolean {
    return item.status === 'Pending' && !this.facade.isOwn(item);
  }

  protected canCancel(item: TreasuryAccessRequestListItem): boolean {
    return item.status === 'Pending' && this.facade.isOwn(item);
  }

  protected statusLabel(status: TreasuryAccessStatus): string {
    return this.i18n.t(`treasury.status.${status}`);
  }

  protected problemText(code: string): string {
    return problemMessage(this.i18n, 'treasury', { code });
  }
}
