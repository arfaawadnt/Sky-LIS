import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import { ACCOUNT_PERMISSIONS } from '../../../core/api/account.contracts';
import type { PageRequest } from '../../../core/api/paging';
import { WALLET_TRANSACTION_KINDS, type WalletDetail, type WalletTransactionItem, type WalletTransactionKind } from '../../../core/api/platform.contracts';
import type { ProblemDetails } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { StatCardComponent } from '../../../shared/ui/stat-card/stat-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { formatDate, formatDateTime, formatMoney, pick } from '../data/account-format';
import { WalletFacade } from '../state/wallet.facade';

/**
 * The laboratory's wallet (SRS CP-66): prepaid balance, next charge, plan and SMS usage, pending top-up requests and
 * the movements; "Request top-up" (`wallet.create`) asks National Technology to credit the wallet. Metered services
 * pause at zero; the portal keeps working (shown as a warning when the balance is at or below zero).
 */
@Component({
  selector: 'app-wallet-page',
  imports: [
    ReactiveFormsModule,
    SaveShortcutDirective,
    CanDirective,
    PageHeaderComponent,
    SectionCardComponent,
    StatCardComponent,
    ToolbarComponent,
    DataTableComponent,
    CellTemplateDirective,
    PaginatorComponent,
    BadgeComponent,
    DialogComponent,
    FormFieldComponent,
    IconComponent,
  ],
  templateUrl: './wallet.page.html',
  styleUrl: './wallet.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WalletPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(WalletFacade);
  private readonly toast = inject(ToastService);

  protected readonly permissions = ACCOUNT_PERMISSIONS;
  protected readonly kinds = WALLET_TRANSACTION_KINDS;
  protected readonly topUpOpen = signal(false);

  protected readonly topUp = new FormGroup({
    amount: new FormControl<number | null>(null, { validators: [Validators.required, Validators.min(1), Validators.max(10_000_000)] }),
    note: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
  });

  protected readonly columns: readonly TableColumn<WalletTransactionItem>[] = [
    { key: 'atUtc', header: 'account.wallet.at', width: '170px', value: (row) => formatDateTime(row.atUtc, this.i18n.lang()) },
    { key: 'kind', header: 'account.wallet.kind', width: '130px' },
    { key: 'amount', header: 'account.wallet.amount', align: 'end', width: '140px', value: (row) => formatMoney(row.amount, this.currency(), this.i18n.lang()) },
    { key: 'balanceAfter', header: 'account.wallet.balanceAfter', align: 'end', width: '140px', secondary: true, value: (row) => formatMoney(row.balanceAfter, this.currency(), this.i18n.lang()) },
    { key: 'reference', header: 'account.wallet.reference', mono: true },
    { key: 'note', header: 'account.wallet.note', secondary: true },
    { key: 'byName', header: 'account.wallet.by', secondary: true },
  ];

  protected readonly wallet = computed<WalletDetail | null>(() => (this.facade.wallet.hasValue() ? this.facade.wallet.value() : null));

  protected readonly currency = computed(() => this.wallet()?.currencyCode ?? null);

  protected readonly rows = computed<readonly WalletTransactionItem[]>(() => (this.facade.transactions.hasValue() ? this.facade.transactions.value().items : []));

  protected readonly totalCount = computed(() => (this.facade.transactions.hasValue() ? this.facade.transactions.value().totalCount : 0));

  protected readonly smsLabel = computed(() => {
    const wallet = this.wallet();
    return wallet ? `${wallet.smsUsedThisMonth} / ${wallet.smsQuota}` : '—';
  });

  constructor() {
    void this.i18n.load('account');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14, A-01/C-13)
  }

  protected money(amount: number | null | undefined, currency: string | null | undefined = this.currency()): string {
    return formatMoney(amount, currency, this.i18n.lang());
  }

  protected day(value: string | null | undefined): string {
    return formatDate(value, this.i18n.lang());
  }

  protected when(value: string | null | undefined): string {
    return formatDateTime(value, this.i18n.lang());
  }

  protected planName(wallet: WalletDetail): string {
    return pick(wallet.planNameEn, wallet.planNameAr, this.i18n.lang());
  }

  protected kindLabel(kind: WalletTransactionKind): string {
    return this.i18n.t(`account.wallet.kindName.${kind}`);
  }

  protected onKind(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setKind(this.kinds.find((kind) => kind === value) ?? null);
  }

  protected onPage(page: PageRequest): void {
    this.facade.setPage(page);
  }

  protected openTopUp(): void {
    this.topUp.reset({ amount: null, note: '' });
    this.facade.requestTopUp.reset();
    this.topUpOpen.set(true);
  }

  protected async submitTopUp(): Promise<void> {
    if (!markAllTouched(this.topUp) || this.facade.requestTopUp.pending()) {
      return;
    }

    const { amount, note } = this.topUp.getRawValue();
    if (amount === null) {
      return;
    }

    if ((await this.facade.requestTopUp.execute({ amount, note: note.trim() === '' ? null : note.trim() })) !== null) {
      this.topUpOpen.set(false);
      this.toast.success(this.i18n.t('account.wallet.topUpRequested'));
    }
  }

  protected problemText(problem: ProblemDetails | null): string {
    return problemMessage(this.i18n, 'account', problem);
  }
}
