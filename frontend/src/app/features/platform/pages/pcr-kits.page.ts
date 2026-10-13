import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import type { PageRequest } from '../../../core/api/paging';
import { type PcrKitListItem, PLATFORM_PERMISSIONS } from '../../../core/api/platform.contracts';
import type { ProblemDetails } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { blockedSave } from '../../../shared/forms/blocked-save';
import { problemMessage } from '../../../shared/forms/problem-text';
import { problemToFormErrors } from '../../../shared/forms/problem-to-form-errors';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { requiredText } from '../../../shared/forms/validators';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { DrawerComponent } from '../../../shared/ui/drawer/drawer.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { CsvExportService } from '../data/csv-export';
import { PcrKitsFacade } from '../state/reference.facades';
import { fileStamp } from '../ui/format';
import { formatJson, jsonValidator } from '../ui/validators';

const EXAMPLE_LAYOUT = '{\n  "sheet": "Results",\n  "valueColumn": "Cq",\n  "wellColumn": "Well",\n  "targetColumn": "Target"\n}';
const EXAMPLE_THRESHOLDS = '[\n  { "dye": "FAM", "gene": "N", "positive": 38, "borderline": 40 },\n  { "dye": "HEX", "gene": "IC", "positive": 38, "borderline": 40 }\n]';
const EXAMPLE_VERDICTS = '[\n  { "when": "targetsPositive >= 2", "verdict": "DETECTED" },\n  { "when": "targetsPositive == 0 && icPositive", "verdict": "NOT DETECTED" },\n  { "when": "otherwise", "verdict": "INVALID - repeat" }\n]';

type KitForm = ReturnType<PcrKitsPage['buildForm']>;
type KitDocument = 'layoutJson' | 'thresholdsJson' | 'verdictRulesJson';

/**
 * SA-16 PCR kits (library, "as CP-62"): run-file layout, gene thresholds and verdict rules as JSON documents that are
 * checked in the browser before saving (and again by the server, LIB-003), with a "format" action per document.
 */
