import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

import { BILLING_PERMISSIONS, PAYMENT_METHODS, PAYMENT_STATUSES, type PaymentListItem, type PaymentMethod, type PaymentStatus, type VisitAccountView } from '../../../core/api/billing.contracts';
import type { PageRequest } from '../../../core/api/paging';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
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
import { formatDateTime, formatMoney } from '../ui/billing-format';
import { dayEndIso, dayStartIso, paymentTone, problemOf } from '../ui/billing-view';
import { MovementFormComponent, type MovementInput } from '../ui/movement-form.component';
import { saveFile } from '../ui/save-file';

type PaymentsTab = 'collect' | 'register';

/**
 * Payments (SRS CP-23, BR-26, BR-27): find a visit by accession, see its account (total, discount, patient share, paid,
 * refunded, remaining), pay in full or in part on a treasury approved for this device, reprint or void receipts, print
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
  ],
  templateUrl: './payments.page.html',
  styleUrl: './billing-pages.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PaymentsPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(PaymentsFacade);
  protected readonly desk = inject(CashDeskFacade);
  private readonly printer = inject(BillingPrinter);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly document = inject(DOCUMENT);

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
    { key: 'treasury', header: 'billing.column.treasury', secondary: true, value: (row) => row.treasuryCode },
    { key: 'cashierName', header: 'billing.column.cashier', secondary: true },
    { key: 'status', header: 'billing.column.status', width: '110px' },
  ];

  protected readonly registerRows = computed<readonly PaymentListItem[]>(() => (this.facade.register.hasValue() ? this.facade.register.value().items : []));

  protected readonly registerTotal = computed(() => (this.facade.register.hasValue() ? this.facade.register.value().totalCount : 0));

  protected readonly branchTreasuries = computed(() => (this.facade.branchTreasuries.hasValue() ? this.facade.branchTreasuries.value() : []));

  protected readonly lookupProblem = computed(() => problemOf(this.facade.account.error()));

  constructor() {
    void this.i18n.load('billing');

    effect(() => {
      const visitId = this.visitId();
      const accession = this.accession();
      untracked(() => {
        if (visitId) {
          this.facade.open(visitId);
        } else if (accession) {
          this.search.setValue(accession);
          this.facade.find(accession);
        }
      });
    });

    effect(() => {
      const register = this.tab() === 'register';
      untracked(() => this.facade.registerOpen.set(register));
    });
  }

  protected find(): void {
    this.facade.find(this.search.value);
  }

  protected reload(): void {
    this.desk.treasuries.reload();
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
  }

  protected onTab(id: string | null): void {
    this.tab.set(id === 'register' ? 'register' : 'collect');
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

  protected onTreasury(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setFilter({ treasuryId: value === '' ? null : value });
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

  protected statusLabel(status: PaymentStatus): string {
    return this.i18n.t(`billing.status.${status}`);
  }

  protected tone(status: PaymentStatus): ReturnType<typeof paymentTone> {
    return paymentTone(status);
  }

  protected treasuryName(item: { nameEn: string; nameAr: string }): string {
    return localizedPick({ en: item.nameEn, ar: item.nameAr }, this.i18n.lang());
  }

  protected money(value: number): string {
    return formatMoney(value, this.i18n.lang());
  }

  protected problemText(problem: { code: string } | null): string {
    return problemMessage(this.i18n, 'billing', problem);
  }

  private async run(print: () => Promise<void>): Promise<void> {
    try {
      await print();
    } catch {
      this.toast.error(this.i18n.t('billing.printError'));
    }
  }
}
