import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import {
  ACCOUNT_PERMISSIONS,
  SUPPORT_TICKET_PRIORITIES,
  SUPPORT_TICKET_STATUSES,
  type SupportTicketDetail,
  type SupportTicketListItem,
  type SupportTicketPriority,
} from '../../../core/api/account.contracts';
import type { PageRequest } from '../../../core/api/paging';
import type { ProblemDetails } from '../../../core/api/problem-details';
import { AuthStore } from '../../../core/auth/auth.store';
import { I18nService } from '../../../core/i18n/i18n.service';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { problemToFormErrors } from '../../../shared/forms/problem-to-form-errors';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { requiredText } from '../../../shared/forms/validators';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { DrawerComponent } from '../../../shared/ui/drawer/drawer.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { formatDateTime } from '../data/account-format';
import { SupportFacade } from '../state/support.facade';
import { priorityTone, statusTone } from '../ui/support-tones';
import { TicketThreadComponent } from '../ui/ticket-thread.component';

/**
 * Support (SRS CP-67): the laboratory's tickets (number, subject, priority, status, opened by, last activity) with
 * filters; "New ticket" (`support.create`) opens a drawer; a row opens the conversation, where the laboratory replies,
 * reopens a resolved ticket or closes it. National Technology answers from the support desk.
 */
@Component({
  selector: 'app-support-page',
  imports: [
    ReactiveFormsModule,
    SaveShortcutDirective,
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
    FormFieldComponent,
    IconComponent,
    TicketThreadComponent,
  ],
  templateUrl: './support.page.html',
  styleUrl: './support.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SupportPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(SupportFacade);
  private readonly auth = inject(AuthStore);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);

  protected readonly permissions = ACCOUNT_PERMISSIONS;
  protected readonly statuses = SUPPORT_TICKET_STATUSES;
  protected readonly priorities = SUPPORT_TICKET_PRIORITIES;
  protected readonly statusTone = statusTone;
  protected readonly priorityTone = priorityTone;

  protected readonly newOpen = signal(false);

  protected readonly form = new FormGroup({
    subject: new FormControl('', { nonNullable: true, validators: [requiredText, Validators.maxLength(200)] }),
    description: new FormControl('', { nonNullable: true, validators: [requiredText, Validators.maxLength(4000)] }),
    priority: new FormControl<SupportTicketPriority>('Normal', { nonNullable: true, validators: [Validators.required] }),
  });

  protected readonly columns: readonly TableColumn<SupportTicketListItem>[] = [
    { key: 'number', header: 'account.support.number', mono: true, width: '150px' },
    { key: 'subject', header: 'account.support.subject' },
    { key: 'priority', header: 'account.support.priorityLabel', width: '110px' },
    { key: 'status', header: 'account.support.statusLabel', width: '130px' },
    { key: 'openedByName', header: 'account.support.openedByLabel', secondary: true },
    { key: 'lastActivityAtUtc', header: 'account.support.lastActivity', width: '170px', secondary: true, value: (row) => formatDateTime(row.lastActivityAtUtc, this.i18n.lang()) },
  ];

  protected readonly rows = computed<readonly SupportTicketListItem[]>(() => (this.facade.tickets.hasValue() ? this.facade.tickets.value().items : []));
  protected readonly totalCount = computed(() => (this.facade.tickets.hasValue() ? this.facade.tickets.value().totalCount : 0));
  protected readonly detail = computed<SupportTicketDetail | null>(() => (this.facade.detail.hasValue() ? (this.facade.detail.value() ?? null) : null));
  protected readonly canCreate = computed(() => this.auth.hasPermission(ACCOUNT_PERMISSIONS.supportCreate));

  protected readonly threadOpen = computed(() => this.facade.selectedId() !== null);

  constructor() {
    void this.i18n.load('account');
  }

  protected onStatus(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setStatus(this.statuses.find((status) => status === value) ?? null);
  }

  protected onPriority(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setPriority(this.priorities.find((priority) => priority === value) ?? null);
  }

  protected onMine(event: Event): void {
    this.facade.setMine((event.target as HTMLInputElement).checked);
  }

  protected onPage(page: PageRequest): void {
    this.facade.setPage(page);
  }

  protected openNew(): void {
    this.form.reset({ subject: '', description: '', priority: 'Normal' });
    this.facade.open.reset();
    this.newOpen.set(true);
  }

  protected async submitNew(): Promise<void> {
    if (!markAllTouched(this.form) || this.facade.open.pending()) {
      return;
    }

    const value = this.form.getRawValue();
    const created = await this.facade.open.execute({ subject: value.subject.trim(), description: value.description.trim(), priority: value.priority });
    if (created === null) {
      problemToFormErrors(this.facade.open.problem(), this.form);
      return;
    }

    this.newOpen.set(false);
    this.toast.success(this.i18n.t('account.support.opened'));
  }

  protected onThreadOpenChange(open: boolean): void {
    if (!open) {
      this.facade.select(null);
    }
  }

  protected async sendReply(text: string): Promise<void> {
    const ticket = this.detail();
    if (ticket !== null && (await this.facade.reply.execute({ id: ticket.id, text })) !== null) {
      this.toast.success(this.i18n.t('account.support.replied'));
    }
  }

  protected async reopen(ticket: SupportTicketDetail): Promise<void> {
    if ((await this.facade.reopen.execute(ticket.id)) !== null) {
      this.toast.success(this.i18n.t('account.support.reopened'));
    }
  }

  protected async close(ticket: SupportTicketDetail): Promise<void> {
    const confirmed = await this.confirm.confirm({ title: this.i18n.t('account.support.close'), message: this.i18n.tf('account.support.confirmClose', { number: ticket.number }) });
    if (confirmed && (await this.facade.close.execute(ticket.id)) !== null) {
      this.toast.success(this.i18n.t('account.support.closed'));
    }
  }

  protected threadProblem(): string | null {
    const problem = this.facade.reply.problem() ?? this.facade.reopen.problem() ?? this.facade.close.problem();
    return problem === null ? null : this.problemText(problem);
  }

  protected problemText(problem: ProblemDetails | null): string {
    return problemMessage(this.i18n, 'account', problem);
  }
}
