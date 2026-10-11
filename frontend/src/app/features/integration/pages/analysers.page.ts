import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormArray, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';

import {
  ANALYSER_MODES,
  CONNECTION_KINDS,
  FLAG_ACTIONS,
  INTEGRATION_PERMISSIONS,
  SERIAL_SETTINGS_PATTERN,
  TCP_ROLES,
  type AnalyserConnectionState,
  type AnalyserDetail,
  type AnalyserListItem,
  type AnalyserMode,
  type ConnectionKind,
  type FlagAction,
  type SaveAnalyserRequest,
  type TcpRole,
} from '../../../core/api/integration.contracts';
import type { PageRequest } from '../../../core/api/paging';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick, localizedTextGroup } from '../../../shared/forms/localized-text';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { problemToFormErrors } from '../../../shared/forms/problem-to-form-errors';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { UppercaseDirective } from '../../../shared/forms/uppercase.directive';
import { codeValidator } from '../../../shared/forms/validators';
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
import { AnalysersFacade } from '../state/analysers.facade';
import { ageText, integrationTone } from '../ui/integration-format';

type FlagRow = FormGroup<{ code: FormControl<string>; action: FormControl<FlagAction>; description: FormControl<string> }>;

export function analyserForm() {
  return new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(16), codeValidator] }),
    name: localizedTextGroup({}, { required: true, maxLength: 200 }),
    clinicalGroupId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    defaultSampleTypeId: new FormControl('', { nonNullable: true }),
    analyserModelId: new FormControl('', { nonNullable: true }),
    mode: new FormControl<AnalyserMode>('Unidirectional', { nonNullable: true }),
    kind: new FormControl<ConnectionKind>('Serial', { nonNullable: true }),
    serialSettings: new FormControl('9600,n,8,1', { nonNullable: true, validators: [Validators.pattern(SERIAL_SETTINGS_PATTERN)] }),
    serialPort: new FormControl('COM1', { nonNullable: true, validators: [Validators.maxLength(64)] }),
    host: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(253)] }),
    port: new FormControl<number | null>(null, { validators: [Validators.min(1), Validators.max(65535)] }),
    tcpRole: new FormControl<TcpRole>('Client', { nonNullable: true }),
    folderPath: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(260)] }),
    autoPrint: new FormControl(false, { nonNullable: true }),
    downloadOrders: new FormControl(false, { nonNullable: true }),
    appendUnregisteredTests: new FormControl(false, { nonNullable: true }),
    appendUnregisteredSamples: new FormControl(false, { nonNullable: true }),
    overwriteReviewed: new FormControl(false, { nonNullable: true }),
    overwriteVerified: new FormControl(false, { nonNullable: true }),
    printCopies: new FormControl(1, { nonNullable: true, validators: [Validators.min(0), Validators.max(10)] }),
    siteAgentId: new FormControl('', { nonNullable: true }),
    flags: new FormArray<FlagRow>([]),
  });
}

export type AnalyserForm = ReturnType<typeof analyserForm>;

/** The form's value as the API shape: only the active link kind's fields travel; blanks are nulls. */
export function toAnalyserRequest(form: AnalyserForm): SaveAnalyserRequest {
  const value = form.getRawValue();
  const blank = (text: string): string | null => (text.trim() === '' ? null : text.trim());
  return {
    code: value.code.trim(),
    nameEn: value.name.en.trim(),
    nameAr: value.name.ar.trim(),
    clinicalGroupId: value.clinicalGroupId,
    defaultSampleTypeId: blank(value.defaultSampleTypeId),
    analyserModelId: blank(value.analyserModelId),
    mode: value.mode,
    connection: {
      kind: value.kind,
      serialSettings: value.kind === 'Serial' ? blank(value.serialSettings) : null,
      serialPort: value.kind === 'Serial' ? blank(value.serialPort) : null,
      host: value.kind === 'Tcp' ? blank(value.host) : null,
      port: value.kind === 'Tcp' ? value.port : null,
      tcpRole: value.kind === 'Tcp' ? value.tcpRole : null,
      folderPath: value.kind === 'File' ? blank(value.folderPath) : null,
    },
    autoPrint: value.autoPrint,
    downloadOrders: value.downloadOrders || value.mode === 'Download',
    appendUnregisteredTests: value.appendUnregisteredTests,
    appendUnregisteredSamples: value.appendUnregisteredSamples,
    overwriteReviewed: value.overwriteReviewed,
    overwriteVerified: value.overwriteVerified,
    printCopies: value.printCopies,
    flags: value.flags.map((flag) => ({ code: flag.code.trim(), action: flag.action, description: blank(flag.description) })),
    siteAgentId: blank(value.siteAgentId),
  };
}

