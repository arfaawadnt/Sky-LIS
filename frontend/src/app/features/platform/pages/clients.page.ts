import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

import type { PageRequest } from '../../../core/api/paging';
import {
  CLIENT_SOURCES,
  CLIENT_STATUSES,
  type ClientDetail,
  type ClientDetailsRequest,
  type ClientListItem,
  PLATFORM_PERMISSIONS,
  type PlanListItem,
  SILENT_DAYS,
} from '../../../core/api/platform.contracts';
import { ProblemError } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedTextGroup } from '../../../shared/forms/localized-text';
import { blockedSave } from '../../../shared/forms/blocked-save';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { problemToFormErrors, SERVER_ERROR_KEY } from '../../../shared/forms/problem-to-form-errors';
import { MobileInputComponent } from '../../../shared/forms/mobile-input/mobile-input.component';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { mobileValidator, requiredText } from '../../../shared/forms/validators';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { DrawerComponent } from '../../../shared/ui/drawer/drawer.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { LocalizedTextInputComponent } from '../../../shared/ui/localized-text-input/localized-text-input.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { CsvExportService } from '../data/csv-export';
import { PlatformApi } from '../data/platform.api';
import { ClientsFacade } from '../state/clients.facade';
import { fileStamp, formatDate, formatMoney, pick } from '../ui/format';
import { SilentClientsComponent } from '../ui/silent-clients.component';
import { statusTone } from '../ui/status';
import {
  countryMobileValidator,
  matchingPasswords,
  type MobileCountry,
  notContaining,
  PASSWORD_MAX_LENGTH,
  PASSWORD_MIN_LENGTH,
  SLUG_PATTERN,
  USERNAME_PATTERN,
} from '../ui/validators';

type ClientForm = ReturnType<ClientsPage['buildForm']>;

/**
 * SA-04 Clients: the tenant life cycle. Filters by source, plan and status; drawer to add (manual provisioning with
 * the administrator's credentials, sealed by the envelope) and edit; reset the administrator's password; open the
 * wallet; activate / deactivate with a reason (users are then blocked at sign-in). CSV export of the filtered list.
 */
