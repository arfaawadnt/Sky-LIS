import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormArray, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import { INTEGRATION_PERMISSIONS, type PcrKitDetail, type PcrKitListItem, type PcrRunView, type SavePcrKitRequest } from '../../../core/api/integration.contracts';
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
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { DrawerComponent } from '../../../shared/ui/drawer/drawer.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { PcrKitsFacade } from '../state/pcr-kits.facade';
import { integrationTone, parseNumber } from '../ui/integration-format';

type GeneRow = FormGroup<{ name: FormControl<string>; isInternalControl: FormControl<boolean>; positiveCtMax: FormControl<string>; borderlineCtMax: FormControl<string> }>;

export const DELIMITERS: readonly { readonly value: string; readonly label: string }[] = [
  { value: ',', label: ',' },
  { value: ';', label: ';' },
  { value: '\t', label: 'TAB' },
  { value: '|', label: '|' },
];

export function kitForm() {
  return new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] }),
    analyserId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    analyserTestCode: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(32)] }),
    serialLock: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(64)] }),
    delimiter: new FormControl(',', { nonNullable: true }),
    sampleColumn: new FormControl('Sample', { nonNullable: true, validators: [Validators.required, Validators.maxLength(64)] }),
    geneColumn: new FormControl('Target', { nonNullable: true, validators: [Validators.required, Validators.maxLength(64)] }),
    valueColumn: new FormControl('Cq', { nonNullable: true, validators: [Validators.required, Validators.maxLength(64)] }),
    wellColumn: new FormControl('Well', { nonNullable: true, validators: [Validators.maxLength(64)] }),
    contentColumn: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(64)] }),
    patientContentValue: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(64)] }),
    serialLabel: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(100)] }),
    noAmplificationTokens: new FormControl('N/A, Undetermined', { nonNullable: true, validators: [Validators.maxLength(250)] }),
    detectedMinPositiveTargets: new FormControl(1, { nonNullable: true, validators: [Validators.required, Validators.min(1), Validators.max(10)] }),
    detectedText: new FormControl('DETECTED', { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] }),
    notDetectedText: new FormControl('NOT DETECTED', { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] }),
    presumptiveText: new FormControl('Presumptive positive', { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] }),
    invalidText: new FormControl('INVALID', { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] }),
    resultFolder: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(260)] }),
    backupFolder: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(260)] }),
    failedFolder: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(260)] }),
    genes: new FormArray<GeneRow>([]),
  });
}

export type KitForm = ReturnType<typeof kitForm>;

export function geneRow(name = '', isInternalControl = false, positive = '38', borderline = '40'): GeneRow {
  return new FormGroup({
    name: new FormControl(name, { nonNullable: true, validators: [Validators.required, Validators.maxLength(64)] }),
    isInternalControl: new FormControl(isInternalControl, { nonNullable: true }),
    positiveCtMax: new FormControl(positive, { nonNullable: true, validators: [Validators.required] }),
    borderlineCtMax: new FormControl(borderline, { nonNullable: true, validators: [Validators.required] }),
  });
}

/** The form as the API shape, or null when a threshold is not a number (the server checks 0 < positive ≤ borderline ≤ 60). */
export function toKitRequest(form: KitForm): SavePcrKitRequest | null {
  const value = form.getRawValue();
  const blank = (text: string): string | null => (text.trim() === '' ? null : text.trim());
  const genes = value.genes.map((gene) => ({ name: gene.name.trim(), isInternalControl: gene.isInternalControl, positiveCtMax: parseNumber(gene.positiveCtMax), borderlineCtMax: parseNumber(gene.borderlineCtMax) }));
  if (genes.some((gene) => gene.positiveCtMax === null || gene.borderlineCtMax === null)) {
    return null;
  }

  return {
    name: value.name.trim(),
    analyserId: value.analyserId,
    analyserTestCode: value.analyserTestCode.trim(),
    serialLock: blank(value.serialLock),
    layout: {
      delimiter: value.delimiter,
      sampleColumn: value.sampleColumn.trim(),
      geneColumn: value.geneColumn.trim(),
      wellColumn: blank(value.wellColumn),
      contentColumn: blank(value.contentColumn),
      patientContentValue: blank(value.patientContentValue),
      serialLabel: blank(value.serialLabel),
      noAmplificationTokens: value.noAmplificationTokens.split(',').map((token) => token.trim()).filter((token) => token !== ''),
    },
    valueColumn: value.valueColumn.trim(),
    genes: genes.map((gene) => ({ name: gene.name, isInternalControl: gene.isInternalControl, positiveCtMax: gene.positiveCtMax ?? 0, borderlineCtMax: gene.borderlineCtMax ?? 0 })),
    rules: {
      detectedMinPositiveTargets: value.detectedMinPositiveTargets,
      detectedText: value.detectedText.trim(),
      notDetectedText: value.notDetectedText.trim(),
      presumptiveText: value.presumptiveText.trim(),
      invalidText: value.invalidText.trim(),
    },
    resultFolder: value.resultFolder.trim(),
    backupFolder: value.backupFolder.trim(),
    failedFolder: value.failedFolder.trim(),
  };
}