/**
 * Analysers (SRS CP-58): the register with link status and heartbeat age, and the editor — code, bilingual name,
 * clinical group, default sample, library model, mode, the link (serial "9600,n,8,1" + port, TCP host/port/role, or a
 * shared folder), behaviour switches, print copies, the hosting site agent and the flags table (Stop / Record). Row
 * actions: edit, test connection (last heartbeat state), mappings (CP-59), sample codes (CP-60), (de)activate.
 */
@Component({
  selector: 'app-analysers-page',
  imports: [
    ReactiveFormsModule,
    PageHeaderComponent,
    SectionCardComponent,
    ToolbarComponent,
    SearchBoxComponent,
    DataTableComponent,
    CellTemplateDirective,
    RowActionsTemplateDirective,
    PaginatorComponent,
    BadgeComponent,
    CanDirective,
    DrawerComponent,
    DialogComponent,
    FormFieldComponent,
    LocalizedTextInputComponent,
    SaveShortcutDirective,
    UppercaseDirective,
    IconComponent,
  ],
  templateUrl: './analysers.page.html',
  styleUrl: './analysers.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AnalysersPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(AnalysersFacade);
  private readonly router = inject(Router);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);

  protected readonly permissions = INTEGRATION_PERMISSIONS;
  protected readonly modes = ANALYSER_MODES;
  protected readonly kinds = CONNECTION_KINDS;
  protected readonly roles = TCP_ROLES;
  protected readonly flagActions = FLAG_ACTIONS;

  protected readonly columns: readonly TableColumn<AnalyserListItem>[] = [
    { key: 'code', header: 'integration.field.code', mono: true, width: '120px', sortable: true },
    { key: 'name', header: 'integration.field.name', sortable: true, value: (row) => localizedPick({ en: row.nameEn, ar: row.nameAr }, this.i18n.lang()) },
    { key: 'group', header: 'integration.field.clinicalGroup', secondary: true, value: (row) => localizedPick({ en: row.clinicalGroupNameEn ?? '', ar: row.clinicalGroupNameAr ?? '' }, this.i18n.lang()) },
    { key: 'mode', header: 'integration.field.mode', value: (row) => this.i18n.t(`integration.mode.${row.mode}`) },
    { key: 'agent', header: 'integration.field.siteAgent', secondary: true, value: (row) => row.siteAgentName ?? '' },
    { key: 'link', header: 'integration.field.link' },
    { key: 'mappingCount', header: 'integration.field.mappings', align: 'end', secondary: true },
    { key: 'status', header: 'integration.field.status', width: '110px' },
  ];

  protected readonly rows = computed<readonly AnalyserListItem[]>(() => (this.facade.list.hasValue() ? this.facade.list.value().items : []));
  protected readonly total = computed(() => (this.facade.list.hasValue() ? this.facade.list.value().totalCount : 0));
  protected readonly agents = computed(() => (this.facade.agents.hasValue() ? this.facade.agents.value().items : []));
  protected readonly groups = computed(() => (this.facade.clinicalGroups.hasValue() ? this.facade.clinicalGroups.value() : []));
  protected readonly sampleTypes = computed(() => (this.facade.sampleTypes.hasValue() ? this.facade.sampleTypes.value() : []));
  protected readonly models = computed(() => (this.facade.models.hasValue() ? this.facade.models.value() : []));

  protected readonly drawerOpen = signal(false);
  protected readonly editingId = signal<string | null>(null);
  protected readonly form = analyserForm();
  protected readonly kind = signal<ConnectionKind>('Serial');

  protected readonly connectionOpen = signal(false);
  protected readonly connection = signal<AnalyserConnectionState | null>(null);

  constructor() {
    void this.i18n.load('integration');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14)
  }

  protected name(option: { nameEn: string; nameAr: string }): string {
    return localizedPick({ en: option.nameEn, ar: option.nameAr }, this.i18n.lang());
  }

  protected tone = integrationTone;

  protected age(iso: string | null): string {
    return ageText(this.i18n, iso, Date.now());
  }

  protected onActiveFilter(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setActiveFilter(value === '' ? null : value === 'true');
  }

  protected onAgentFilter(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setAgent(value === '' ? null : value);
  }

  protected onPage(page: PageRequest): void {
    this.facade.setPage(page);
  }

  protected onKind(event: Event): void {
    this.kind.set((event.target as HTMLSelectElement).value as ConnectionKind);
  }

  protected onMode(event: Event): void {
    const mode = (event.target as HTMLSelectElement).value as AnalyserMode;
    if (mode === 'SharedFolder') {
      this.form.controls.kind.setValue('File');
      this.kind.set('File');
    } else if (this.form.controls.kind.value === 'File') {
      this.form.controls.kind.setValue('Serial');
      this.kind.set('Serial');
    }
  }

  protected add(): void {
    this.editingId.set(null);
    this.form.reset();
    this.form.controls.flags.clear();
    this.form.controls.code.enable();
    this.kind.set('Serial');
    this.facade.save.reset();
    this.drawerOpen.set(true);
  }

  protected async edit(row: AnalyserListItem): Promise<void> {
    const detail = await this.facade.load(row.id);
    this.fill(detail);
    this.editingId.set(row.id);
    this.facade.save.reset();
    this.drawerOpen.set(true);
  }

  protected addFlag(code = '', action: FlagAction = 'Record', description = ''): void {
    this.form.controls.flags.push(
      new FormGroup({
        code: new FormControl(code, { nonNullable: true, validators: [Validators.required, Validators.maxLength(32)] }),
        action: new FormControl<FlagAction>(action, { nonNullable: true }),
        description: new FormControl(description, { nonNullable: true, validators: [Validators.maxLength(200)] }),
      }),
    );
  }

  protected removeFlag(index: number): void {
    this.form.controls.flags.removeAt(index);
  }

  protected async save(): Promise<void> {
    if (!markAllTouched(this.form) || this.facade.save.pending()) {
      return;
    }

    const saved = await this.facade.save.execute({ id: this.editingId(), request: toAnalyserRequest(this.form) });
    if (saved === null) {
      problemToFormErrors(this.facade.save.problem(), this.form);
      return;
    }

    this.toast.success(this.i18n.t('integration.analysers.saved'));
    this.drawerOpen.set(false);
  }

  protected async toggleActive(row: AnalyserListItem): Promise<void> {
    const key = row.isActive ? 'deactivate' : 'activate';
    const confirmed = await this.confirm.confirm({
      title: this.i18n.t(`integration.analysers.${key}`),
      message: this.i18n.tf(`integration.analysers.confirm.${key}`, { code: row.code }),
      danger: row.isActive,
    });
    if (confirmed && (await this.facade.setActive.execute({ id: row.id, active: !row.isActive })) !== null) {
      this.toast.success(this.i18n.t(row.isActive ? 'integration.deactivated' : 'integration.activated'));
    }
  }

  protected async testConnection(row: AnalyserListItem): Promise<void> {
    const state = await this.facade.testConnection.execute(row.id);
    if (state !== null) {
      this.connection.set(state);
      this.connectionOpen.set(true);
    } else {
      this.toast.error(this.problemText(this.facade.testConnection.problem()?.code ?? ''));
    }
  }

  protected openMappings(row: AnalyserListItem): void {
    void this.router.navigate(['/integration/analyser-tests'], { queryParams: { analyserId: row.id } });
  }

  protected openSampleCodes(row: AnalyserListItem): void {
    void this.router.navigate(['/integration/analyser-samples'], { queryParams: { analyserId: row.id } });
  }

  protected problemText(code: string): string {
    return problemMessage(this.i18n, 'integration', { code });
  }

  private fill(detail: AnalyserDetail): void {
    this.form.reset({
      code: detail.code,
      name: { en: detail.nameEn, ar: detail.nameAr },
      clinicalGroupId: detail.clinicalGroupId,
      defaultSampleTypeId: detail.defaultSampleTypeId ?? '',
      analyserModelId: detail.analyserModelId ?? '',
      mode: detail.mode,
      kind: detail.connection.kind,
      serialSettings: detail.connection.serialSettings ?? '9600,n,8,1',
      serialPort: detail.connection.serialPort ?? 'COM1',
      host: detail.connection.host ?? '',
      port: detail.connection.port,
      tcpRole: detail.connection.tcpRole ?? 'Client',
      folderPath: detail.connection.folderPath ?? '',
      autoPrint: detail.autoPrint,
      downloadOrders: detail.downloadOrders,
      appendUnregisteredTests: detail.appendUnregisteredTests,
      appendUnregisteredSamples: detail.appendUnregisteredSamples,
      overwriteReviewed: detail.overwriteReviewed,
      overwriteVerified: detail.overwriteVerified,
      printCopies: detail.printCopies,
      siteAgentId: detail.siteAgentId ?? '',
    });
    this.form.controls.code.disable();
    this.form.controls.flags.clear();
    for (const flag of detail.flags) {
      this.addFlag(flag.code, flag.action, flag.description ?? '');
    }

    this.kind.set(detail.connection.kind);
  }
}
