import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

import { INTEGRATION_PERMISSIONS, type MappingListItem, type RangeOverrideDto, type SaveMappingRequest, type TestLookupOption } from '../../../core/api/integration.contracts';
import type { PageRequest } from '../../../core/api/paging';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { problemToFormErrors } from '../../../shared/forms/problem-to-form-errors';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { PrintService } from '../../../shared/print/print.service';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { DrawerComponent } from '../../../shared/ui/drawer/drawer.component';
import { EmptyStateComponent } from '../../../shared/ui/empty-state/empty-state.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { AnalyserTestsFacade } from '../state/analyser-tests.facade';
import { CodeSheetDocumentComponent } from '../ui/code-sheet-document.component';
import { ConversionsDialogComponent } from '../ui/conversions-dialog.component';
import { integrationTone, parseNumber } from '../ui/integration-format';
import { RangeOverrideDialogComponent } from '../ui/range-override-dialog.component';

/** The mapping editor's form (drawer). */
export function mappingForm() {
  return new FormGroup({
    testId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    analyserCode: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(32)] }),
    hostCode: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(32)] }),
    resultSelection: new FormControl(1, { nonNullable: true, validators: [Validators.required, Validators.min(1), Validators.max(99)] }),
    slope: new FormControl('1', { nonNullable: true, validators: [Validators.required] }),
    intercept: new FormControl('0', { nonNullable: true, validators: [Validators.required] }),
    unitId: new FormControl('', { nonNullable: true }),
    query: new FormControl(false, { nonNullable: true }),
    queryDetails: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
    autoSend: new FormControl(false, { nonNullable: true }),
    methodology: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
  });
}

/** The inline row editor (CP-59 "edit inline"): codes, selection, slope and intercept. */
export function inlineForm() {
  return new FormGroup({
    analyserCode: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(32)] }),
    hostCode: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(32)] }),
    resultSelection: new FormControl(1, { nonNullable: true, validators: [Validators.required, Validators.min(1), Validators.max(99)] }),
    slope: new FormControl('1', { nonNullable: true, validators: [Validators.required] }),
    intercept: new FormControl('0', { nonNullable: true, validators: [Validators.required] }),
  });
}

/** A mapping row as the save request (the row keeps its conversions and range; the edit replaces the given fields). */
export function toMappingRequest(row: MappingListItem, change: Partial<SaveMappingRequest>): SaveMappingRequest {
  return {
    testId: row.testId,
    analyserCode: row.analyserCode,
    hostCode: row.hostCode,
    resultSelection: row.resultSelection,
    slope: row.slope,
    intercept: row.intercept,
    unitId: row.unitId,
    query: row.query,
    queryDetails: row.queryDetails,
    autoSend: row.autoSend,
    conversions: row.conversions,
    rangeOverride: row.rangeOverride,
    methodology: row.methodology,
    ...change,
  };
}

/**
 * Analyser tests (SRS CP-59): per analyser, the mapping grid with inline edits (codes, result selection, slope,
 * intercept), the Auto Send switch on every row (the permission to land results — a mapping without it never lands one),
 * the conversion table and range override dialogs, the full editor with the test picker, and the printable code sheet.
 */
