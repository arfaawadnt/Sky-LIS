import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators, type AbstractControl, type ValidationErrors } from '@angular/forms';

import {
  AGENT_LOG_LEVELS,
  INTAKE_STATUSES,
  INTEGRATION_PERMISSIONS,
  type AgentLogItem,
  type AgentLogLevel,
  type IntakeLogItem,
  type IntakeStatus,
  type SiteAgentListItem,
  type SiteAgentSecretResponse,
} from '../../../core/api/integration.contracts';
import type { PageRequest } from '../../../core/api/paging';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { problemToFormErrors } from '../../../shared/forms/problem-to-form-errors';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { RowMenuComponent } from '../../../shared/ui/data-table/row-menu.component';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { DrawerComponent } from '../../../shared/ui/drawer/drawer.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { TabsComponent, type TabItem } from '../../../shared/ui/tabs/tabs.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { SiteAgentsFacade } from '../state/site-agents.facade';
import { ageText, dateTimeText, integrationTone, localInputToIso } from '../ui/integration-format';
import { SecretDialogComponent } from '../ui/secret-dialog.component';

/** A JSON object or blank (INT-018 on the server). */
export function jsonObjectValidator(control: AbstractControl): ValidationErrors | null {
  const value = String(control.value ?? '').trim();
  if (value === '') {
    return null;
  }

  try {
    const parsed: unknown = JSON.parse(value);
    return parsed !== null && typeof parsed === 'object' && !Array.isArray(parsed) ? null : { json: true };
  } catch {
    return { json: true };
  }
}

/**
 * Site agents (SRS CP-61): per-branch connectors with status (Online, Error, Offline after five minutes without a
 * heartbeat) and heartbeat age, version and connected analysers; creating one issues its machine credential and shows
 * the client secret once; rotate the secret, push the configuration, read the agent's log with filters, deactivate
 * (revokes the credential) and reactivate (new credential). The second tab is the result intake log (WF-07).
 */
