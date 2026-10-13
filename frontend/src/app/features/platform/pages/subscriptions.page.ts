import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';

import type { PageRequest } from '../../../core/api/paging';
import {
  PLATFORM_PERMISSIONS,
  type PlanListItem,
  type TopUpRequestItem,
  WALLET_TRANSACTION_KINDS,
  type WalletTransactionItem,
  type WalletDetail,
  type WalletTransactionKind,
} from '../../../core/api/platform.contracts';
import { type ProblemDetails, ProblemError } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedTextGroup } from '../../../shared/forms/localized-text';
import { blockedSave } from '../../../shared/forms/blocked-save';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { problemToFormErrors } from '../../../shared/forms/problem-to-form-errors';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { UppercaseDirective } from '../../../shared/forms/uppercase.directive';
import { codeValidator, requiredText } from '../../../shared/forms/validators';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { DrawerComponent } from '../../../shared/ui/drawer/drawer.component';
import { EmptyStateComponent } from '../../../shared/ui/empty-state/empty-state.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { LocalizedTextInputComponent } from '../../../shared/ui/localized-text-input/localized-text-input.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { StatCardComponent } from '../../../shared/ui/stat-card/stat-card.component';
import { type TabItem, TabsComponent } from '../../../shared/ui/tabs/tabs.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { CsvExportService } from '../data/csv-export';
import { WalletsFacade } from '../state/wallets.facade';
import { fileStamp, formatDate, formatDateTime, formatMoney, formatNumber, pick } from '../ui/format';
import { statusTone } from '../ui/status';
import { maxTwoDecimals } from '../ui/validators';

type PlanForm = ReturnType<SubscriptionsPage['buildPlanForm']>;

/**
 * SA-05 Subscriptions & wallets: the plans register (fee, branches, users, SMS quota, analysers) and the wallet of
 * a client (balance, next charge, SMS used, pending top-up requests of the laboratory, transactions) with the top-up
 * dialog. `?client=<id>` opens a client's wallet directly (SA-04 "open wallet").
 */