@Component({
  selector: 'app-analyser-tests-page',
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
    FormFieldComponent,
    SaveShortcutDirective,
    IconComponent,
    EmptyStateComponent,
    ConversionsDialogComponent,
    RangeOverrideDialogComponent,
  ],
  templateUrl: './analyser-tests.page.html',
  styleUrl: './analyser-tests.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AnalyserTestsPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(AnalyserTestsFacade);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly print = inject(PrintService);

  protected readonly permissions = INTEGRATION_PERMISSIONS;
  protected readonly tone = integrationTone;

  private readonly queryParams = toSignal(this.route.queryParamMap);

  protected readonly columns: readonly TableColumn<MappingListItem>[] = [
    { key: 'test', header: 'integration.mapping.test', value: (row) => `${row.testCode ?? ''} ${this.testName(row)}`.trim() },
    { key: 'analyserCode', header: 'integration.mapping.analyserCode', mono: true, width: '130px' },
    { key: 'hostCode', header: 'integration.mapping.hostCode', mono: true, secondary: true, width: '110px' },
    { key: 'resultSelection', header: 'integration.mapping.resultSelectionShort', align: 'end', width: '70px' },
    { key: 'slope', header: 'integration.mapping.slope', align: 'end', width: '100px' },
    { key: 'intercept', header: 'integration.mapping.intercept', align: 'end', width: '100px', secondary: true },
    { key: 'unit', header: 'integration.mapping.unit', secondary: true, value: (row) => row.unitCode ?? '' },
    { key: 'autoSend', header: 'integration.mapping.autoSend', align: 'center', width: '100px' },
    { key: 'conversions', header: 'integration.mapping.conversions', align: 'center', width: '110px' },
    { key: 'range', header: 'integration.mapping.range', secondary: true },
    { key: 'status', header: 'integration.field.status', width: '100px' },
  ];

  protected readonly analysers = computed(() => (this.facade.analysers.hasValue() ? this.facade.analysers.value().items : []));
  protected readonly rows = computed<readonly MappingListItem[]>(() => (this.facade.mappings.hasValue() ? this.facade.mappings.value().items : []));
  protected readonly total = computed(() => (this.facade.mappings.hasValue() ? this.facade.mappings.value().totalCount : 0));
  protected readonly units = computed(() => (this.facade.units.hasValue() ? this.facade.units.value() : []));

  protected readonly editingRowId = signal<string | null>(null);
  protected readonly inline = inlineForm();

  protected readonly drawerOpen = signal(false);
  protected readonly editingId = signal<string | null>(null);
  protected readonly form = mappingForm();
  protected readonly testSearch = signal('');
  protected readonly testOptions = signal<readonly TestLookupOption[]>([]);
  protected readonly selectedTest = signal<string>('');

  protected readonly dialogRow = signal<MappingListItem | null>(null);
  protected readonly conversionsOpen = signal(false);
  protected readonly rangeOpen = signal(false);

  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    void this.i18n.load('integration');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14)
    effect(() => {
      const id = this.queryParams()?.get('analyserId') ?? null;
      if (id !== this.facade.analyserId()) {
        this.facade.selectAnalyser(id);
        this.editingRowId.set(null);
      }
    });
    inject(DestroyRef).onDestroy(() => {
      if (this.searchTimer !== null) {
        clearTimeout(this.searchTimer);
      }
    });
  }

  protected testName(row: MappingListItem): string {
    return localizedPick({ en: row.testNameEn ?? '', ar: row.testNameAr ?? '' }, this.i18n.lang());
  }

  protected optionName(option: { nameEn: string; nameAr: string }): string {
    return localizedPick({ en: option.nameEn, ar: option.nameAr }, this.i18n.lang());
  }

  protected rangeText(row: MappingListItem): string {
    const range = row.rangeOverride;
    if (range === null) {
      return '';
    }

    const bounds = range.low !== null || range.high !== null ? `${range.low ?? ''} – ${range.high ?? ''}` : '';
    return [bounds, range.text ?? ''].filter((part) => part !== '').join(' · ');
  }

  protected onAnalyser(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    void this.router.navigate([], { relativeTo: this.route, queryParams: { analyserId: value === '' ? null : value }, queryParamsHandling: 'merge' });
  }

  protected onPage(page: PageRequest): void {
    this.facade.setPage(page);
  }

  // ---- inline edit

  protected startInline(row: MappingListItem): void {
    this.inline.reset({ analyserCode: row.analyserCode, hostCode: row.hostCode ?? '', resultSelection: row.resultSelection, slope: String(row.slope), intercept: String(row.intercept) });
    this.facade.save.reset();
    this.editingRowId.set(row.id);
  }

  protected cancelInline(): void {
    this.editingRowId.set(null);
  }

  protected async saveInline(row: MappingListItem): Promise<void> {
    const value = this.inline.getRawValue();
    const slope = parseNumber(value.slope);
    const intercept = parseNumber(value.intercept);
    if (!markAllTouched(this.inline) || slope === null || slope === 0 || intercept === null) {
      this.toast.error(this.i18n.t('integration.mapping.invalidNumbers'));
      return;
    }

    const change: Partial<SaveMappingRequest> = {
      analyserCode: value.analyserCode.trim(),
      hostCode: value.hostCode.trim() === '' ? null : value.hostCode.trim(),
      resultSelection: value.resultSelection,
      slope,
      intercept,
    };
    if ((await this.facade.save.execute({ mappingId: row.id, request: toMappingRequest(row, change) })) !== null) {
      this.editingRowId.set(null);
      this.toast.success(this.i18n.t('integration.mapping.saved'));
    }
  }

  protected onInlineKey(event: KeyboardEvent, row: MappingListItem): void {
    if (event.key === 'Enter') {
      event.preventDefault();
      void this.saveInline(row);
    } else if (event.key === 'Escape') {
      event.stopPropagation();
      this.cancelInline();
    }
  }

  protected async toggleAutoSend(row: MappingListItem): Promise<void> {
    const enabling = !row.autoSend;
    if (!enabling) {
      const confirmed = await this.confirm.confirm({
        title: this.i18n.t('integration.mapping.autoSendOffTitle'),
        message: this.i18n.tf('integration.mapping.autoSendOffMessage', { code: row.analyserCode }),
        danger: true,
      });
      if (!confirmed) {
        return;
      }
    }

    if ((await this.facade.autoSend.execute({ mappingId: row.id, enabled: enabling })) !== null) {
      this.toast.success(this.i18n.t(enabling ? 'integration.mapping.autoSendOn' : 'integration.mapping.autoSendOff'));
    }
  }

  // ---- drawer editor

  protected add(): void {
    this.editingId.set(null);
    this.form.reset();
    this.selectedTest.set('');
    this.testSearch.set('');
    this.testOptions.set([]);
    this.facade.save.reset();
    this.drawerOpen.set(true);
  }

  protected edit(row: MappingListItem): void {
    this.editingId.set(row.id);
    this.form.reset({
      testId: row.testId,
      analyserCode: row.analyserCode,
      hostCode: row.hostCode ?? '',
      resultSelection: row.resultSelection,
      slope: String(row.slope),
      intercept: String(row.intercept),
      unitId: row.unitId ?? '',
      query: row.query,
      queryDetails: row.queryDetails ?? '',
      autoSend: row.autoSend,
      methodology: row.methodology ?? '',
    });
    this.testOptions.set([{ id: row.testId, code: row.testCode ?? '', nameEn: row.testNameEn ?? '', nameAr: row.testNameAr ?? '', clinicalGroupId: null, type: '' }]);
    this.selectedTest.set(row.testId);
    this.facade.save.reset();
    this.drawerOpen.set(true);
  }

  protected onTestSearch(event: Event): void {
    const text = (event.target as HTMLInputElement).value;
    this.testSearch.set(text);
    if (this.searchTimer !== null) {
      clearTimeout(this.searchTimer);
    }

    this.searchTimer = setTimeout(() => void this.runTestSearch(text), 300);
  }

  protected chooseTest(event: Event): void {
    const id = (event.target as HTMLSelectElement).value;
    this.selectedTest.set(id);
    this.form.controls.testId.setValue(id);
    this.form.controls.testId.markAsTouched();
  }

  protected async save(): Promise<void> {
    const value = this.form.getRawValue();
    const slope = parseNumber(value.slope);
    const intercept = parseNumber(value.intercept);
    if (!markAllTouched(this.form) || this.facade.save.pending()) {
      return;
    }

    if (slope === null || slope === 0 || intercept === null) {
      this.toast.error(this.i18n.t('integration.mapping.invalidNumbers'));
      return;
    }

    const existing = this.rows().find((row) => row.id === this.editingId());
    const blank = (text: string): string | null => (text.trim() === '' ? null : text.trim());
    const request: SaveMappingRequest = {
      testId: value.testId,
      analyserCode: value.analyserCode.trim(),
      hostCode: blank(value.hostCode),
      resultSelection: value.resultSelection,
      slope,
      intercept,
      unitId: blank(value.unitId),
      query: value.query,
      queryDetails: blank(value.queryDetails),
      autoSend: value.autoSend,
      conversions: existing?.conversions ?? [],
      rangeOverride: existing?.rangeOverride ?? null,
      methodology: blank(value.methodology),
    };

    const saved = await this.facade.save.execute({ mappingId: this.editingId(), request });
    if (saved === null) {
      problemToFormErrors(this.facade.save.problem(), this.form);
      return;
    }

    this.toast.success(this.i18n.t('integration.mapping.saved'));
    this.drawerOpen.set(false);
  }

  // ---- dialogs

  protected openConversions(row: MappingListItem): void {
    this.facade.conversions.reset();
    this.dialogRow.set(row);
    this.conversionsOpen.set(true);
  }

  protected async saveConversions(conversions: readonly { fromValue: string; toValue: string }[]): Promise<void> {
    const row = this.dialogRow();
    if (row !== null && (await this.facade.conversions.execute({ mappingId: row.id, conversions })) !== null) {
      this.conversionsOpen.set(false);
      this.toast.success(this.i18n.t('integration.conversions.saved'));
    }
  }

  protected openRange(row: MappingListItem): void {
    this.facade.range.reset();
    this.dialogRow.set(row);
    this.rangeOpen.set(true);
  }

  protected async saveRange(range: RangeOverrideDto | null): Promise<void> {
    const row = this.dialogRow();
    if (row !== null && (await this.facade.range.execute({ mappingId: row.id, range })) !== null) {
      this.rangeOpen.set(false);
      this.toast.success(this.i18n.t('integration.range.saved'));
    }
  }

  protected async toggleActive(row: MappingListItem): Promise<void> {
    const key = row.isActive ? 'deactivate' : 'activate';
    const confirmed = await this.confirm.confirm({ title: this.i18n.t(`integration.mapping.${key}`), message: this.i18n.tf(`integration.mapping.confirm.${key}`, { code: row.analyserCode }), danger: row.isActive });
    if (confirmed && (await this.facade.setMappingActive.execute({ mappingId: row.id, active: !row.isActive })) !== null) {
      this.toast.success(this.i18n.t(row.isActive ? 'integration.deactivated' : 'integration.activated'));
    }
  }

  protected async printCodeSheet(): Promise<void> {
    const analyserId = this.facade.analyserId();
    if (analyserId === null) {
      return;
    }

    const doc = await this.facade.codeSheet.execute(analyserId);
    if (doc === null) {
      this.toast.error(this.problemText(this.facade.codeSheet.problem()?.code ?? ''));
      return;
    }

    await this.print.print({
      title: `${this.i18n.t('integration.codeSheet.title')} ${doc.analyserCode}`,
      size: 'A4',
      component: { type: CodeSheetDocumentComponent, inputs: { doc } },
      dir: this.i18n.dir(),
      lang: this.i18n.lang(),
    });
  }

  protected problemText(code: string): string {
    return problemMessage(this.i18n, 'integration', { code });
  }

  private async runTestSearch(text: string): Promise<void> {
    if (text.trim().length < 2) {
      return;
    }

    try {
      this.testOptions.set(await this.facade.searchTests(text.trim()));
    } catch {
      this.testOptions.set([]);
    }
  }
}