/**
 * PCR kits (SRS CP-62; LDM manual §14): the kit on its instrument with the serial lock, the run-file layout and value
 * column, the genes with their positive/borderline Ct thresholds (a low Ct is positive), the verdict rules and texts, the
 * analyser test code the verdict lands through, and the result/backup/failed folders. "Simulate run": paste a run file,
 * see each sample's readings and verdict, or why the file would be moved to the failed folder.
 */
@Component({
  selector: 'app-pcr-kits-page',
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
    SaveShortcutDirective,
    IconComponent,
  ],
  templateUrl: './pcr-kits.page.html',
  styleUrl: './pcr-kits.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PcrKitsPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(PcrKitsFacade);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);

  protected readonly permissions = INTEGRATION_PERMISSIONS;
  protected readonly delimiters = DELIMITERS;
  protected readonly tone = integrationTone;

  protected readonly columns: readonly TableColumn<PcrKitListItem>[] = [
    { key: 'name', header: 'integration.field.name', sortable: true },
    { key: 'analyser', header: 'integration.kits.instrument', value: (row) => `${row.analyserCode ?? ''} ${localizedPick({ en: row.analyserNameEn ?? '', ar: row.analyserNameAr ?? '' }, this.i18n.lang())}`.trim() },
    { key: 'analyserTestCode', header: 'integration.kits.testCode', mono: true, width: '120px' },
    { key: 'serialLock', header: 'integration.kits.serialLock', mono: true, secondary: true, value: (row) => row.serialLock ?? '' },
    { key: 'geneCount', header: 'integration.kits.genes', align: 'end', width: '80px', secondary: true },
    { key: 'status', header: 'integration.field.status', width: '110px' },
  ];

  protected readonly rows = computed<readonly PcrKitListItem[]>(() => (this.facade.list.hasValue() ? this.facade.list.value().items : []));
  protected readonly total = computed(() => (this.facade.list.hasValue() ? this.facade.list.value().totalCount : 0));
  protected readonly analysers = computed(() => (this.facade.analysers.hasValue() ? this.facade.analysers.value().items : []));

  protected readonly drawerOpen = signal(false);
  protected readonly editingId = signal<string | null>(null);
  protected readonly form = kitForm();

  protected readonly simulateOpen = signal(false);
  protected readonly simulateKit = signal<PcrKitListItem | null>(null);
  protected readonly simulation = signal<PcrRunView | null>(null);
  protected readonly simulateForm = new FormGroup({
    instrumentSerial: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(64)] }),
    content: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(2 * 1024 * 1024)] }),
  });

  constructor() {
    void this.i18n.load('integration');
  }

  protected analyserName(option: { nameEn: string; nameAr: string }): string {
    return localizedPick({ en: option.nameEn, ar: option.nameAr }, this.i18n.lang());
  }

  protected onActiveFilter(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setActiveFilter(value === '' ? null : value === 'true');
  }

  protected onPage(page: PageRequest): void {
    this.facade.setPage(page);
  }

  protected add(): void {
    this.editingId.set(null);
    this.form.reset();
    this.form.controls.genes.clear();
    for (const [name, control] of [['ORF1a', false], ['RdRp', false], ['N Gene', false], ['IC', true]] as const) {
      this.form.controls.genes.push(geneRow(name, control, control ? '40' : '38', '40'));
    }

    this.facade.save.reset();
    this.drawerOpen.set(true);
  }

  protected async edit(row: PcrKitListItem): Promise<void> {
    this.fill(await this.facade.load(row.id));
    this.editingId.set(row.id);
    this.facade.save.reset();
    this.drawerOpen.set(true);
  }

  protected addGene(): void {
    this.form.controls.genes.push(geneRow());
  }

  protected removeGene(index: number): void {
    this.form.controls.genes.removeAt(index);
  }

  protected async save(): Promise<void> {
    if (!markAllTouched(this.form) || this.facade.save.pending()) {
      return;
    }

    const request = toKitRequest(this.form);
    if (request === null) {
      this.toast.error(this.i18n.t('integration.kits.thresholdsInvalid'));
      return;
    }

    if ((await this.facade.save.execute({ id: this.editingId(), request })) === null) {
      problemToFormErrors(this.facade.save.problem(), this.form);
      return;
    }

    this.toast.success(this.i18n.t('integration.kits.saved'));
    this.drawerOpen.set(false);
  }

  protected async toggleActive(row: PcrKitListItem): Promise<void> {
    const key = row.isActive ? 'deactivate' : 'activate';
    const confirmed = await this.confirm.confirm({ title: this.i18n.t(`integration.kits.${key}`), message: this.i18n.tf(`integration.kits.confirm.${key}`, { name: row.name }), danger: row.isActive });
    if (confirmed && (await this.facade.setActive.execute({ id: row.id, active: !row.isActive })) !== null) {
      this.toast.success(this.i18n.t(row.isActive ? 'integration.deactivated' : 'integration.activated'));
    }
  }

  protected openSimulate(row: PcrKitListItem): void {
    this.simulateKit.set(row);
    this.simulation.set(null);
    this.simulateForm.reset({ instrumentSerial: row.serialLock ?? '', content: '' });
    this.facade.simulate.reset();
    this.simulateOpen.set(true);
  }

  protected async runSimulation(): Promise<void> {
    const kit = this.simulateKit();
    if (kit === null || !markAllTouched(this.simulateForm) || this.facade.simulate.pending()) {
      return;
    }

    const { instrumentSerial, content } = this.simulateForm.getRawValue();
    this.simulation.set(await this.facade.simulate.execute({ id: kit.id, request: { instrumentSerial: instrumentSerial.trim() === '' ? null : instrumentSerial.trim(), content } }));
  }

  protected async loadFile(event: Event): Promise<void> {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (file !== undefined) {
      this.simulateForm.controls.content.setValue(await file.text());
    }
  }

  protected verdictTone(verdict: string | null): 'bad' | 'ok' | 'warn' | 'neutral' {
    return verdict === 'Detected' ? 'bad' : verdict === 'NotDetected' ? 'ok' : verdict === 'Presumptive' ? 'warn' : 'neutral';
  }

  protected callTone(call: string): 'bad' | 'warn' | 'neutral' {
    return call === 'Positive' ? 'bad' : call === 'Borderline' ? 'warn' : 'neutral';
  }

  protected problemText(code: string): string {
    return problemMessage(this.i18n, 'integration', { code });
  }

  private fill(kit: PcrKitDetail): void {
    this.form.reset({
      name: kit.name,
      analyserId: kit.analyserId,
      analyserTestCode: kit.analyserTestCode,
      serialLock: kit.serialLock ?? '',
      delimiter: kit.layout.delimiter,
      sampleColumn: kit.layout.sampleColumn,
      geneColumn: kit.layout.geneColumn,
      valueColumn: kit.valueColumn,
      wellColumn: kit.layout.wellColumn ?? '',
      contentColumn: kit.layout.contentColumn ?? '',
      patientContentValue: kit.layout.patientContentValue ?? '',
      serialLabel: kit.layout.serialLabel ?? '',
      noAmplificationTokens: kit.layout.noAmplificationTokens.join(', '),
      detectedMinPositiveTargets: kit.rules.detectedMinPositiveTargets,
      detectedText: kit.rules.detectedText,
      notDetectedText: kit.rules.notDetectedText,
      presumptiveText: kit.rules.presumptiveText,
      invalidText: kit.rules.invalidText,
      resultFolder: kit.resultFolder,
      backupFolder: kit.backupFolder,
      failedFolder: kit.failedFolder,
    });
    this.form.controls.genes.clear();
    for (const gene of kit.genes) {
      this.form.controls.genes.push(geneRow(gene.name, gene.isInternalControl, String(gene.positiveCtMax), String(gene.borderlineCtMax)));
    }
  }
}
