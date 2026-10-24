import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, Injector, afterNextRender, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

import {
  BILLING_PERMISSIONS,
  type CollectionsReportRequest,
  PAYMENT_METHODS,
  PAYMENT_STATUSES,
  type PaymentListItem,
  type PaymentMethod,
  type RefundListItem,
  type PaymentStatus,
  type VisitAccountView,
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
import { EmptyStateComponent } from '../../../shared/ui/empty-state/empty-state.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { TabsComponent, type TabItem } from '../../../shared/ui/tabs/tabs.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { BillingPrinter } from '../state/billing-printer';
import { CashDeskFacade } from '../state/cash-desk.facade';
import { PaymentsFacade } from '../state/payments.facade';
import { AccountSummaryComponent } from '../ui/account-summary.component';
import { type MovementMethod, formatDateTime, formatMoney, movementMethodLabel } from '../ui/billing-format';
import { dayEndIso, dayStartIso, paymentTone, problemOf } from '../ui/billing-view';
import { CollectionsReportDialogComponent } from '../ui/collections-report-dialog.component';
import { MovementFormComponent, type MovementInput } from '../ui/movement-form.component';
import { saveFile } from '../ui/save-file';

type PaymentsTab = 'collect' | 'register';

/** How many of the visit's payments and refunds show before "Show all" (LIS-CR-007 C-07). */
export const LATEST_MOVEMENTS = 3;

/**
 * Payments (SRS CP-23, BR-26; LIS-CR-004): find a visit by accession, see its account (total, discount, patient share, paid,
 * refunded, remaining), pay in full or in part as the signed-in cashier on the session branch, reprint or void receipts, print
 * the invoice, jump to the refund when the balance is negative; the "Register" tab lists every payment with filters
 * and CSV export. Receipts print after a payment when `autoPrintReceipt` is on.
 */
@Component({
  selector: 'app-billing-payments-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    CanDirective,
    PageHeaderComponent,
    SectionCardComponent,
    TabsComponent,
    ToolbarComponent,
    DataTableComponent,
    CellTemplateDirective,
    RowActionsTemplateDirective,
    PaginatorComponent,
    BadgeComponent,
    EmptyStateComponent,
    FormFieldComponent,
    IconComponent,
    AccountSummaryComponent,
    MovementFormComponent,
    CollectionsReportDialogComponent,
  ],
  templateUrl: './payments.page.html',
  styleUrl: './billing-pages.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PaymentsPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(PaymentsFacade);
  protected readonly desk = inject(CashDeskFacade);
  private readonly form = viewChild(MovementFormComponent);
  private readonly printer = inject(BillingPrinter);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);

  /** Set by a find (or an opened link): the next account to load puts the cursor in the amount (scan → amount → F10). */
  private focusAmountPending = false;

  /** Query parameters (router input binding): a link from Registration opens the visit (BR-26). */
  readonly visitId = input<string>();
  readonly accession = input<string>();

  protected readonly permissions = BILLING_PERMISSIONS;
  protected readonly methods = PAYMENT_METHODS;
  protected readonly statuses = PAYMENT_STATUSES;

  protected readonly tab = signal<PaymentsTab>('collect');

  protected readonly tabs = computed<readonly TabItem[]>(() => [
    { id: 'collect', label: this.i18n.t('billing.payments.tabCollect') },
    { id: 'register', label: this.i18n.t('billing.payments.tabRegister') },
  ]);

  protected readonly search = new FormControl('', { nonNullable: true });

  protected readonly visitColumns: readonly TableColumn<PaymentListItem>[] = [
    { key: 'receiptNumber', header: 'billing.column.receipt', mono: true, width: '150px' },
    { key: 'paidAtUtc', header: 'billing.column.date', value: (row) => formatDateTime(row.paidAtUtc, this.i18n.lang()) },
    { key: 'amount', header: 'billing.column.amount', align: 'end', value: (row) => formatMoney(row.amount, this.i18n.lang()) },
    { key: 'method', header: 'billing.column.method' },
    { key: 'reference', header: 'billing.column.reference', mono: true, secondary: true },
    { key: 'cashierName', header: 'billing.column.cashier', secondary: true },
    { key: 'status', header: 'billing.column.status', width: '110px' },
  ];

  protected readonly registerColumns: readonly TableColumn<PaymentListItem>[] = [
    { key: 'receiptNumber', header: 'billing.column.receipt', mono: true, width: '150px' },
    { key: 'paidAtUtc', header: 'billing.column.date', value: (row) => formatDateTime(row.paidAtUtc, this.i18n.lang()) },
    { key: 'accession', header: 'billing.column.accession', mono: true },
    { key: 'amount', header: 'billing.column.amount', align: 'end', value: (row) => formatMoney(row.amount, this.i18n.lang()) },
    { key: 'method', header: 'billing.column.method' },
    { key: 'cashierName', header: 'billing.column.cashier', secondary: true },
    { key: 'status', header: 'billing.column.status', width: '110px' },
  ];

  protected readonly registerRows = computed<readonly PaymentListItem[]>(() => (this.facade.register.hasValue() ? this.facade.register.value().items : []));

  protected readonly registerTotal = computed(() => (this.facade.register.hasValue() ? this.facade.register.value().totalCount : 0));

  /** The visit's history collapsed to the latest payments and refunds unless the cashier asked for all of them. */
  protected readonly showAllMovements = signal(false);

  protected readonly shownPayments = computed<readonly PaymentListItem[]>(() => this.latest(this.facade.current()?.payments ?? []));

  protected readonly shownRefunds = computed<readonly RefundListItem[]>(() => this.latest(this.facade.current()?.refunds ?? []));

  protected readonly collapsible = computed(() => {
    const account = this.facade.current();
    return account !== null && (account.payments.length > LATEST_MOVEMENTS || account.refunds.length > LATEST_MOVEMENTS);
  });

  protected readonly lookupProblem = computed(() => problemOf(this.facade.account.error()));

  /** The member's report rights once the collections dialog has read them (null while loading: the branch option stays hidden). */
  protected readonly reportPrivileges = computed(() => (this.facade.reportPrivileges.hasValue() ? this.facade.reportPrivileges.value() : null));

  constructor() {
    void this.i18n.load('billing');
    // UAT D-08: the cash desk is shared by the module's pages for the life of the route; re-read the print parameter and the
    // payment methods each time a page opens so a parameter or a role change since the last read shows at once.
    this.desk.refresh();
    this.facade.refresh();   // the page's facade outlives the page too: re-read on open (UAT D-14, A-01)

    effect(() => {
      const visitId = this.visitId();
      const accession = this.accession();
      untracked(() => {
        if (visitId) {
          this.expectNewVisit();
          this.facade.open(visitId);
        } else if (accession) {
          this.search.setValue(accession);
          this.expectNewVisit();
          this.facade.find(accession);
        }
      });
    });

    // LIS-CR-007 C-07: once the found visit's account is on screen, the amount field takes the focus.
    effect(() => {
      if (this.facade.current() === null || !this.focusAmountPending) {
        return;
      }

      this.focusAmountPending = false;
      afterNextRender(() => this.form()?.focusAmount(), { injector: this.injector });
    });

    effect(() => {
      const register = this.tab() === 'register';
      untracked(() => this.facade.registerOpen.set(register));
    });
  }

  protected find(): void {
    if (this.search.value.trim() !== '') {
      this.expectNewVisit();
    }

    this.facade.find(this.search.value);
  }

  protected toggleMovements(): void {
    this.showAllMovements.update((all) => !all);
  }

  protected reload(): void {
    this.desk.refresh();
    if (this.facade.lookup() !== null) {
      this.facade.account.reload();
    }

    if (this.tab() === 'register') {
      this.facade.register.reload();
    }
  }

  protected async pay(account: VisitAccountView, input: MovementInput): Promise<void> {
    const created = await this.facade.pay.execute({ ...input, visitId: account.visitId });
    if (created === null) {
      return;
    }

    this.toast.success(this.i18n.tf('billing.pay.done', { number: created.receiptNumber }));
    this.form()?.reset();   // UAT A-06: the next payment starts from a fresh form
    if (this.desk.shouldAutoPrint()) {
      await this.run(() => this.printer.receipt(created.id));
    }
  }

  protected async voidPayment(row: PaymentListItem): Promise<void> {
    const reason = await this.confirm.promptReason({
      title: this.i18n.t('billing.void.title'),
      message: this.i18n.tf('billing.void.message', { number: row.receiptNumber, amount: formatMoney(row.amount, this.i18n.lang()) }),
      danger: true,
      maxLength: 500,
    });
    if (reason === null) {
      return;
    }

    if ((await this.facade.voidPayment.execute({ id: row.id, reason })) !== null) {
      this.toast.success(this.i18n.tf('billing.void.done', { number: row.receiptNumber }));
    } else {
      this.toast.error(this.problemText(this.facade.voidPayment.problem()));
    }
  }

  protected canVoid(row: PaymentListItem): boolean {
    return row.status === 'Posted' && !row.isFrozen;
  }

  protected reprint(row: PaymentListItem): Promise<void> {
    return this.run(() => this.printer.receipt(row.id));
  }

  protected printInvoice(account: VisitAccountView): Promise<void> {
    return this.run(() => this.printer.invoice(account.visitId));
  }

  protected openFromRegister(row: PaymentListItem): void {
    this.expectNewVisit();
    this.facade.open(row.visitId);
    this.search.setValue(row.accession ?? '');
    this.tab.set('collect');
  }

  protected async exportCsv(): Promise<void> {
    const file = await this.facade.exportCsv.execute();
    if (file === null) {
      this.toast.error(this.problemText(this.facade.exportCsv.problem()));
      return;
    }

    saveFile(this.document, file);
    this.toast.success(this.i18n.t('billing.exported'));
  }

  protected onTab(id: string | null): void {
    this.tab.set(id === 'register' ? 'register' : 'collect');
  }

  // ---- collections report, change request 2026-10-13 item 2 ---------------------------------------------

  protected openCollections(): void {
    this.facade.openCollections();
  }

  protected onCollectionsOpen(open: boolean): void {
    if (!open) {
      this.facade.closeCollections();
    }
  }

  protected loadCollections(request: CollectionsReportRequest): Promise<unknown> {
    return this.facade.collectionsReport.execute(request);
  }

  /** Loads the report for the chosen period, scope and grouping (so the paper matches the form), then prints it. */
  protected async printCollections(request: CollectionsReportRequest): Promise<void> {
    const doc = await this.facade.collectionsReport.execute(request);
    if (doc !== null) {
      await this.run(() => this.printer.collectionsReport(doc));
    }
  }

  protected onAccessionFilter(value: string): void {
    this.facade.setFilter({ accession: value.trim() === '' ? null : value.trim() });
  }

  protected onFrom(event: Event): void {
    this.facade.setFilter({ fromUtc: dayStartIso((event.target as HTMLInputElement).value) });
  }

  protected onTo(event: Event): void {
    this.facade.setFilter({ toUtc: dayEndIso((event.target as HTMLInputElement).value) });
  }

  protected onMethod(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setFilter({ method: this.methods.find((method) => method === value) ?? null });
  }

  protected onStatus(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setFilter({ status: this.statuses.find((status) => status === value) ?? null });
  }

  protected onPage(page: PageRequest): void {
    this.facade.setPage(page);
  }

  protected methodLabel(method: PaymentMethod): string {
    return this.i18n.t(`billing.method.${method}`);
  }

  /** The laboratory's method name with its code (CP-30b), or the kind on a legacy row. */
  protected movementMethod(movement: MovementMethod): string {
    return movementMethodLabel((key) => this.i18n.t(key), this.i18n.lang(), movement);
  }

  protected statusLabel(status: PaymentStatus): string {
    return this.i18n.t(`billing.status.${status}`);
  }

  protected tone(status: PaymentStatus): ReturnType<typeof paymentTone> {
    return paymentTone(status);
  }

  protected money(value: number): string {
    return formatMoney(value, this.i18n.lang());
  }

  protected problemText(problem: { code: string } | null): string {
    return problemMessage(this.i18n, 'billing', problem);
  }

  /** A visit is being looked up: its history starts collapsed and its amount field will take the focus. */
  private expectNewVisit(): void {
    this.showAllMovements.set(false);
    this.focusAmountPending = true;
  }

  private latest<T>(rows: readonly T[]): readonly T[] {
    return this.showAllMovements() || rows.length <= LATEST_MOVEMENTS ? rows : rows.slice(-LATEST_MOVEMENTS);
  }

  private async run(print: () => Promise<void>): Promise<void> {
    try {
      await print();
    } catch {
      this.toast.error(this.i18n.t('billing.printError'));
    }
  }
}