@Component({
  selector: 'app-site-agents-page',
  imports: [
    ReactiveFormsModule,
    PageHeaderComponent,
    SectionCardComponent,
    ToolbarComponent,
    SearchBoxComponent,
    DataTableComponent,
    RowMenuComponent,
    CellTemplateDirective,
    RowActionsTemplateDirective,
    PaginatorComponent,
    BadgeComponent,
    CanDirective,
    DrawerComponent,
    DialogComponent,
    FormFieldComponent,
    SaveShortcutDirective,
    IconComponent,
    TabsComponent,
    SecretDialogComponent,
  ],
  templateUrl: './site-agents.page.html',
  styleUrl: './site-agents.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SiteAgentsPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(SiteAgentsFacade);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);

  protected readonly permissions = INTEGRATION_PERMISSIONS;
  protected readonly levels = AGENT_LOG_LEVELS;
  protected readonly intakeStatuses = INTAKE_STATUSES;
  protected readonly tone = integrationTone;

  protected readonly tab = signal<string | null>('agents');
  protected readonly tabs = computed<readonly TabItem[]>(() => [
    { id: 'agents', label: this.i18n.t('integration.agents.tab') },
    { id: 'intake', label: this.i18n.t('integration.intake.tab') },
  ]);

  protected readonly columns: readonly TableColumn<SiteAgentListItem>[] = [
    { key: 'name', header: 'integration.field.name', sortable: true },
    { key: 'branch', header: 'integration.field.branch', value: (row) => localizedPick({ en: row.branchNameEn ?? '', ar: row.branchNameAr ?? '' }, this.i18n.lang()) },
    { key: 'status', header: 'integration.field.status', width: '120px' },
    { key: 'heartbeat', header: 'integration.agents.heartbeat', value: (row) => this.age(row.lastHeartbeatAtUtc) },
    { key: 'version', header: 'integration.agents.version', secondary: true, value: (row) => row.version ?? '' },
    { key: 'analyserCount', header: 'integration.agents.analysers', align: 'end', secondary: true },
    { key: 'clientId', header: 'integration.secret.clientId', mono: true, secondary: true, value: (row) => row.clientId ?? '' },
    { key: 'active', header: 'integration.field.active', width: '100px' },
  ];

  protected readonly logColumns: readonly TableColumn<AgentLogItem>[] = [
    { key: 'atUtc', header: 'integration.logs.time', width: '180px', value: (row) => dateTimeText(this.i18n, row.atUtc) },
    { key: 'level', header: 'integration.logs.level', width: '100px' },
    { key: 'analyserCode', header: 'integration.field.analyser', mono: true, width: '110px', secondary: true },
    { key: 'sampleNumber', header: 'integration.intake.sample', mono: true, width: '140px', secondary: true },
    { key: 'message', header: 'integration.logs.message' },
  ];

  protected readonly intakeColumns: readonly TableColumn<IntakeLogItem>[] = [
    { key: 'atUtc', header: 'integration.logs.time', width: '170px', value: (row) => dateTimeText(this.i18n, row.atUtc) },
    { key: 'agentName', header: 'integration.field.siteAgent', secondary: true, value: (row) => row.agentName ?? '' },
    { key: 'analyserCode', header: 'integration.field.analyser', mono: true, width: '100px' },
    { key: 'sampleNumber', header: 'integration.intake.sample', mono: true },
    { key: 'analyserTestCode', header: 'integration.mapping.analyserCode', mono: true, width: '100px' },
    { key: 'value', header: 'integration.intake.value' },
    { key: 'flags', header: 'integration.intake.flags', secondary: true, value: (row) => row.flags.join(', ') },
    { key: 'status', header: 'integration.field.status', width: '130px' },
    { key: 'reason', header: 'integration.intake.reason', secondary: true, value: (row) => row.reason ?? '' },
  ];

  protected readonly rows = computed<readonly SiteAgentListItem[]>(() => (this.facade.list.hasValue() ? this.facade.list.value().items : []));
  protected readonly total = computed(() => (this.facade.list.hasValue() ? this.facade.list.value().totalCount : 0));
  protected readonly branches = computed(() => (this.facade.branches.hasValue() ? this.facade.branches.value() : []));
  protected readonly analysers = computed(() => (this.facade.analysers.hasValue() ? this.facade.analysers.value().items : []));
  protected readonly logRows = computed<readonly AgentLogItem[]>(() => (this.facade.logs.hasValue() ? this.facade.logs.value().items : []));
  protected readonly logTotal = computed(() => (this.facade.logs.hasValue() ? this.facade.logs.value().totalCount : 0));
  protected readonly intakeRows = computed<readonly IntakeLogItem[]>(() => (this.facade.intake.hasValue() ? this.facade.intake.value().items : []));
  protected readonly intakeTotal = computed(() => (this.facade.intake.hasValue() ? this.facade.intake.value().totalCount : 0));
  protected readonly detail = computed(() => (this.facade.detail.hasValue() ? this.facade.detail.value() : null));

  protected readonly createOpen = signal(false);
  protected readonly createForm = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] }),
    branchId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    configJson: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(16384), jsonObjectValidator] }),
  });

  protected readonly detailOpen = signal(false);
  protected readonly editForm = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] }),
    configJson: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(16384), jsonObjectValidator] }),
  });

  protected readonly logsOpen = signal(false);

  protected readonly secretOpen = signal(false);
  protected readonly secret = signal<SiteAgentSecretResponse | null>(null);

  constructor() {
    void this.i18n.load('integration');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14)
  }

  protected age(iso: string | null): string {
    return ageText(this.i18n, iso, Date.now());
  }

  protected when(iso: string | null): string {
    return dateTimeText(this.i18n, iso);
  }

  protected levelTone(level: AgentLogLevel): 'bad' | 'warn' | 'info' | 'neutral' {
    return level === 'Error' ? 'bad' : level === 'Warning' ? 'warn' : level === 'Info' ? 'info' : 'neutral';
  }

  protected onActiveFilter(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setActiveFilter(value === '' ? null : value === 'true');
  }

  protected onPage(page: PageRequest): void {
    this.facade.setPage(page);
  }

  // ---- create (G1)

  protected openCreate(): void {
    this.createForm.reset();
    this.facade.create.reset();
    this.createOpen.set(true);
  }

  protected async create(): Promise<void> {
    if (!markAllTouched(this.createForm) || this.facade.create.pending()) {
      return;
    }

    const { name, branchId, configJson } = this.createForm.getRawValue();
    const created = await this.facade.create.execute({ name: name.trim(), branchId, configJson: configJson.trim() === '' ? null : configJson.trim() });
    if (created === null) {
      problemToFormErrors(this.facade.create.problem(), this.createForm);
      return;
    }

    this.createOpen.set(false);
    this.showSecret(created);
  }

  // ---- detail (edit, push)

  protected async openDetail(row: SiteAgentListItem): Promise<void> {
    this.facade.select(row.id);
    this.facade.update.reset();
    this.editForm.reset({ name: row.name, configJson: '' });
    this.detailOpen.set(true);
    const detail = await this.facade.getAgent(row.id);
    this.editForm.reset({ name: detail.name, configJson: prettyJson(detail.configJson) });
  }

  protected async saveDetail(): Promise<void> {
    const id = this.facade.selectedId();
    if (id === null || !markAllTouched(this.editForm) || this.facade.update.pending()) {
      return;
    }

    const { name, configJson } = this.editForm.getRawValue();
    if ((await this.facade.update.execute({ id, request: { name: name.trim(), configJson: configJson.trim() === '' ? null : configJson.trim() } })) === null) {
      problemToFormErrors(this.facade.update.problem(), this.editForm);
      return;
    }

    this.toast.success(this.i18n.t('integration.agents.saved'));
  }

  protected async push(row: { id: string; name: string }): Promise<void> {
    if ((await this.facade.push.execute(row.id)) !== null) {
      this.toast.success(this.i18n.tf('integration.agents.pushed', { name: row.name }));
    } else {
      this.toast.error(this.problemText(this.facade.push.problem()?.code ?? ''));
    }
  }

  // ---- credential (G2, G4, G5)

  protected async rotate(row: SiteAgentListItem): Promise<void> {
    const confirmed = await this.confirm.confirm({ title: this.i18n.t('integration.agents.rotate'), message: this.i18n.tf('integration.agents.confirmRotate', { name: row.name }), danger: true });
    if (!confirmed) {
      return;
    }

    const secret = await this.facade.rotate.execute(row.id);
    if (secret !== null) {
      this.showSecret(secret);
    } else {
      this.toast.error(this.problemText(this.facade.rotate.problem()?.code ?? ''));
    }
  }

  protected async toggleActive(row: SiteAgentListItem): Promise<void> {
    if (row.isActive) {
      const confirmed = await this.confirm.confirm({ title: this.i18n.t('integration.agents.deactivate'), message: this.i18n.tf('integration.agents.confirmDeactivate', { name: row.name }), danger: true });
      if (confirmed && (await this.facade.deactivate.execute(row.id)) !== null) {
        this.toast.success(this.i18n.t('integration.deactivated'));
      }

      return;
    }

    const confirmed = await this.confirm.confirm({ title: this.i18n.t('integration.agents.reactivate'), message: this.i18n.tf('integration.agents.confirmReactivate', { name: row.name }) });
    if (!confirmed) {
      return;
    }

    const secret = await this.facade.reactivate.execute(row.id);
    if (secret !== null) {
      this.showSecret(secret);
    } else {
      this.toast.error(this.problemText(this.facade.reactivate.problem()?.code ?? ''));
    }
  }

  // ---- logs

  protected openLogs(row: SiteAgentListItem): void {
    this.facade.select(row.id);
    this.logsOpen.set(true);
  }

  protected onLogLevel(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setLogFilter({ level: value === '' ? null : (value as AgentLogLevel) });
  }

  protected onLogFrom(event: Event): void {
    this.facade.setLogFilter({ from: localInputToIso((event.target as HTMLInputElement).value) });
  }

  protected onLogTo(event: Event): void {
    this.facade.setLogFilter({ to: localInputToIso((event.target as HTMLInputElement).value) });
  }

  protected onLogPage(page: PageRequest): void {
    this.facade.setLogFilter({ page: page.page, pageSize: page.pageSize });
  }

  // ---- intake

  protected onIntakeStatus(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setIntakeFilter({ status: value === '' ? null : (value as IntakeStatus) });
  }

  protected onIntakeAnalyser(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setIntakeFilter({ analyserId: value === '' ? null : value });
  }

  protected onIntakeFrom(event: Event): void {
    this.facade.setIntakeFilter({ from: localInputToIso((event.target as HTMLInputElement).value) });
  }

  protected onIntakeTo(event: Event): void {
    this.facade.setIntakeFilter({ to: localInputToIso((event.target as HTMLInputElement).value) });
  }

  protected onIntakePage(page: PageRequest): void {
    this.facade.setIntakeFilter({ page: page.page, pageSize: page.pageSize });
  }

  protected branchName(branch: { nameEn: string; nameAr: string }): string {
    return localizedPick({ en: branch.nameEn, ar: branch.nameAr }, this.i18n.lang());
  }

  protected problemText(code: string): string {
    return problemMessage(this.i18n, 'integration', { code });
  }

  private showSecret(secret: SiteAgentSecretResponse): void {
    this.secret.set(secret);
    this.secretOpen.set(true);
  }
}

/** Indented JSON for the editor; the server stores it compact. */
export function prettyJson(json: string): string {
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
}
