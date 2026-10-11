import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';

import {
  ACCOUNT_PERMISSIONS,
  SUPPORT_TICKET_PRIORITIES,
  SUPPORT_TICKET_STATUSES,
  type SupportTicketDetail,
  type SupportTicketListItem,
} from '../../../core/api/account.contracts';
import type { PageRequest } from '../../../core/api/paging';
import { ProblemError, type ProblemDetails } from '../../../core/api/problem-details';
import { AuthStore } from '../../../core/auth/auth.store';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { DrawerComponent } from '../../../shared/ui/drawer/drawer.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { formatDateTime, pick } from '../data/account-format';
import { SupportDeskFacade } from '../state/support.facade';
import { priorityTone, statusTone } from '../ui/support-tones';
import { TicketThreadComponent } from '../ui/ticket-thread.component';

/**
 * The platform support desk (Super Admin portal, the desk side of CP-67): every laboratory's tickets, Open first;
 * the desk answers (an open ticket moves to In progress), starts, resolves with an optional note and closes.
 * `support.manage` in the platform tenant; the server refuses any other tenant (SUP-403).
 */
@Component({
  selector: 'app-support-desk-page',
  imports: [
    CanDirective,
    PageHeaderComponent,
    SectionCardComponent,
    ToolbarComponent,
    SearchBoxComponent,
    DataTableComponent,
    CellTemplateDirective,
    PaginatorComponent,
    BadgeComponent,
    DrawerComponent,
    IconComponent,
    TicketThreadComponent,
  ],
  templateUrl: './support-desk.page.html',
  styleUrl: './support.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SupportDeskPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(SupportDeskFacade);
  private readonly auth = inject(AuthStore);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);

  protected readonly permissions = ACCOUNT_PERMISSIONS;
  protected readonly statuses = SUPPORT_TICKET_STATUSES;
  protected readonly priorities = SUPPORT_TICKET_PRIORITIES;
  protected readonly statusTone = statusTone;
  protected readonly priorityTone = priorityTone;

  protected readonly columns: readonly TableColumn<SupportTicketListItem>[] = [
    { key: 'number', header: 'account.support.number', mono: true, width: '150px' },
    { key: 'laboratory', header: 'account.support.laboratory', value: (row) => this.laboratory(row) },
    { key: 'subject', header: 'account.support.subject' },
    { key: 'priority', header: 'account.support.priorityLabel', width: '110px' },
    { key: 'status', header: 'account.support.statusLabel', width: '130px' },
    { key: 'lastActivityAtUtc', header: 'account.support.lastActivity', width: '170px', secondary: true, value: (row) => formatDateTime(row.lastActivityAtUtc, this.i18n.lang()) },
  ];

  protected readonly rows = computed<readonly SupportTicketListItem[]>(() => (this.facade.tickets.hasValue() ? this.facade.tickets.value().items : []));
  protected readonly totalCount = computed(() => (this.facade.tickets.hasValue() ? this.facade.tickets.value().totalCount : 0));
  protected readonly detail = computed<SupportTicketDetail | null>(() => (this.facade.detail.hasValue() ? (this.facade.detail.value() ?? null) : null));
  protected readonly canAnswer = computed(() => this.auth.hasPermission(ACCOUNT_PERMISSIONS.supportManage));
  protected readonly threadOpen = computed(() => this.facade.selectedId() !== null);

  /** SUP-403 (not the platform tenant) and the other refusals arrive as typed problems. */
  protected readonly loadError = computed(() => {
    const error = this.facade.tickets.error();
    return error instanceof ProblemError ? this.problemText(error.problem) : this.i18n.t('account.support.loadError');
  });

  constructor() {
    void this.i18n.load('account');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14, A-01/C-13)
  }

  protected laboratory(row: { readonly laboratoryNameEn: string | null; readonly laboratoryNameAr: string | null; readonly tenantId: string }): string {
    return pick(row.laboratoryNameEn, row.laboratoryNameAr, this.i18n.lang()) || row.tenantId;
  }

  protected onStatus(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setStatus(this.statuses.find((status) => status === value) ?? null);
  }

  protected onPriority(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setPriority(this.priorities.find((priority) => priority === value) ?? null);
  }

  protected onPage(page: PageRequest): void {
    this.facade.setPage(page);
  }

  protected onThreadOpenChange(open: boolean): void {
    if (!open) {
      this.facade.select(null);
    }
  }

  protected async answer(text: string): Promise<void> {
    const ticket = this.detail();
    if (ticket !== null && (await this.facade.answer.execute({ id: ticket.id, text })) !== null) {
      this.toast.success(this.i18n.t('account.support.replied'));
    }
  }

  protected async start(ticket: SupportTicketDetail): Promise<void> {
    if ((await this.facade.start.execute(ticket.id)) !== null) {
      this.toast.success(this.i18n.t('account.support.started'));
    }
  }

  protected async resolve(ticket: SupportTicketDetail): Promise<void> {
    const note = await this.confirm.promptReason({
      title: this.i18n.t('account.support.resolve'),
      message: this.i18n.tf('account.support.promptResolve', { number: ticket.number }),
      label: this.i18n.t('account.support.resolutionNote'),
      required: false,
      maxLength: 4000,
    });
    if (note === null) {
      return;
    }

    if ((await this.facade.resolve.execute({ id: ticket.id, note: note === '' ? null : note })) !== null) {
      this.toast.success(this.i18n.t('account.support.resolved'));
    }
  }

  protected async close(ticket: SupportTicketDetail): Promise<void> {
    const confirmed = await this.confirm.confirm({ title: this.i18n.t('account.support.close'), message: this.i18n.tf('account.support.confirmClose', { number: ticket.number }) });
    if (confirmed && (await this.facade.close.execute(ticket.id)) !== null) {
      this.toast.success(this.i18n.t('account.support.closed'));
    }
  }

  protected threadProblem(): string | null {
    const problem = this.facade.answer.problem() ?? this.facade.start.problem() ?? this.facade.resolve.problem() ?? this.facade.close.problem();
    return problem === null ? null : this.problemText(problem);
  }

  protected problemText(problem: ProblemDetails | null): string {
    return problemMessage(this.i18n, 'account', problem);
  }
}
