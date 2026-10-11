import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, untracked, viewChild } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

import { BILLING_PERMISSIONS, PAYMENT_METHODS, type PaymentMethod, type RefundListItem, type VisitAccountView } from '../../../core/api/billing.contracts';
import type { PageRequest } from '../../../core/api/paging';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { problemMessage } from '../../../shared/forms/problem-text';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { EmptyStateComponent } from '../../../shared/ui/empty-state/empty-state.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { BillingPrinter } from '../state/billing-printer';
import { CashDeskFacade } from '../state/cash-desk.facade';
import { RefundsFacade } from '../state/refunds.facade';
import { AccountSummaryComponent } from '../ui/account-summary.component';
import { formatDateTime, formatMoney } from '../ui/billing-format';
import { dayEndIso, dayStartIso, problemOf } from '../ui/billing-view';
import { MovementFormComponent, type MovementInput } from '../ui/movement-form.component';
import { saveFile } from '../ui/save-file';

/**
 * Refunds (SRS CP-24, BR-14; LDM `Return Money` §10.8): find a visit, see the refundable amount (a negative balance after a
 * transfer or a cancellation), refund up to it with a catalogue reason on an approved treasury of this device, and print the
 * refund receipt for the patient's signature (always). The register below lists every refund with filters and CSV export.
 */
@Component({
  selector: 'app-billing-refunds-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    CanDirective,
    PageHeaderComponent,
    SectionCardComponent,
    ToolbarComponent,
    DataTableComponent,
    CellTemplateDirective,
    RowActionsTemplateDirective,
    PaginatorComponent,
    EmptyStateComponent,
    FormFieldComponent,
    IconComponent,
    AccountSummaryComponent,
    MovementFormComponent,
  ],
  templateUrl: './refunds.page.html',
  styleUrl: './billing-pages.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RefundsPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(RefundsFacade);
  protected readonly desk = inject(CashDeskFacade);
  private readonly form = viewChild(MovementFormComponent);
  private readonly printer = inject(BillingPrinter);
  private readonly toast = inject(ToastService);
  private readonly document = inject(DOCUMENT);

  /** `?visitId=…` from the Payments page's refund shortcut. */
  readonly visitId = input<string>();

  protected readonly permissions = BILLING_PERMISSIONS;
  protected readonly methods = PAYMENT_METHODS;

  protected readonly search = new FormControl('', { nonNullable: true });

  protected readonly columns: readonly TableColumn<RefundListItem>[] = [
    { key: 'number', header: 'billing.column.number', mono: true, width: '150px' },
    { key: 'refundedAtUtc', header: 'billing.column.date', value: (row) => formatDateTime(row.refundedAtUtc, this.i18n.lang()) },
    { key: 'accession', header: 'billing.column.accession', mono: true },
    { key: 'amount', header: 'billing.column.amount', align: 'end', value: (row) => formatMoney(row.amount, this.i18n.lang()) },
    { key: 'method', header: 'billing.column.method' },
    { key: 'reason', header: 'billing.column.reason', value: (row) => this.reasonName(row) },
    { key: 'cashierName', header: 'billing.column.cashier', secondary: true },
    { key: 'treasury', header: 'billing.column.treasury', secondary: true, value: (row) => row.treasuryCode },
  ];

  protected readonly rows = computed<readonly RefundListItem[]>(() => (this.facade.register.hasValue() ? this.facade.register.value().items : []));

  protected readonly total = computed(() => (this.facade.register.hasValue() ? this.facade.register.value().totalCount : 0));

  protected readonly reasons = computed(() => (this.facade.reasons.hasValue() ? this.facade.reasons.value() : []));

  protected readonly lookupProblem = computed(() => problemOf(this.facade.account.error()));

  constructor() {
    void this.i18n.load('billing');
    // UAT D-08: the cash desk is shared by the module's pages for the life of the route; re-read the approved treasuries
    // (and the refund reasons) each time a page opens so an approval or a role change since the last read shows at once.
    this.desk.refresh();
    this.facade.refreshReasons();
    this.facade.refresh();   // the page's facade outlives the page too: re-read on open (UAT D-14, A-01)

    effect(() => {
      const visitId = this.visitId();
      untracked(() => {
        if (visitId) {
          this.facade.open(visitId);
        }
      });
    });
  }

  protected find(): void {
    this.facade.find(this.search.value);
  }

  protected reload(): void {
    this.desk.treasuries.reload();
    this.facade.reasons.reload();
    this.facade.register.reload();
    if (this.facade.lookup() !== null) {
      this.facade.account.reload();
    }
  }

  protected async refund(account: VisitAccountView, input: MovementInput): Promise<void> {
    if (input.reasonId === null) {
      return;
    }

    const created = await this.facade.refund.execute({ ...input, reasonId: input.reasonId, visitId: account.visitId });
    if (created === null) {
      return;
    }

    this.toast.success(this.i18n.tf('billing.refund.done', { number: created.number }));
    this.form()?.reset();   // UAT A-06: the next refund starts from a fresh form
    // CP-24 "print receipt for signature": the refund receipt always prints, whatever autoPrintReceipt says.
    await this.run(() => this.printer.refundReceipt(created.id));
  }

  protected reprint(row: RefundListItem): Promise<void> {
    return this.run(() => this.printer.refundReceipt(row.id));
  }

  protected async exportCsv(): Promise<void> {
    const file = await this.facade.exportCsv.execute();
    if (file === null) {
      this.toast.error(problemMessage(this.i18n, 'billing', this.facade.exportCsv.problem()));
      return;
    }

    saveFile(this.document, file);
    this.toast.success(this.i18n.t('billing.exported'));
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

  protected onPage(page: PageRequest): void {
    this.facade.setPage(page);
  }

  protected methodLabel(method: PaymentMethod): string {
    return this.i18n.t(`billing.method.${method}`);
  }

  protected reasonName(row: RefundListItem): string {
    const name = localizedPick({ en: row.reasonNameEn ?? '', ar: row.reasonNameAr ?? '' }, this.i18n.lang());
    return name === '' ? '—' : name;
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