@Component({
  selector: 'app-platform-clients-page',
  imports: [
    ReactiveFormsModule,
    CanDirective,
    SaveShortcutDirective,
    PageHeaderComponent,
    SectionCardComponent,
    ToolbarComponent,
    SearchBoxComponent,
    DataTableComponent,
    CellTemplateDirective,
    RowActionsTemplateDirective,
    PaginatorComponent,
    BadgeComponent,
    DrawerComponent,
    DialogComponent,
    FormFieldComponent,
    MobileInputComponent,
    LocalizedTextInputComponent,
    IconComponent,
    SilentClientsComponent,
  ],
  templateUrl: './clients.page.html',
  styleUrl: './platform-pages.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClientsPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(ClientsFacade);
  private readonly api = inject(PlatformApi);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly csv = inject(CsvExportService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly permissions = PLATFORM_PERMISSIONS;
  protected readonly sources = CLIENT_SOURCES;
  protected readonly statuses = CLIENT_STATUSES;

  /** LIS-CR-007 A-01: the Silent filter (`?silent=3` from the dashboard tile); null shows the register. */
  protected readonly silentOptions = SILENT_DAYS;
  protected readonly silentDays = signal<number | null>(silentDaysOf(this.route.snapshot.queryParamMap.get('silent')));
  private readonly silentPanel = viewChild(SilentClientsComponent);
  protected readonly passwordMin = PASSWORD_MIN_LENGTH;

  protected readonly columns: readonly TableColumn<ClientListItem>[] = [
    { key: 'name', header: 'platform.clients.column.client', sortable: true, value: (row) => this.name(row) },
    { key: 'country', header: 'platform.clients.column.country', value: (row) => `${pick(row.countryNameEn, row.countryNameAr, this.i18n.lang())} · ${pick(row.governorateNameEn, row.governorateNameAr, this.i18n.lang())}`, secondary: true },
    { key: 'manager', header: 'platform.clients.column.manager', value: (row) => row.managerName },
    { key: 'planCode', header: 'platform.clients.column.plan', mono: true, sortable: true },
    { key: 'walletBalance', header: 'platform.clients.column.wallet', align: 'end', sortable: true, value: (row) => row.walletBalance },
    { key: 'branches', header: 'platform.clients.column.branches', align: 'end', width: '80px', secondary: true },
    { key: 'users', header: 'platform.clients.column.users', align: 'end', width: '70px', secondary: true },
    { key: 'createdAtUtc', header: 'platform.clients.column.created', sortable: true, value: (row) => formatDate(row.createdAtUtc, this.i18n.lang()), secondary: true },
    { key: 'source', header: 'platform.clients.column.source', width: '120px' },
    { key: 'status', header: 'platform.clients.column.status', width: '110px' },
  ];

  // drawer
  protected readonly drawerOpen = signal(false);
  protected readonly editing = signal<ClientDetail | null>(null);
  protected readonly form = signal<ClientForm | null>(null);
  protected readonly saveProblem = signal<ProblemError['problem'] | null>(null);
  /** F-03 (validation v0.5.0): a refused save says so above the form and focuses the first marked field. */
  protected readonly blockedSave = blockedSave();
  protected readonly saving = computed(() => this.facade.provision.pending() || this.facade.update.pending());
  protected readonly governorateOptions = computed(() => this.facade.governorates.value()?.items ?? []);

  /**
   * Validation 2026-10-11 C-07: the drawer offers active plans only (the server refuses an inactive one, PLN-010); on edit the
   * client's current plan stays listed, marked inactive, so the form still shows what the client has.
   */
  protected readonly planOptions = computed<readonly PlanListItem[]>(() => {
    const current = this.editing()?.planId ?? null;
    return (this.facade.plans.value()?.items ?? []).filter((plan) => plan.isActive || plan.id === current);
  });

  // reset-password dialog
  protected readonly resetTarget = signal<ClientListItem | null>(null);
  protected readonly resetOpen = signal(false);
  protected readonly resetForm = new FormGroup(
    {
      newPassword: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(PASSWORD_MIN_LENGTH), Validators.maxLength(PASSWORD_MAX_LENGTH)] }),
      confirm: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    },
    { validators: matchingPasswords('newPassword', 'confirm', () => this.i18n.t('platform.validation.mismatch')) },
  );

  constructor() {
    void this.i18n.load('platform');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14)
  }

  // ── list ──────────────────────────────────────────────────────────────────────────────────

  protected name(row: { nameEn: string; nameAr: string }): string {
    return pick(row.nameEn, row.nameAr, this.i18n.lang());
  }

  protected money(row: ClientListItem): string {
    return formatMoney(row.walletBalance, row.walletCurrency, this.i18n.lang());
  }

  protected tone(status: string): ReturnType<typeof statusTone> {
    return statusTone(status);
  }

  protected onSource(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setSource(this.sources.find((source) => source === value) ?? null);
  }

  protected onStatus(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setStatus(this.statuses.find((status) => status === value) ?? null);
  }

  protected onPlan(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setPlan(value === '' ? null : value);
  }

  protected onSilent(event: Event): void {
    const days = silentDaysOf((event.target as HTMLSelectElement).value);
    this.silentDays.set(days);
    void this.router.navigate([], { relativeTo: this.route, queryParams: { silent: days }, queryParamsHandling: 'merge', replaceUrl: true });
  }

  protected reloadList(): void {
    const silent = this.silentPanel();
    if (silent) {
      silent.refresh();
    } else {
      this.facade.clients.reload();
    }
  }

  protected onPage(page: PageRequest): void {
    this.facade.setPage(page);
  }

  protected async exportCsv(): Promise<void> {
    const silent = this.silentPanel();
    if (silent) {
      await silent.exportCsv();   // LIS-CR-007 A-01: the server's CSV of the silent clients
      return;
    }

    try {
      const rows = await this.facade.exportRows();
      const lang = this.i18n.lang();
      this.csv.download(`clients-${fileStamp()}`, [
        { header: this.i18n.t('platform.clients.column.nameEn'), value: (row: ClientListItem) => row.nameEn },
        { header: this.i18n.t('platform.clients.column.nameAr'), value: (row) => row.nameAr },
        { header: this.i18n.t('platform.clients.column.portalUrl'), value: (row) => row.portalUrl },
        { header: this.i18n.t('platform.clients.column.country'), value: (row) => pick(row.countryNameEn, row.countryNameAr, lang) },
        { header: this.i18n.t('platform.clients.column.governorate'), value: (row) => pick(row.governorateNameEn, row.governorateNameAr, lang) },
        { header: this.i18n.t('platform.clients.column.manager'), value: (row) => row.managerName },
        { header: this.i18n.t('platform.clients.column.managerMobile'), value: (row) => row.managerMobile },
        { header: this.i18n.t('platform.clients.column.plan'), value: (row) => row.planCode },
        { header: this.i18n.t('platform.clients.column.wallet'), value: (row) => row.walletBalance.toFixed(2) },
        { header: this.i18n.t('platform.clients.column.currency'), value: (row) => row.walletCurrency },
        { header: this.i18n.t('platform.clients.column.branches'), value: (row) => row.branches },
        { header: this.i18n.t('platform.clients.column.users'), value: (row) => row.users },
        { header: this.i18n.t('platform.clients.column.created'), value: (row) => row.createdAtUtc.slice(0, 10) },
        { header: this.i18n.t('platform.clients.column.source'), value: (row) => this.i18n.t(`platform.source.${row.source}`) },
        { header: this.i18n.t('platform.clients.column.status'), value: (row) => this.i18n.t(`platform.status.${row.status}`) },
      ], rows);
    } catch (error: unknown) {
      this.toast.error(this.problemText(error));
    }
  }

  // ── life cycle ────────────────────────────────────────────────────────────────────────────

  protected async toggle(row: ClientListItem): Promise<void> {
    if (row.status === 'Active' || row.status === 'Trial') {
      const reason = await this.confirm.promptReason({
        title: this.i18n.t('platform.clients.deactivate'),
        message: this.i18n.tf('platform.clients.confirmDeactivate', { name: this.name(row) }),
        danger: true,
        label: this.i18n.t('platform.clients.reason'),
        maxLength: 500,
      });
      if (reason === null) {
        return;
      }

      if ((await this.facade.deactivate.execute({ id: row.id, reason })) !== null) {
        this.toast.success(this.i18n.t('platform.clients.deactivated'));
      } else {
        this.toast.error(this.problemText(this.facade.deactivate.problem()));
      }

      return;
    }

    const confirmed = await this.confirm.confirm({ title: this.i18n.t('platform.clients.activate'), message: this.i18n.tf('platform.clients.confirmActivate', { name: this.name(row) }) });
    if (!confirmed) {
      return;
    }

    if ((await this.facade.activate.execute(row.id)) !== null) {
      this.toast.success(this.i18n.t('platform.clients.activated'));
    } else {
      this.toast.error(this.problemText(this.facade.activate.problem()));
    }
  }

  protected openWallet(row: ClientListItem): void {
    void this.router.navigate(['../subscriptions'], { relativeTo: this.route, queryParams: { client: row.id } });
  }

  protected openReset(row: ClientListItem): void {
    this.resetTarget.set(row);
    this.resetForm.reset();
    this.facade.resetPassword.reset();
    this.resetOpen.set(true);
  }

  protected async submitReset(): Promise<void> {
    const target = this.resetTarget();
    if (target === null || !markAllTouched(this.resetForm) || this.facade.resetPassword.pending()) {
      return;
    }

    const done = await this.facade.resetPassword.execute({ id: target.id, newPassword: this.resetForm.controls.newPassword.value });
    if (done !== null) {
      this.resetOpen.set(false);
      this.toast.success(this.i18n.tf('platform.clients.passwordReset', { name: this.name(target) }));
    }
  }

  // ── drawer ────────────────────────────────────────────────────────────────────────────────

  protected openCreate(): void {
    this.editing.set(null);
    this.saveProblem.set(null);
    this.blockedSave.reset();
    this.facade.drawerCountryId.set(null);
    this.form.set(this.buildForm(null));
    this.drawerOpen.set(true);
  }

  protected async openEdit(row: Pick<ClientListItem, 'id'>): Promise<void> {
    try {
      const detail = await this.api.getClient(row.id);
      this.editing.set(detail);
      this.saveProblem.set(null);
      this.blockedSave.reset();
      this.facade.drawerCountryId.set(detail.countryId);
      this.form.set(this.buildForm(detail));
      this.drawerOpen.set(true);
    } catch (error: unknown) {
      this.toast.error(this.problemText(error));
    }
  }

  protected onDrawerClosed(): void {
    this.form.set(null);
    this.editing.set(null);
  }

  protected onCountryChange(form: ClientForm): void {
    this.facade.drawerCountryId.set(form.controls.countryId.value === '' ? null : form.controls.countryId.value);
    form.controls.governorateId.setValue('');
    // C-03: the mobiles are checked against the chosen country's length
    form.controls.mobile.updateValueAndValidity();
    form.controls.managerMobile.updateValueAndValidity();
  }

  protected async save(): Promise<void> {
    const form = this.form();
    if (form === null || this.saving() || !this.blockedSave.check(form, '#platform-client-form')) {
      return;
    }

    const value = form.getRawValue();
    const details: ClientDetailsRequest = {
      nameEn: value.name.en.trim(),
      nameAr: value.name.ar.trim(),
      countryId: value.countryId,
      governorateId: value.governorateId,
      address: value.address.trim(),
      mobile: value.mobile.trim(),
      licenceNo: emptyToNull(value.licenceNo),
      managerName: value.managerName.trim(),
      managerMobile: value.managerMobile.trim(),
      email: emptyToNull(value.email),
      planId: value.planId,
      language: value.language,
    };

    this.saveProblem.set(null);
    const editing = this.editing();
    if (editing === null) {
      const created = await this.facade.provision.execute({
        ...details,
        portalSlug: emptyToNull(value.portalSlug.toLowerCase()),
        adminUsername: value.adminUsername.trim(),
        adminPassword: value.adminPassword,
      });
      if (created !== null) {
        this.drawerOpen.set(false);
        this.toast.success(this.i18n.tf('platform.clients.provisioned', { url: created.portalUrl, username: created.administratorUsername }));
      } else {
        this.applyProblem(this.facade.provision.problem(), form);
      }

      return;
    }

    if ((await this.facade.update.execute({ id: editing.id, request: details })) !== null) {
      this.drawerOpen.set(false);
      this.toast.success(this.i18n.t('platform.clients.saved'));
    } else {
      this.applyProblem(this.facade.update.problem(), form);
    }
  }

  protected problemText(problemOrError: unknown): string {
    const problem = problemOrError instanceof ProblemError ? problemOrError.problem : (problemOrError as ProblemError['problem'] | null);
    return problem && typeof problem === 'object' && 'code' in problem ? problemMessage(this.i18n, 'platform', problem) : this.i18n.t('shared.error.generic');
  }

  private applyProblem(problem: ProblemError['problem'] | null, form: ClientForm): void {
    if (problem === null) {
      return;
    }

    // C-03: a mobile refused for the country's length is shown under its own field (GEO-010 laboratory, GEO-014 manager)
    const mobileControl = problem.code === 'GEO-010' ? form.controls.mobile : problem.code === 'GEO-014' ? form.controls.managerMobile : null;
    if (mobileControl !== null) {
      mobileControl.setErrors({ ...(mobileControl.errors ?? {}), [SERVER_ERROR_KEY]: this.problemText(problem) });
      mobileControl.markAsTouched();
      return;
    }

    const unmatched = problemToFormErrors(problem, form);
    if (problem.code !== 'VALIDATION-400' || Object.keys(unmatched).length > 0 || !problem.errors) {
      this.saveProblem.set(problem);
    }
  }

  /** LIS-CR-007 C-05: the drawer's country gives the two mobiles their dial code. */
  protected readonly drawerCountry = computed(() => {
    const id = this.facade.drawerCountryId();
    return id === null ? null : this.countryById(id);
  });

  private countryMobile(messageKey: string) {
    return countryMobileValidator(
      'countryId',
      (id) => this.countryById(id),
      (country) => this.i18n.tf(messageKey, { length: country.mobileLength, dial: country.dialCode }),
    );
  }

  private countryById(id: string): MobileCountry | null {
    return this.facade.countries.value()?.items.find((country) => country.id === id) ?? null;
  }

  private buildForm(detail: ClientDetail | null) {
    const creating = detail === null;
    const form = new FormGroup(
      {
        name: localizedTextGroup({ en: detail?.nameEn ?? '', ar: detail?.nameAr ?? '' }, { required: true, maxLength: 200 }),
        countryId: new FormControl(detail?.countryId ?? '', { nonNullable: true, validators: [Validators.required] }),
        governorateId: new FormControl(detail?.governorateId ?? '', { nonNullable: true, validators: [Validators.required] }),
        address: new FormControl(detail?.address ?? '', { nonNullable: true, validators: [requiredText, Validators.maxLength(300)] }),
        mobile: new FormControl(detail?.mobile ?? '', { nonNullable: true, validators: [requiredText, mobileValidator, this.countryMobile('platform.clients.validation.mobileLength')] }),
        licenceNo: new FormControl(detail?.licenceNo ?? '', { nonNullable: true, validators: [Validators.maxLength(64)] }),
        managerName: new FormControl(detail?.managerName ?? '', { nonNullable: true, validators: [requiredText, Validators.maxLength(200)] }),
        managerMobile: new FormControl(detail?.managerMobile ?? '', { nonNullable: true, validators: [requiredText, mobileValidator, this.countryMobile('platform.clients.validation.managerMobileLength')] }),
        email: new FormControl(detail?.email ?? '', { nonNullable: true, validators: [Validators.email, Validators.maxLength(254)] }),
        planId: new FormControl(detail?.planId ?? '', { nonNullable: true, validators: [Validators.required] }),
        language: new FormControl(detail?.language ?? 'en', { nonNullable: true }),
        portalSlug: new FormControl({ value: detail?.portalSlug ?? '', disabled: !creating }, { nonNullable: true, validators: [Validators.pattern(SLUG_PATTERN)] }),
        adminUsername: new FormControl({ value: detail?.administratorUsername ?? '', disabled: !creating }, { nonNullable: true, validators: creating ? [requiredText, Validators.pattern(USERNAME_PATTERN)] : [] }),
        adminPassword: new FormControl({ value: '', disabled: !creating }, { nonNullable: true, validators: creating ? [Validators.required, Validators.minLength(PASSWORD_MIN_LENGTH), Validators.maxLength(PASSWORD_MAX_LENGTH), notContaining('adminUsername', () => this.i18n.t('platform.validation.containsUsername'))] : [] }),
        adminPasswordConfirm: new FormControl({ value: '', disabled: !creating }, { nonNullable: true, validators: creating ? [Validators.required] : [] }),
      },
      { validators: creating ? [matchingPasswords('adminPassword', 'adminPasswordConfirm', () => this.i18n.t('platform.validation.mismatch'))] : [] },
    );
    return form;
  }
}

/** The Silent window of a query parameter or select value: one of the offered windows, else null (the register). */
export function silentDaysOf(value: string | null | undefined): number | null {
  const days = Number(value);
  return value !== null && value !== undefined && value !== '' && SILENT_DAYS.includes(days) ? days : null;
}

function emptyToNull(value: string): string | null {
  const trimmed = value.trim();
  return trimmed === '' ? null : trimmed;
}