@Component({
  selector: 'app-platform-subscriptions-page',
  imports: [
    ReactiveFormsModule,
    CanDirective,
    SaveShortcutDirective,
    UppercaseDirective,
    PageHeaderComponent,
    SectionCardComponent,
    ToolbarComponent,
    DataTableComponent,
    CellTemplateDirective,
    RowActionsTemplateDirective,
    PaginatorComponent,
    BadgeComponent,
    DrawerComponent,
    DialogComponent,
    FormFieldComponent,
    LocalizedTextInputComponent,
    StatCardComponent,
    TabsComponent,
    EmptyStateComponent,
    IconComponent,
  ],
  templateUrl: './subscriptions.page.html',
  styleUrl: './platform-pages.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SubscriptionsPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(WalletsFacade);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly csv = inject(CsvExportService);
  private readonly route = inject(ActivatedRoute);

  protected readonly permissions = PLATFORM_PERMISSIONS;
  protected readonly kinds = WALLET_TRANSACTION_KINDS;

  protected readonly tab = signal<string | null>(null);
  protected readonly tabs = computed<readonly TabItem[]>(() => [
    { id: 'wallets', label: this.i18n.t('platform.subscriptions.tab.wallets') },
    { id: 'plans', label: this.i18n.t('platform.subscriptions.tab.plans') },
  ]);

  protected readonly planColumns: readonly TableColumn<PlanListItem>[] = [
    { key: 'code', header: 'platform.plans.column.code', mono: true, sortable: true, width: '130px' },
    { key: 'name', header: 'platform.plans.column.name', sortable: true, value: (row) => pick(row.nameEn, row.nameAr, this.i18n.lang()) },
    { key: 'monthlyFee', header: 'platform.plans.column.monthlyFee', align: 'end', sortable: true, value: (row) => formatMoney(row.monthlyFee, row.currencyCode, this.i18n.lang()) },
    { key: 'maxBranches', header: 'platform.plans.column.branches', align: 'end' },
    { key: 'maxUsers', header: 'platform.plans.column.users', align: 'end' },
    { key: 'smsQuota', header: 'platform.plans.column.smsQuota', align: 'end' },
    { key: 'maxAnalysers', header: 'platform.plans.column.analysers', align: 'end', secondary: true },
    { key: 'clientCount', header: 'platform.plans.column.clients', align: 'end', secondary: true },
    { key: 'status', header: 'platform.plans.column.status', width: '110px' },
  ];

  protected readonly transactionColumns: readonly TableColumn<WalletTransactionItem>[] = [
    { key: 'atUtc', header: 'platform.wallet.column.date', value: (row) => formatDateTime(row.atUtc, this.i18n.lang()), width: '160px' },
    { key: 'kind', header: 'platform.wallet.column.kind', width: '130px' },
    { key: 'amount', header: 'platform.wallet.column.amount', align: 'end' },
    { key: 'balanceAfter', header: 'platform.wallet.column.balanceAfter', align: 'end', value: (row) => formatMoney(row.balanceAfter, null, this.i18n.lang()), secondary: true },
    { key: 'reference', header: 'platform.wallet.column.reference', mono: true },
    { key: 'note', header: 'platform.wallet.column.note', secondary: true },
    { key: 'byName', header: 'platform.wallet.column.user', secondary: true },
  ];

  protected readonly wallet = computed(() => this.facade.wallet.value() ?? null);
  protected readonly clients = computed(() => this.facade.clientOptions.value()?.items ?? []);

  // plan drawer
  protected readonly planDrawerOpen = signal(false);
  protected readonly editingPlan = signal<PlanListItem | null>(null);
  protected readonly planForm = signal<PlanForm | null>(null);
  protected readonly planProblem = signal<ProblemDetails | null>(null);
  /** F-03 (validation v0.5.0): a refused save says so above the form and focuses the first marked field. */
  protected readonly planBlocked = blockedSave();

  // top-up dialog
  protected readonly topUpOpen = signal(false);
  protected readonly topUpFor = signal<TopUpRequestItem | null>(null);
  protected readonly topUpForm = new FormGroup({
    amount: new FormControl<number | null>(null, { validators: [Validators.required, Validators.min(0.01), Validators.max(100_000_000), maxTwoDecimals(() => this.i18n.t('platform.wallet.validation.twoDecimals'))] }),
    reference: new FormControl('', { nonNullable: true, validators: [requiredText, Validators.maxLength(100)] }),
    note: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
  });

  constructor() {
    void this.i18n.load('platform');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14)
    const fromQuery = this.route.snapshot.queryParamMap.get('client');
    if (fromQuery) {
      this.facade.selectClient(fromQuery);
    }

    // with no client chosen, open the first one of the list
    effect(() => {
      const first = this.clients()[0];
      if (first && untracked(() => this.facade.clientId()) === null) {
        untracked(() => this.facade.selectClient(first.id));
      }
    });
  }

  protected name(row: { nameEn: string; nameAr: string }): string {
    return pick(row.nameEn, row.nameAr, this.i18n.lang());
  }

  protected pickPlan(wallet: WalletDetail): string {
    return pick(wallet.planNameEn, wallet.planNameAr, this.i18n.lang());
  }

  protected money(amount: number | null | undefined, currency: string | null = null): string {
    return formatMoney(amount, currency, this.i18n.lang());
  }

  protected number(value: number): string {
    return formatNumber(value, this.i18n.lang());
  }

  protected date(iso: string | null): string {
    return formatDate(iso, this.i18n.lang());
  }

  protected tone(status: string): ReturnType<typeof statusTone> {
    return statusTone(status);
  }

  protected onClient(event: Event): void {
    this.facade.selectClient((event.target as HTMLSelectElement).value);
  }

  protected onKind(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setKind(this.kinds.find((kind) => kind === value) ?? null);
  }

  protected onTransactionPage(page: PageRequest): void {
    this.facade.setTransactionPage(page);
  }

  protected onPlanFilter(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.planActive.set(value === 'all' ? null : value === 'active');
  }

  // ── top-up ────────────────────────────────────────────────────────────────────────────────

  protected openTopUp(request: TopUpRequestItem | null): void {
    this.topUpFor.set(request);
    this.topUpForm.reset({ amount: request?.amount ?? null, reference: '', note: request?.note ?? '' });
    this.facade.topUp.reset();
    this.topUpOpen.set(true);
  }

  protected async submitTopUp(): Promise<void> {
    if (!markAllTouched(this.topUpForm) || this.facade.topUp.pending()) {
      return;
    }

    const value = this.topUpForm.getRawValue();
    const note = value.note.trim();
    const done = await this.facade.topUp.execute({ amount: value.amount ?? 0, reference: value.reference.trim(), note: note === '' ? null : note, topUpRequestId: this.topUpFor()?.id ?? null });
    if (done !== null) {
      this.topUpOpen.set(false);
      this.toast.success(this.i18n.t('platform.wallet.toppedUp'));
    }
  }

  /** SA-05 "Run charges now" (W2): the monthly subscription charge of every due wallet, as the hosted job runs it; a period is never charged twice. */
  protected async runCharges(): Promise<void> {
    if (this.facade.runCharges.pending()) {
      return;
    }

    const confirmed = await this.confirm.confirm({ title: this.i18n.t('platform.wallet.runCharges'), message: this.i18n.t('platform.wallet.confirmRunCharges') });
    if (!confirmed) {
      return;
    }

    const summary = await this.facade.runCharges.execute();
    if (summary === null) {
      this.toast.error(this.problemText(this.facade.runCharges.problem()));
      return;
    }

    const text = this.i18n.tf('platform.wallet.chargesRun', { charged: this.number(summary.charged), skipped: this.number(summary.skipped), failed: this.number(summary.failed) });
    if (summary.failed > 0) {
      this.toast.warn(text);
    } else {
      this.toast.success(text);
    }
  }

  protected async declineRequest(request: TopUpRequestItem): Promise<void> {
    const confirmed = await this.confirm.confirm({ title: this.i18n.t('platform.wallet.decline'), message: this.i18n.tf('platform.wallet.confirmDecline', { amount: this.money(request.amount, this.wallet()?.currencyCode ?? null) }), danger: true });
    if (!confirmed) {
      return;
    }

    if ((await this.facade.decline.execute(request.id)) !== null) {
      this.toast.success(this.i18n.t('platform.wallet.declined'));
    } else {
      this.toast.error(this.problemText(this.facade.decline.problem()));
    }
  }

  protected async exportTransactions(): Promise<void> {
    const wallet = this.wallet();
    if (wallet === null) {
      return;
    }

    try {
      const rows = await this.facade.exportTransactions();
      this.csv.download(`wallet-${wallet.tenantId.slice(0, 8)}-${fileStamp()}`, [
        { header: this.i18n.t('platform.wallet.column.date'), value: (row: WalletTransactionItem) => row.atUtc },
        { header: this.i18n.t('platform.wallet.column.kind'), value: (row) => this.i18n.t(`platform.wallet.kind.${row.kind}`) },
        { header: this.i18n.t('platform.wallet.column.amount'), value: (row) => row.amount.toFixed(2) },
        { header: this.i18n.t('platform.wallet.column.balanceAfter'), value: (row) => row.balanceAfter.toFixed(2) },
        { header: this.i18n.t('platform.wallet.column.reference'), value: (row) => row.reference },
        { header: this.i18n.t('platform.wallet.column.note'), value: (row) => row.note },
        { header: this.i18n.t('platform.wallet.column.user'), value: (row) => row.byName },
      ], rows);
    } catch (error: unknown) {
      this.toast.error(this.problemText(error instanceof ProblemError ? error.problem : null));
    }
  }

  protected kindLabel(kind: WalletTransactionKind): string {
    return this.i18n.t(`platform.wallet.kind.${kind}`);
  }

  // ── plans ─────────────────────────────────────────────────────────────────────────────────

  protected openPlan(plan: PlanListItem | null): void {
    this.editingPlan.set(plan);
    this.planProblem.set(null);
    this.planBlocked.reset();
    this.planForm.set(this.buildPlanForm(plan));
    this.planDrawerOpen.set(true);
  }

  protected async savePlan(): Promise<void> {
    const form = this.planForm();
    if (form === null || this.facade.savePlan.pending() || !this.planBlocked.check(form, '#platform-plan-form')) {
      return;
    }

    const value = form.getRawValue();
    const request = {
      code: value.code.trim().toUpperCase(),
      nameEn: value.name.en.trim(),
      nameAr: value.name.ar.trim(),
      monthlyFee: value.monthlyFee ?? 0,
      currencyCode: value.currencyCode.trim().toUpperCase(),
      maxBranches: value.maxBranches ?? 1,
      maxUsers: value.maxUsers ?? 1,
      smsQuota: value.smsQuota ?? 0,
      maxAnalysers: value.maxAnalysers ?? 0,
    };
    this.planProblem.set(null);
    if ((await this.facade.savePlan.execute({ id: this.editingPlan()?.id ?? null, request })) !== null) {
      this.planDrawerOpen.set(false);
      this.toast.success(this.i18n.t('platform.plans.saved'));
      return;
    }

    const problem = this.facade.savePlan.problem();
    if (problem !== null) {
      problemToFormErrors(problem, form);
      this.planProblem.set(problem);
    }
  }

  protected async togglePlan(plan: PlanListItem): Promise<void> {
    const key = plan.isActive ? 'deactivate' : 'activate';
    const confirmed = await this.confirm.confirm({ title: this.i18n.t(`platform.plans.${key}`), message: this.i18n.tf(`platform.plans.confirm.${key}`, { code: plan.code }), danger: plan.isActive });
    if (!confirmed) {
      return;
    }

    if ((await this.facade.setPlanActive.execute({ id: plan.id, active: !plan.isActive })) !== null) {
      this.toast.success(this.i18n.t(plan.isActive ? 'platform.plans.deactivated' : 'platform.plans.activated'));
    } else {
      this.toast.error(this.problemText(this.facade.setPlanActive.problem()));
    }
  }

  protected problemText(problem: ProblemDetails | null): string {
    return problem ? problemMessage(this.i18n, 'platform', problem) : this.i18n.t('shared.error.generic');
  }

  private buildPlanForm(plan: PlanListItem | null) {
    return new FormGroup({
      code: new FormControl({ value: plan?.code ?? '', disabled: plan !== null }, { nonNullable: true, validators: [requiredText, codeValidator, Validators.maxLength(20)] }),
      name: localizedTextGroup({ en: plan?.nameEn ?? '', ar: plan?.nameAr ?? '' }, { required: true, maxLength: 100 }),
      // C-05: the fee is refused with more than two decimals (PLN-002 on the server), never rounded.
      monthlyFee: new FormControl<number | null>(plan?.monthlyFee ?? 0, { validators: [Validators.required, Validators.min(0), Validators.max(100_000_000), maxTwoDecimals(() => this.i18n.t('platform.plans.validation.twoDecimals'))] }),
      currencyCode: new FormControl(plan?.currencyCode ?? 'EGP', { nonNullable: true, validators: [requiredText, Validators.pattern(/^[A-Za-z]{3}$/)] }),
      maxBranches: new FormControl<number | null>(plan?.maxBranches ?? 1, { validators: [Validators.required, Validators.min(1)] }),
      maxUsers: new FormControl<number | null>(plan?.maxUsers ?? 5, { validators: [Validators.required, Validators.min(1)] }),
      smsQuota: new FormControl<number | null>(plan?.smsQuota ?? 0, { validators: [Validators.required, Validators.min(0)] }),
      maxAnalysers: new FormControl<number | null>(plan?.maxAnalysers ?? 0, { validators: [Validators.required, Validators.min(0)] }),
    });
  }
}
