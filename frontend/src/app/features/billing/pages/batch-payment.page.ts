import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked, viewChildren } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

import { BILLING_PERMISSIONS, type BalanceStatus, type BatchAccountView, type BatchMemberView } from '../../../core/api/billing.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { problemMessage } from '../../../shared/forms/problem-text';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { EmptyStateComponent } from '../../../shared/ui/empty-state/empty-state.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { StatCardComponent } from '../../../shared/ui/stat-card/stat-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { BatchPaymentFacade } from '../state/batch-payment.facade';
import { BillingPrinter } from '../state/billing-printer';
import { CashDeskFacade } from '../state/cash-desk.facade';
import { BALANCE_TONES } from '../ui/account-summary.component';
import { formatCount, formatMoney, roundMoney } from '../ui/billing-format';
import { problemOf } from '../ui/billing-view';
import { MovementFormComponent, type MovementInput } from '../ui/movement-form.component';

/**
 * Batch payment (SRS CP-27; LDM `Batch Payment` §4.8): look a batch up by number, see every member with shares, paid and
 * remaining, pay one member, or pay all — the amount is distributed in accession order up to each member's remaining
 * balance (T4). Lab-to-lab batches are billed to the sending laboratory through claims and take no payment here.
 */
@Component({
  selector: 'app-billing-batch-payment-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    CanDirective,
    PageHeaderComponent,
    SectionCardComponent,
    DataTableComponent,
    CellTemplateDirective,
    RowActionsTemplateDirective,
    BadgeComponent,
    DialogComponent,
    EmptyStateComponent,
    FormFieldComponent,
    IconComponent,
    StatCardComponent,
    MovementFormComponent,
  ],
  templateUrl: './batch-payment.page.html',
  styleUrl: './billing-pages.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BatchPaymentPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(BatchPaymentFacade);
  protected readonly desk = inject(CashDeskFacade);
  private readonly forms = viewChildren(MovementFormComponent);
  private readonly printer = inject(BillingPrinter);
  private readonly toast = inject(ToastService);

  /** `?number=BTH-…` opens a batch directly (the batch registration page links here). */
  readonly number = input<string>();

  protected readonly permissions = BILLING_PERMISSIONS;
  protected readonly search = new FormControl('', { nonNullable: true });

  /** The member whose payment dialog is open. */
  protected readonly member = signal<BatchMemberView | null>(null);
  protected readonly memberDialogOpen = signal(false);

  protected readonly columns: readonly TableColumn<BatchMemberView>[] = [
    { key: 'accession', header: 'billing.column.accession', mono: true, width: '150px' },
    { key: 'patient', header: 'billing.column.patient', value: (row) => row.patientName ?? row.patientNumber ?? '—' },
    { key: 'totalListPrice', header: 'billing.column.listPrice', align: 'end', secondary: true, value: (row) => this.money(row.totalListPrice) },
    { key: 'discountTotal', header: 'billing.column.discount', align: 'end', secondary: true, value: (row) => this.money(row.discountTotal) },
    { key: 'patientShareTotal', header: 'billing.column.patientShare', align: 'end', value: (row) => this.money(row.patientShareTotal) },
    { key: 'payerShareTotal', header: 'billing.column.payerShare', align: 'end', secondary: true, value: (row) => this.money(row.payerShareTotal) },
    { key: 'paid', header: 'billing.column.paid', align: 'end', value: (row) => this.money(row.paid) },
    { key: 'remaining', header: 'billing.column.remaining', align: 'end', value: (row) => this.money(row.remaining) },
    { key: 'balanceStatus', header: 'billing.column.status', width: '120px' },
  ];

  protected readonly members = computed<readonly BatchMemberView[]>(() => this.facade.current()?.members ?? []);

  protected readonly memberId = (row: BatchMemberView): string => row.visitId;

  /** What pay-all may distribute: the positive remaining balances only (a member due a refund is skipped). */
  protected readonly payable = computed(() => roundMoney(this.members().reduce((sum, member) => sum + Math.max(member.remaining, 0), 0)));

  protected readonly lookupProblem = computed(() => problemOf(this.facade.batch.error()));

  constructor() {
    void this.i18n.load('billing');
    // UAT D-08: the cash desk is shared by the module's pages for the life of the route; re-read the approved treasuries
    // (and the refund reasons) each time a page opens so an approval or a role change since the last read shows at once.
    this.desk.refresh();
    this.facade.refresh();   // the page's facade outlives the page too: re-read on open (UAT D-14, A-01)

    effect(() => {
      const number = this.number();
      untracked(() => {
        if (number) {
          this.search.setValue(number);
          this.facade.find(number);
        }
      });
    });
  }

  protected find(): void {
    this.facade.find(this.search.value);
  }

  protected reload(): void {
    this.desk.treasuries.reload();
    if (this.facade.number() !== null) {
      this.facade.batch.reload();
    }
  }

  protected openMember(member: BatchMemberView): void {
    this.facade.payMember.reset();
    this.member.set(member);
    this.memberDialogOpen.set(true);
  }

  protected async payMember(input: MovementInput): Promise<void> {
    const member = this.member();
    if (member === null) {
      return;
    }

    const created = await this.facade.payMember.execute({ ...input, visitId: member.visitId });
    if (created === null) {
      return;
    }

    this.memberDialogOpen.set(false);
    this.toast.success(this.i18n.tf('billing.pay.done', { number: created.receiptNumber }));
    this.resetForms();   // UAT A-06
    if (this.desk.shouldAutoPrint()) {
      await this.run(() => this.printer.receipt(created.id));
    }
  }

  protected async payAll(batch: BatchAccountView, input: MovementInput): Promise<void> {
    const result = await this.facade.payAll.execute({ ...input, batchId: batch.batchId });
    if (result === null) {
      return;
    }

    this.toast.success(this.i18n.tc('billing.batch.paidAll', result.payments.length, { count: this.num(result.payments.length), amount: this.money(result.amount) }));
    this.resetForms();   // UAT A-06
    if (this.desk.shouldAutoPrint()) {
      await this.run(() => this.printer.receipts(result.payments.map((payment) => payment.paymentId)));
    }
  }

  /** Every money form of the page starts fresh after a saved movement (UAT A-06). */
  private resetForms(): void {
    for (const form of this.forms()) {
      form.reset();
    }
  }

  protected canPay(batch: BatchAccountView, member: BatchMemberView): boolean {
    return !batch.billedThroughClaims && member.remaining > 0;
  }

  protected payer(batch: BatchAccountView): string {
    const name = localizedPick({ en: batch.payerNameEn ?? '', ar: batch.payerNameAr ?? '' }, this.i18n.lang());
    return name === '' ? '—' : name;
  }

  protected statusLabel(status: BalanceStatus): string {
    return this.i18n.t(`billing.balance.${status}`);
  }

  protected tone(status: BalanceStatus): (typeof BALANCE_TONES)[BalanceStatus] {
    return BALANCE_TONES[status];
  }

  protected money(value: number): string {
    return formatMoney(value, this.i18n.lang());
  }

  /** A count in the page language's digits (validation v0.5.0 F-09). */
  protected num(value: number): string {
    return formatCount(value, this.i18n.lang());
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
