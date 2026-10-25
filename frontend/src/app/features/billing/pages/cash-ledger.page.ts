import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';

import {
  BILLING_PERMISSIONS,
  type CashEntryKind,
  type CashLedgerRow,
  type CashLedgerScope,
  type CashLedgerView,
  type PostCashEntryRequest,
} from '../../../core/api/billing.contracts';
import type { PageRequest } from '../../../core/api/paging';
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
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { StatCardComponent } from '../../../shared/ui/stat-card/stat-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { buildLedgerWorkbook, ledgerFileName } from '../data/cash-ledger-excel';
import { BillingPrinter } from '../state/billing-printer';
import { CashDeskFacade } from '../state/cash-desk.facade';
import { CashLedgerFacade } from '../state/cash-ledger.facade';
import { formatCount, formatDateTime, formatMoney } from '../ui/billing-format';
import { problemOf } from '../ui/billing-view';
import { CashEntryDialogComponent } from '../ui/cash-entry-dialog.component';
import { canVoidLine, ledgerDetail, ledgerScopeLabel, ledgerTone } from '../ui/cash-ledger-view';
import { dayInputValue, earliestReportDay } from '../ui/collections-report-view';
import { type DayCloseInput, DayCloseDialogComponent } from '../ui/day-close-dialog.component';
import { saveFile } from '../ui/save-file';

/** Lines per page of the table (the ledger itself is read whole: the summary covers every line). */
export const LEDGER_PAGE_SIZE = 50;

/**
 * CP-70 Cash ledger (change request LIS-CR-011, point C-15): one page a laboratory owner reads at a glance — the day book of
 * the session branch's drawer (own, one cashier or every cashier with the CP-37 right), opening balance, receipts by method,
 * refunds, voids, cash in and out, closing balance and the running balance per line; *Add cash out* / *Add cash in*; the
 * void of an entry (billing.approve); *Close the day* through the closing of CP-30 (billing.manage); Excel export
 * (billing.export) and the printable A4 "Daily cash sheet". Past days and closed periods are read-only.
 */