@Component({
  selector: 'app-platform-pcr-kits-page',
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
    FormFieldComponent,
    IconComponent,
  ],
  templateUrl: './pcr-kits.page.html',
  styleUrl: './platform-pages.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PcrKitsPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(PcrKitsFacade);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly csv = inject(CsvExportService);

  protected readonly permissions = PLATFORM_PERMISSIONS;
  protected readonly documents: readonly KitDocument[] = ['layoutJson', 'thresholdsJson', 'verdictRulesJson'];

  protected readonly columns: readonly TableColumn<PcrKitListItem>[] = [
    { key: 'name', header: 'platform.pcrKits.column.name', sortable: true },
    { key: 'analyserModelName', header: 'platform.pcrKits.column.analyserModel', sortable: true },
    { key: 'genes', header: 'platform.pcrKits.column.genes', value: (row) => genesOf(row.thresholdsJson), secondary: true },
    { key: 'status', header: 'platform.pcrKits.column.status', width: '110px' },
  ];

  protected readonly drawerOpen = signal(false);
  protected readonly editing = signal<PcrKitListItem | null>(null);
  protected readonly form = signal<KitForm | null>(null);
  protected readonly saveProblem = signal<ProblemDetails | null>(null);
  /** F-03 (validation v0.5.0): a refused save says so above the form and focuses the first marked field. */
  protected readonly blockedSave = blockedSave();
  protected readonly modelOptions = computed(() => this.facade.models.value()?.items ?? []);

  constructor() {
    void this.i18n.load('platform');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14)
  }

  protected onActive(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setActiveFilter(value === 'all' ? null : value === 'active');
  }

  protected onPage(page: PageRequest): void {
    this.facade.setPage(page);
  }

  protected openEdit(kit: PcrKitListItem | null): void {
    this.editing.set(kit);
    this.saveProblem.set(null);
    this.blockedSave.reset();
    this.form.set(this.buildForm(kit));
    this.drawerOpen.set(true);
  }

  protected format(form: KitForm, name: KitDocument): void {
    const control = form.controls[name];
    control.setValue(formatJson(control.value));
    control.markAsTouched();
  }

  protected async save(): Promise<void> {
    const form = this.form();
    if (form === null || this.facade.save.pending() || !this.blockedSave.check(form, '#platform-pcr-kit-form')) {
      return;
    }

    const value = form.getRawValue();
    this.saveProblem.set(null);
    const done = await this.facade.save.execute({
      id: this.editing()?.id ?? null,
      request: {
        name: value.name.trim(),
        analyserModelId: value.analyserModelId === '' ? null : value.analyserModelId,
        layoutJson: value.layoutJson,
        thresholdsJson: value.thresholdsJson,
        verdictRulesJson: value.verdictRulesJson,
      },
    });
    if (done !== null) {
      this.drawerOpen.set(false);
      this.toast.success(this.i18n.t('platform.pcrKits.saved'));
      return;
    }

    const problem = this.facade.save.problem();
    if (problem !== null) {
      problemToFormErrors(problem, form);
      this.saveProblem.set(problem);
    }
  }

  protected async toggle(kit: PcrKitListItem): Promise<void> {
    const key = kit.isActive ? 'deactivate' : 'activate';
    const confirmed = await this.confirm.confirm({ title: this.i18n.t(`platform.${key}`), message: this.i18n.tf(`platform.pcrKits.confirm.${key}`, { name: kit.name }), danger: kit.isActive });
    if (confirmed && (await this.facade.setActive.execute({ id: kit.id, active: !kit.isActive })) !== null) {
      this.toast.success(this.i18n.t(kit.isActive ? 'platform.deactivated' : 'platform.activated'));
    }
  }

  protected problemText(problem: ProblemDetails): string {
    return problemMessage(this.i18n, 'platform.pcrKits', problem);
  }

  protected exportCsv(): void {
    this.csv.download(`pcr-kits-${fileStamp()}`, [
      { header: this.i18n.t('platform.pcrKits.column.name'), value: (row: PcrKitListItem) => row.name },
      { header: this.i18n.t('platform.pcrKits.column.analyserModel'), value: (row) => row.analyserModelName },
      { header: this.i18n.t('platform.pcrKits.field.layoutJson'), value: (row) => row.layoutJson },
      { header: this.i18n.t('platform.pcrKits.field.thresholdsJson'), value: (row) => row.thresholdsJson },
      { header: this.i18n.t('platform.pcrKits.field.verdictRulesJson'), value: (row) => row.verdictRulesJson },
      { header: this.i18n.t('platform.active'), value: (row) => (row.isActive ? this.i18n.t('platform.yes') : this.i18n.t('platform.no')) },
    ], this.facade.rows());
  }

  private buildForm(kit: PcrKitListItem | null) {
    const json = jsonValidator((detail) => this.i18n.tf('platform.validation.json', { detail }));
    return new FormGroup({
      name: new FormControl(kit?.name ?? '', { nonNullable: true, validators: [requiredText, Validators.maxLength(150)] }),
      analyserModelId: new FormControl(kit?.analyserModelId ?? '', { nonNullable: true }),
      layoutJson: new FormControl(kit ? formatJson(kit.layoutJson) : EXAMPLE_LAYOUT, { nonNullable: true, validators: [requiredText, Validators.maxLength(20_000), json] }),
      thresholdsJson: new FormControl(kit ? formatJson(kit.thresholdsJson) : EXAMPLE_THRESHOLDS, { nonNullable: true, validators: [requiredText, Validators.maxLength(20_000), json] }),
      verdictRulesJson: new FormControl(kit ? formatJson(kit.verdictRulesJson) : EXAMPLE_VERDICTS, { nonNullable: true, validators: [requiredText, Validators.maxLength(20_000), json] }),
    });
  }
}

/** The gene names of a thresholds document (`[{ "gene": "N" }, …]`), for the list. */
function genesOf(thresholdsJson: string): string {
  try {
    const parsed: unknown = JSON.parse(thresholdsJson);
    if (!Array.isArray(parsed)) {
      return '—';
    }

    const genes = parsed
      .map((entry: unknown) => (typeof entry === 'object' && entry !== null && 'gene' in entry ? String((entry as { gene: unknown }).gene) : null))
      .filter((gene): gene is string => gene !== null);
    return genes.length > 0 ? genes.join(', ') : '—';
  } catch {
    return '—';
  }
}
