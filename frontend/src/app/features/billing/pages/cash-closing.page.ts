import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import { BILLING_PERMISSIONS, type CashClosingListItem, type CashClosingPreview, type DrawerMovement, type PaymentMethod } from '../../../core/api/billing.contracts';
import type { PageRequest } from '../../../core/api/paging';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
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
import { StatCardComponent } from '../../../shared/ui/stat-card/stat-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { BillingPrinter } from '../state/billing-printer';
import { CashClosingFacade } from '../state/cash-closing.facade';
import { formatDateTime, formatCount, formatMoney, hasTwoDecimals, localInputToIso, roundMoney, startOfDay, toLocalInputValue } from '../ui/billing-format';
import { dayEndIso, dayStartIso, paymentTone, problemOf } from '../ui/billing-view';
import { saveFile } from '../ui/save-file';

/**
 * Cash closing (SRS CP-30, BR-16, WF-09; LIS-CR-004): on the session branch, choose one cashier or all and a period;
 * preview the drawer (payments and refunds per method, expected cash = cash payments − cash refunds, outstanding balances,
 * discounts, movements); count the cash, explain a difference, close with confirmation and print the handover sheet.
 * The register lists the closings with their sheets and a CSV export.
 */
@Component({
  selector: 'app-billing-cash-closing-page',
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
    StatCardComponent,
  ],
  templateUrl: './cash-closing.page.html',
  styleUrl: './billing-pages.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CashClosingPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(CashClosingFacade);
  private readonly printer = inject(BillingPrinter);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly document = inject(DOCUMENT);
  private readonly can = inject(PermissionsService);

  protected readonly permissions = BILLING_PERMISSIONS;

  protected readonly scopeForm = new FormGroup({
    cashierUserId: new FormControl('', { nonNullable: true }),
    from: new FormControl(toLocalInputValue(startOfDay(new Date())), { nonNullable: true, validators: [Validators.required] }),
    to: new FormControl(toLocalInputValue(new Date()), { nonNullable: true, validators: [Validators.required] }),
  });

  protected readonly countForm = new FormGroup({
    countedCash: new FormControl<number | null>(null, { validators: [Validators.min(0)] }),
    note: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(1000)] }),
  });

  /** The count as a signal (valueChanges through toSignal, never a subscribe in the component). */
  private readonly counted = toSignal(this.countForm.controls.countedCash.valueChanges, { initialValue: null });

  protected readonly cashierList = computed(() => (this.facade.cashiers.hasValue() ? this.facade.cashiers.value() : []));

  protected readonly difference = computed<number | null>(() => {
    const preview = this.facade.currentPreview();
    const counted = this.counted();
    return preview === null || counted === null || counted === undefined ? null : roundMoney(counted - preview.totals.expectedCash);
  });

  protected readonly nonCash = computed(() => {
    const totals = this.facade.currentPreview()?.totals;
    return totals === undefined ? 0 : roundMoney(totals.cardPayments + totals.posPayments + totals.transferPayments);
  });

  protected readonly previewProblem = computed(() => problemOf(this.facade.preview.error()));

  protected readonly movementColumns: readonly TableColumn<DrawerMovement>[] = [
    { key: 'kind', header: 'billing.column.kind', width: '110px' },
    { key: 'number', header: 'billing.column.number', mono: true },
    { key: 'atUtc', header: 'billing.column.date', value: (row) => formatDateTime(row.atUtc, this.i18n.lang()) },
    { key: 'accession', header: 'billing.column.accession', mono: true, secondary: true },
    { key: 'method', header: 'billing.column.method' },
    { key: 'amount', header: 'billing.column.amount', align: 'end', value: (row) => `${row.kind === 'Refund' || row.kind === 'CashOut' ? '−' : ''}${this.money(row.amount)}` },
    { key: 'cashierName', header: 'billing.column.cashier', secondary: true },
    { key: 'status', header: 'billing.column.status', width: '110px' },
  ];

  protected readonly closingColumns: readonly TableColumn<CashClosingListItem>[] = [
    { key: 'number', header: 'billing.column.number', mono: true, width: '150px' },
    { key: 'cashierName', header: 'billing.column.cashier', value: (row) => row.cashierName ?? this.i18n.t('billing.closing.allCashiers') },
    { key: 'period', header: 'billing.column.period', secondary: true, value: (row) => `${formatDateTime(row.periodFromUtc, this.i18n.lang())} → ${formatDateTime(row.periodToUtc, this.i18n.lang())}` },
    { key: 'expectedCash', header: 'billing.column.expectedCash', align: 'end', value: (row) => this.money(row.expectedCash) },
    { key: 'countedCash', header: 'billing.column.countedCash', align: 'end', value: (row) => this.money(row.countedCash) },
    { key: 'difference', header: 'billing.column.difference', align: 'end', value: (row) => this.money(row.difference) },
    { key: 'closedByName', header: 'billing.column.closedBy', secondary: true, value: (row) => `${row.closedByName} · ${formatDateTime(row.closedAtUtc, this.i18n.lang())}` },
  ];

  protected readonly closings = computed<readonly CashClosingListItem[]>(() => (this.facade.closings.hasValue() ? this.facade.closings.value().items : []));

  protected readonly closingsTotal = computed(() => (this.facade.closings.hasValue() ? this.facade.closings.value().totalCount : 0));

  constructor() {
    void this.i18n.load('billing');
    this.facade.refresh();   // the page's facade outlives the page too: re-read on open (UAT D-14, A-01)
    if (this.can.can(this.permissions.close)) {
      this.loadCashiers();   // the cashiers of the session branch over the default period (today so far)
    }
  }

  protected loadCashiers(): void {
    const { from, to } = this.scopeForm.getRawValue();
    const fromUtc = localInputToIso(from);
    const toUtc = localInputToIso(to);
    if (fromUtc !== null && toUtc !== null && fromUtc < toUtc) {
      this.facade.cashierQuery.set({ fromUtc, toUtc });
    }
  }

  protected preview(): void {
    if (!markAllTouched(this.scopeForm)) {
      return;
    }

    const { cashierUserId, from, to } = this.scopeForm.getRawValue();
    const fromUtc = localInputToIso(from);
    const toUtc = localInputToIso(to);
    if (fromUtc === null || toUtc === null) {
      return;
    }

    this.facade.close.reset();
    this.countForm.reset({ countedCash: null, note: '' });
    this.loadCashiers();
    this.facade.scope.set({ cashierUserId: cashierUserId === '' ? null : cashierUserId, fromUtc, toUtc });
  }

  private readonly noteText = toSignal(this.countForm.controls.note.valueChanges, { initialValue: '' });

  /** BIL-034: a non-zero difference needs a note (checked here so the cashier sees it before confirming). */
  protected readonly noteMissing = computed(() => {
    const difference = this.difference();
    return difference !== null && difference !== 0 && this.noteText().trim() === '';
  });

  protected canClose(preview: CashClosingPreview): boolean {
    return !preview.overlapsClosedPeriod && !this.facade.close.pending();
  }

  protected async close(preview: CashClosingPreview): Promise<void> {
    const counted = this.countForm.controls.countedCash.value;
    if (!markAllTouched(this.countForm) || this.noteMissing() || (counted !== null && !hasTwoDecimals(counted)) || !this.canClose(preview)) {
      return;
    }

    const confirmed = await this.confirm.confirm({
      title: this.i18n.t('billing.closing.confirmTitle'),
      message: this.i18n.tf('billing.closing.confirmMessage', {
        cashier: this.cashierOf(preview),
        expected: this.money(preview.totals.expectedCash),
        counted: this.money(counted),
      }),
      confirmLabel: this.i18n.t('billing.closing.close'),
    });
    if (!confirmed) {
      return;
    }

    const note = this.countForm.controls.note.value.trim();
    const created = await this.facade.close.execute({
      cashierUserId: preview.cashierUserId,
      periodFromUtc: preview.periodFromUtc,
      periodToUtc: preview.periodToUtc,
      countedCash: counted,
      note: note === '' ? null : note,
    });
    if (created === null) {
      return;
    }

    this.toast.success(this.i18n.tf('billing.closing.done', { number: created.number }));
    await this.run(() => this.printer.closingSheet(created.id));
  }

  protected printSheet(row: CashClosingListItem): Promise<void> {
    return this.run(() => this.printer.closingSheet(row.id));
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

  protected onRegisterFrom(event: Event): void {
    this.facade.setFilter({ fromUtc: dayStartIso((event.target as HTMLInputElement).value) });
  }

  protected onRegisterTo(event: Event): void {
    this.facade.setFilter({ toUtc: dayEndIso((event.target as HTMLInputElement).value) });
  }

  protected onPage(page: PageRequest): void {
    this.facade.setPage(page);
  }

  protected reload(): void {
    this.facade.closings.reload();
    this.facade.cashiers.reload();
    if (this.facade.scope() !== null) {
      this.facade.preview.reload();
    }
  }

  /** The cashier of the drawer, or "All cashiers" for the whole session branch. */
  protected cashierOf(preview: CashClosingPreview): string {
    return preview.cashierName ?? this.i18n.t('billing.closing.allCashiers');
  }

  protected methodLabel(method: PaymentMethod): string {
    return this.i18n.t(`billing.method.${method}`);
  }

  protected tone(movement: DrawerMovement): ReturnType<typeof paymentTone> {
    return paymentTone(movement.status);
  }

  protected money(value: number | null): string {
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