@Component({
  selector: 'app-billing-cash-ledger-page',
  imports: [
    CanDirective,
    PageHeaderComponent,
    SectionCardComponent,
    ToolbarComponent,
    DataTableComponent,
    CellTemplateDirective,
    RowActionsTemplateDirective,
    PaginatorComponent,
    BadgeComponent,
    IconComponent,
    StatCardComponent,
    CashEntryDialogComponent,
    DayCloseDialogComponent,
  ],
  templateUrl: './cash-ledger.page.html',
  styleUrl: './cash-ledger.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CashLedgerPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(CashLedgerFacade);
  protected readonly desk = inject(CashDeskFacade);
  private readonly printer = inject(BillingPrinter);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);

  protected readonly permissions = BILLING_PERMISSIONS;
  protected readonly today = dayInputValue(new Date());

  protected readonly view = this.facade.current;

  protected readonly problem = computed(() => problemOf(this.facade.ledger.error()));

  protected readonly canViewBranch = computed(() => this.facade.currentPrivileges()?.canViewBranchCollections === true);

  protected readonly earliest = computed(() => earliestReportDay(this.facade.currentPrivileges()?.reportDaysLimit ?? null));

  protected readonly entryKind = signal<CashEntryKind>('CashOut');
  protected readonly entryOpen = signal(false);
  protected readonly closeOpen = signal(false);
  protected readonly page = signal<PageRequest>({ page: 1, pageSize: LEDGER_PAGE_SIZE });

  protected readonly rows = computed<readonly CashLedgerRow[]>(() => this.view()?.rows ?? []);

  protected readonly pagedRows = computed<readonly CashLedgerRow[]>(() => {
    const { page, pageSize } = this.page();
    return this.rows().slice((page - 1) * pageSize, page * pageSize);
  });

  /** The ledger covers the present (entries post now) and something is left to close. */
  protected readonly canAddEntries = computed(() => {
    const view = this.view();
    return view !== null && view.fromDay <= this.today && this.today <= view.toDay;
  });

  protected readonly scopeLabel = computed(() => {
    const view = this.view();
    return view === null ? '' : ledgerScopeLabel(this.i18n, view);
  });

  protected readonly columns: readonly TableColumn<CashLedgerRow>[] = [
    { key: 'atUtc', header: 'billing.column.date', width: '150px', value: (row) => formatDateTime(row.atUtc, this.i18n.lang()) },
    { key: 'kind', header: 'billing.column.kind', width: '120px' },
    { key: 'number', header: 'billing.column.number', mono: true, width: '150px' },
    { key: 'detail', header: 'billing.ledger.detail', value: (row) => ledgerDetail(this.i18n, row) },
    { key: 'moneyIn', header: 'billing.ledger.in', align: 'end', value: (row) => (row.moneyIn === 0 ? '' : this.money(row.moneyIn)) },
    { key: 'moneyOut', header: 'billing.ledger.out', align: 'end', value: (row) => (row.moneyOut === 0 ? '' : this.money(row.moneyOut)) },
    { key: 'balance', header: 'billing.ledger.balance', align: 'end', value: (row) => this.money(row.balance) },
    { key: 'cashierName', header: 'billing.column.cashier', secondary: true, value: (row) => row.cashierName ?? '' },
  ];

  constructor() {
    void this.i18n.load('billing');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14, A-01)
    this.desk.refresh();
  }

  // ---- criteria -------------------------------------------------------------------------------------------------

  protected onFrom(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    const current = this.facade.request();
    const toDay = current.toDay !== null && value !== '' && current.toDay < value ? value : current.toDay;
    this.setRequest({ fromDay: value === '' ? null : value, toDay });
  }

  protected onTo(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.setRequest({ toDay: value === '' ? null : value });
  }

  protected onScope(event: Event): void {
    const scope = (event.target as HTMLSelectElement).value as CashLedgerScope;
    this.setRequest({ scope, cashierUserId: null });
  }

  protected onCashier(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.setRequest({ cashierUserId: value === '' ? null : value });
  }

  protected onPage(page: PageRequest): void {
    this.page.set(page);
  }

  protected reload(): void {
    this.facade.ledger.reload();
  }

  // ---- cash entries ---------------------------------------------------------------------------------------------

  protected openEntry(kind: CashEntryKind): void {
    this.facade.postEntry.reset();
    this.entryKind.set(kind);
    this.entryOpen.set(true);
  }

  protected async postEntry(request: PostCashEntryRequest): Promise<void> {
    const created = await this.facade.postEntry.execute(request);
    if (created === null) {
      return;
    }

    this.entryOpen.set(false);
    this.toast.success(this.i18n.tf('billing.ledger.entry.done', { number: created.number }));
  }

  protected async voidEntry(row: CashLedgerRow): Promise<void> {
    const reason = await this.confirm.promptReason({
      title: this.i18n.t('billing.ledger.void.title'),
      message: this.i18n.tf('billing.ledger.void.message', { number: row.number, amount: this.money(row.amount) }),
      confirmLabel: this.i18n.t('billing.void.action'),
      danger: true,
      maxLength: 500,
    });
    if (reason === null) {
      return;
    }

    if ((await this.facade.voidEntry.execute({ id: row.id, reason })) !== null) {
      this.toast.success(this.i18n.tf('billing.ledger.void.done', { number: row.number }));
    } else {
      this.toast.error(this.problemText(this.facade.voidEntry.problem()));
    }
  }

  protected canVoid(row: CashLedgerRow): boolean {
    return canVoidLine(row);
  }

  // ---- day close ------------------------------------------------------------------------------------------------

  protected openClose(view: CashLedgerView): void {
    if (this.facade.openClose(view)) {
      this.closeOpen.set(true);
    }
  }

  protected onCloseOpen(open: boolean): void {
    this.closeOpen.set(open);
    if (!open) {
      this.facade.cancelClose();
    }
  }

  protected async closeDay(input: DayCloseInput): Promise<void> {
    const preview = this.facade.currentClosePreview();
    if (preview === null) {
      return;
    }

    const created = await this.facade.close.execute({
      cashierUserId: preview.cashierUserId,
      periodFromUtc: preview.periodFromUtc,
      periodToUtc: preview.periodToUtc,
      countedCash: input.countedCash,
      note: input.note,
    });
    if (created === null) {
      return;
    }

    this.closeOpen.set(false);
    this.toast.success(this.i18n.tf('billing.closing.done', { number: created.number }));
    await this.run(() => this.printer.closingSheet(created.id));
  }

  protected closeCashierLabel(view: CashLedgerView): string {
    return view.cashierUserId === null ? this.i18n.t('billing.closing.allCashiers') : (view.cashierName ?? '');
  }

  // ---- rows -----------------------------------------------------------------------------------------------------

  protected printLine(row: CashLedgerRow): Promise<void> {
    switch (row.kind) {
      case 'Receipt':
        return this.run(() => this.printer.receipt(row.id));
      case 'Refund':
        return this.run(() => this.printer.refundReceipt(row.id));
      case 'Closing':
        return this.run(() => this.printer.closingSheet(row.id));
      default:
        return Promise.resolve();
    }
  }

  protected canPrint(row: CashLedgerRow): boolean {
    return row.kind === 'Receipt' || row.kind === 'Refund' || row.kind === 'Closing';
  }

  protected openVisit(row: CashLedgerRow): void {
    if (row.visitId !== null) {
      void this.router.navigate(['/billing/payments'], { queryParams: { visitId: row.visitId } });
    }
  }

  protected tone(row: CashLedgerRow): ReturnType<typeof ledgerTone> {
    return ledgerTone(row);
  }

  // ---- outputs --------------------------------------------------------------------------------------------------

  protected printSheet(view: CashLedgerView): Promise<void> {
    return this.run(() => this.printer.cashSheet(view));
  }

  protected async exportExcel(view: CashLedgerView): Promise<void> {
    try {
      const blob = await buildLedgerWorkbook(this.i18n, view);
      saveFile(this.document, { blob, fileName: ledgerFileName(view) });
      this.toast.success(this.i18n.t('billing.ledger.exported'));
    } catch {
      this.toast.error(this.i18n.t('billing.ledger.exportError'));
    }
  }

  protected money(value: number | null): string {
    return formatMoney(value, this.i18n.lang());
  }

  protected count(value: number): string {
    return formatCount(value, this.i18n.lang());
  }

  protected problemText(problem: { code: string } | null): string {
    return problemMessage(this.i18n, 'billing', problem);
  }

  private setRequest(change: Parameters<CashLedgerFacade['setRequest']>[0]): void {
    this.page.set({ page: 1, pageSize: this.page().pageSize });
    this.facade.setRequest(change);
  }

  private async run(print: () => Promise<void>): Promise<void> {
    try {
      await print();
    } catch {
      this.toast.error(this.i18n.t('billing.printError'));
    }
  }
}
