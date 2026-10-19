import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import type { PageRequest } from '../../../core/api/paging';
import {
  type AdjustBasePriceListRequest,
  type AdjustBasePriceListResult,
  type CopyBasePriceListRequest,
  type ImportPriceLinesResult,
  type ImportPriceRowRequest,
  PRICING_PERMISSIONS,
  type ServiceLabel,
} from '../../../core/api/pricing.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { displayNameFor } from '../../../core/auth/display-name';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { inReleaseScope } from '../../../core/release';
import { localizedPick } from '../../../shared/forms/localized-text';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { PrintService } from '../../../shared/print/print.service';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import { type TableColumn, type TableSort, sortRows } from '../../../shared/ui/data-table/data-table.types';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { type Breadcrumb, PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { csvFileName, downloadBlob, downloadCsv, toCsv } from '../data/csv';
import { buildPriceWorkbook, xlsxFileName } from '../data/excel';
import { formatAmount } from '../data/money';
import { PricingApi, takenCodes } from '../data/pricing.api';
import { ServiceCatalogApi } from '../data/service-catalog.api';
import { type EditorRow, PriceListEditorFacade } from '../state/price-list-editor.facade';
import { AdjustPricesDialogComponent } from '../ui/adjust-prices-dialog.component';
import { CopyListDialogComponent, copyCodeStem } from '../ui/copy-list-dialog.component';
import { type PriceListLabels, PriceListDocumentComponent, type PrintedPriceLine } from '../ui/documents/price-list-document.component';
import { ImportPricesDialogComponent } from '../ui/import-prices-dialog.component';
import { ServicePickerComponent } from '../ui/service-picker.component';

const PAGE_SIZE = 50;

/**
 * The price grid of one base list (SRS CP-33): inline price and active flag per service, new services through the
 * catalogue typeahead (Enter adds), Ctrl+S saves every changed line in one call, copy into next year's list, Apply to
 * the derived contracts (BR-07, confirmed), activation, CSV export. Leaving with unsaved changes asks first.
 * Tools (CR 2026-10-19 C21): Download Excel (the lines with an empty "New price" column), Upload Excel (parsed in the
 * browser, checked by the server as a dry run, applied all-or-nothing), Bulk adjust (preview, then apply) and Print.
 */
@Component({
  selector: 'app-price-list-editor-page',
  imports: [
    RouterLink,
    SaveShortcutDirective,
    CanDirective,
    PageHeaderComponent,
    SectionCardComponent,
    ToolbarComponent,
    SearchBoxComponent,
    DataTableComponent,
    CellTemplateDirective,
    PaginatorComponent,
    BadgeComponent,
    IconComponent,
    ServicePickerComponent,
    CopyListDialogComponent,
    ImportPricesDialogComponent,
    AdjustPricesDialogComponent,
  ],
  templateUrl: './price-list-editor.page.html',
  styleUrl: './price-list-editor.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PriceListEditorPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(PriceListEditorFacade);
  private readonly catalog = inject(ServiceCatalogApi);
  private readonly api = inject(PricingApi);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly permissionsService = inject(PermissionsService);

  /** Route parameter (`withComponentInputBinding`). */
  readonly listId = input.required<string>();

  protected readonly permissions = PRICING_PERMISSIONS;

  /** Derived contracts link to contract pricing (CP-34) only when that Release-2 page is in scope (UAT R1-03). */
  protected readonly contractPricingOffered = inReleaseScope('R2');
  protected readonly page = signal<PageRequest>({ page: 1, pageSize: PAGE_SIZE });
  protected readonly copyOpen = signal(false);
  /** Existing codes like the proposed copy code, read before the dialog opens (UAT B-06). */
  protected readonly copyTaken = signal<ReadonlySet<string>>(new Set<string>());

  // ---- tools (C21) ----
  private readonly printer = inject(PrintService);
  private readonly auth = inject(AuthStore);
  protected readonly importOpen = signal(false);
  protected readonly adjustOpen = signal(false);
  protected readonly downloading = signal(false);
  protected readonly printing = signal(false);
  /** The dry run's or the import's answer shown by the dialog (reset when it opens). */
  protected readonly importResult = signal<ImportPriceLinesResult | null>(null);
  /** The preview's or the apply's answer shown by the dialog (reset when it opens). */
  protected readonly adjustResult = signal<AdjustBasePriceListResult | null>(null);

  /** The ids of the rows the search box keeps: the "filtered rows" scope of the bulk adjustment. */
  protected readonly filteredIds = computed<readonly string[]>(() => this.facade.visibleRows().filter((row) => !row.isNew).map((row) => row.serviceId));
  protected readonly filterActive = computed(() => this.facade.search().trim() !== '');

  protected readonly detail = computed(() => (this.facade.detail.hasValue() ? this.facade.detail.value() : null));

  protected readonly title = computed(() => {
    const detail = this.detail();
    return detail ? `${detail.code} · ${localizedPick({ en: detail.nameEn, ar: detail.nameAr }, this.i18n.lang())}` : this.i18n.t('pricing.lists.title');
  });

  protected readonly crumbs = computed<readonly Breadcrumb[]>(() => [
    { label: this.i18n.t('pricing.lists.title'), path: '/pricing/base-price-lists' },
    { label: this.detail()?.code ?? '…', path: null },
  ]);

  protected readonly columns: readonly TableColumn<EditorRow>[] = [
    { key: 'code', header: 'pricing.grid.code', mono: true, width: '120px', sortable: true, value: (row) => this.facade.names.code(row.serviceId) },
    { key: 'service', header: 'pricing.grid.service', sortable: true, value: (row) => this.facade.names.name(row.serviceId, this.i18n.lang()) },
    { key: 'price', header: 'pricing.grid.price', align: 'end', width: '160px', sortable: true, value: (row) => row.price },
    { key: 'active', header: 'pricing.grid.activeAtOrdering', align: 'center', width: '130px' },
    { key: 'state', header: 'pricing.grid.state', width: '110px', secondary: true },
  ];

  /** The column order chosen in the header: the grid holds every line, so the whole list is sorted before the page is cut (B-03). */
  protected readonly sort = signal<TableSort | null>(null);

  protected readonly pagedRows = computed(() => {
    const { page, pageSize } = this.page();
    return sortRows(this.facade.visibleRows(), this.columns, this.sort(), this.i18n.lang()).slice((page - 1) * pageSize, page * pageSize);
  });

  /** Inline inputs are read-only without pricing.edit (affordance; the server decides). */
  protected readonly canEdit = computed(() => this.permissionsService.can(PRICING_PERMISSIONS.edit));

  protected readonly rowClass = (row: EditorRow): string | null => (row.invalid && row.dirty ? 'invalid-row' : row.isActive ? null : 'inactive');

  protected readonly searchService = (term: string): Promise<readonly ServiceLabel[]> => this.catalog.search(term);

  constructor() {
    void this.i18n.load('pricing');
    effect(() => {
      const id = this.listId();
      untracked(() => this.facade.load(id));
    });
  }

  /**
   * Every change of the search box (typing, clear, Escape) is the grid's filter at once: the rows are filtered in the
   * browser, and the box is bound to the facade's filter, so the grid never applies a filter the box does not show (UAT B-05).
   */
  /** A header click sorts every line (the table shows the slice as given) and goes back to page 1. */
  protected onSort(sort: TableSort | null): void {
    this.sort.set(sort);
    this.page.update((current) => ({ ...current, page: 1 }));
  }

  protected onSearch(term: string): void {
    if (term === this.facade.search()) {
      return;
    }

    this.facade.search.set(term);
    this.page.update((current) => ({ ...current, page: 1 }));
  }

  protected onPrice(row: EditorRow, event: Event): void {
    const value = (event.target as HTMLInputElement).valueAsNumber;
    this.facade.setPrice(row.serviceId, Number.isNaN(value) ? -1 : value);
  }

  protected onActive(row: EditorRow, event: Event): void {
    this.facade.setLineActive(row.serviceId, (event.target as HTMLInputElement).checked);
  }

  protected onPicked(service: ServiceLabel): void {
    if (this.facade.addService(service)) {
      this.facade.search.set('');
      this.page.update((current) => ({ ...current, page: 1 }));
      this.toast.info(this.i18n.tf('pricing.editor.added', { code: service.code || service.nameEn }));
    } else {
      // shows the line by filtering on it; the search box shows the same text, so clearing it brings every line back (UAT B-05)
      this.onSearch(service.code || service.nameEn);
      this.toast.info(this.i18n.t('pricing.editor.alreadyOnList'));
    }
  }

  protected async save(): Promise<void> {
    const listId = this.facade.listId();
    const lines = this.facade.pendingLines();
    if (listId === null || lines.length === 0 || this.facade.save.pending()) {
      return;
    }

    if (this.facade.invalidCount() > 0) {
      this.toast.error(this.i18n.t('pricing.editor.fixInvalid'));
      return;
    }

    const result = await this.facade.save.execute({ listId, lines });
    if (result !== null) {
      this.toast.success(this.i18n.tf('pricing.editor.saved', { added: result.added, changed: result.changed }), { title: this.i18n.t('pricing.editor.applyReminder') });
    }
  }

  protected async discard(): Promise<void> {
    if (this.facade.dirtyRows().length === 0) {
      return;
    }

    if (await this.confirm.confirm({ title: this.i18n.t('pricing.editor.discard'), message: this.i18n.t('pricing.editor.confirmDiscard'), danger: true })) {
      this.facade.discard();
    }
  }

  protected async toggleActive(): Promise<void> {
    const detail = this.detail();
    if (!detail) {
      return;
    }

    // both directions ask first: activating changes what cash patients pay (validation O-1), deactivating stops it
    const confirmed = await this.confirm.confirm(
      detail.isActive
        ? { title: this.i18n.t('pricing.lists.deactivate'), message: this.i18n.tf('pricing.editor.confirmDeactivate', { code: detail.code }), danger: true }
        : { title: this.i18n.t('pricing.lists.confirmActivate.title'), message: this.i18n.tf('pricing.editor.confirmActivate', { code: detail.code }), confirmLabel: this.i18n.t('pricing.lists.activate') },
    );
    if (!confirmed) {
      return;
    }

    if ((await this.facade.setActive.execute({ listId: detail.id, active: !detail.isActive })) !== null) {
      this.toast.success(this.i18n.t(detail.isActive ? 'pricing.lists.deactivated' : 'pricing.lists.activated'));
    }
  }

  protected async apply(): Promise<void> {
    const detail = this.detail();
    if (!detail) {
      return;
    }

    if (this.facade.dirtyRows().length > 0) {
      this.toast.warn(this.i18n.t('pricing.editor.saveBeforeApply'));
      return;
    }

    const confirmed = await this.confirm.confirm({
      title: this.i18n.t('pricing.editor.apply'),
      message: this.i18n.tc('pricing.editor.confirmApply', detail.derivedContracts.length, { code: detail.code }),
      confirmLabel: this.i18n.t('pricing.editor.apply'),
    });
    if (!confirmed) {
      return;
    }

    const result = await this.facade.apply.execute(detail.id);
    if (result !== null) {
      this.toast.success(this.i18n.tf('pricing.editor.applied', { contracts: this.i18n.tc('pricing.editor.appliedContracts', result.contractsApplied), prices: this.i18n.tc('pricing.editor.appliedPrices', result.pricesChanged) }));
    }
  }

  protected async openCopy(): Promise<void> {
    const detail = this.detail();
    if (detail === null) {
      return;
    }

    this.facade.copy.reset();
    this.copyTaken.set(await takenCodes(this.api, copyCodeStem(detail)));
    this.copyOpen.set(true);
  }

  protected async runCopy(request: CopyBasePriceListRequest): Promise<void> {
    const listId = this.facade.listId();
    if (listId === null) {
      return;
    }

    const created = await this.facade.copy.execute({ listId, request });
    if (created !== null) {
      this.copyOpen.set(false);
      this.toast.success(this.i18n.tf('pricing.copy.done', { code: request.code.toUpperCase() }));
      void this.router.navigate(['/pricing/base-price-lists', created.id]);
    }
  }

  protected exportCsv(): void {
    const detail = this.detail();
    const lines = this.facade.lines.hasValue() ? this.facade.lines.value() : [];
    if (!detail) {
      return;
    }

    const lang = this.i18n.lang();
    const header = [this.i18n.t('pricing.grid.code'), this.i18n.t('pricing.grid.service'), this.i18n.t('pricing.grid.price'), this.i18n.t('pricing.grid.activeAtOrdering')];
    const rows = lines.map((line) => [this.facade.names.code(line.serviceId), this.facade.names.name(line.serviceId, lang), line.price, this.i18n.t(line.isActive ? 'shared.yes' : 'shared.no')]);
    downloadCsv(csvFileName(`${detail.code}-prices`, new Date()), toCsv(header, rows));
    this.toast.success(this.i18n.t('pricing.exported'));
  }

  // ---- tools (C21) ----

  /** Download Excel: the list's lines (code, name, current price, empty "New price") in a sheet named after the list code. */
  protected async downloadExcel(): Promise<void> {
    const detail = this.detail();
    const lines = this.facade.lines.hasValue() ? this.facade.lines.value() : null;
    if (detail === null || lines === null || this.downloading()) {
      return;
    }

    this.downloading.set(true);
    try {
      const lang = this.i18n.lang();
      const blob = await buildPriceWorkbook(detail.code, lines.map((line) => ({ code: this.facade.names.code(line.serviceId), name: this.facade.names.name(line.serviceId, lang), price: line.price })));
      downloadBlob(xlsxFileName(`${detail.code}-prices`, new Date()), blob);
      this.toast.success(this.i18n.t('pricing.tools.excelDownloaded'));
    } catch {
      this.toast.error(this.i18n.t('pricing.exportError'));
    } finally {
      this.downloading.set(false);
    }
  }

  protected openImport(): void {
    if (this.facade.dirtyRows().length > 0) {
      this.toast.warn(this.i18n.t('pricing.tools.saveBeforeTool'));
      return;
    }

    this.facade.importLines.reset();
    this.importResult.set(null);
    this.importOpen.set(true);
  }

  protected async checkImport(rows: readonly ImportPriceRowRequest[]): Promise<void> {
    await this.runImport(rows, true);
  }

  protected async applyImport(rows: readonly ImportPriceRowRequest[]): Promise<void> {
    const result = await this.runImport(rows, false);
    if (result?.applied) {
      this.importOpen.set(false);
      this.toast.success(this.i18n.tf('pricing.tools.importApplied', { added: result.added, changed: result.changed }), { title: this.i18n.t('pricing.editor.applyReminder') });
    }
  }

  private async runImport(rows: readonly ImportPriceRowRequest[], dryRun: boolean): Promise<ImportPriceLinesResult | null> {
    const listId = this.facade.listId();
    if (listId === null || this.facade.importLines.pending()) {
      return null;
    }

    const result = await this.facade.importLines.execute({ listId, rows, dryRun });
    this.importResult.set(result);
    return result;
  }

  protected openAdjust(): void {
    if (this.facade.dirtyRows().length > 0) {
      this.toast.warn(this.i18n.t('pricing.tools.saveBeforeTool'));
      return;
    }

    this.facade.adjust.reset();
    this.adjustResult.set(null);
    this.adjustOpen.set(true);
  }

  protected async previewAdjust(request: AdjustBasePriceListRequest): Promise<void> {
    await this.runAdjust({ ...request, preview: true });
  }

  protected async applyAdjust(request: AdjustBasePriceListRequest): Promise<void> {
    const result = await this.runAdjust({ ...request, preview: false });
    if (result?.applied) {
      this.adjustOpen.set(false);
      this.toast.success(this.i18n.tc('pricing.tools.adjustApplied', result.changed), { title: this.i18n.t('pricing.editor.applyReminder') });
    }
  }

  private async runAdjust(request: AdjustBasePriceListRequest): Promise<AdjustBasePriceListResult | null> {
    const listId = this.facade.listId();
    if (listId === null || this.facade.adjust.pending()) {
      return null;
    }

    const result = await this.facade.adjust.execute({ listId, request });
    this.adjustResult.set(result);
    return result;
  }

  /** Print: the list header and every stored line (unsaved edits are not printed) on A4 portrait. */
  protected async print(): Promise<void> {
    const detail = this.detail();
    const stored = this.facade.lines.hasValue() ? this.facade.lines.value() : null;
    if (detail === null || stored === null || this.printing()) {
      return;
    }

    this.printing.set(true);
    try {
      const lang = this.i18n.lang();
      const lines: PrintedPriceLine[] = [...stored]
        .map((line) => ({ code: this.facade.names.code(line.serviceId), name: this.facade.names.name(line.serviceId, lang), price: line.price, active: line.isActive }))
        .sort((a, b) => a.code.localeCompare(b.code));
      const printedAt = new Intl.DateTimeFormat(lang === 'ar' ? 'ar-EG' : 'en-GB', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date());
      await this.printer.print({
        title: `${this.i18n.t('pricing.print.title')} — ${detail.code}`,
        size: 'A4',
        component: {
          type: PriceListDocumentComponent,
          inputs: { detail, lines, labels: this.printLabels(), lang, printedBy: displayNameFor(this.auth.profile(), lang) || null, printedAt },
        },
      });
    } catch {
      this.toast.error(this.i18n.t('pricing.print.failed'));
    } finally {
      this.printing.set(false);
    }
  }

  private printLabels(): PriceListLabels {
    const t = (key: string): string => this.i18n.t(`pricing.print.${key}`);
    return {
      title: t('title'),
      code: t('code'),
      name: t('name'),
      year: t('year'),
      status: t('status'),
      active: t('active'),
      inactive: t('inactive'),
      lines: t('lines'),
      service: t('service'),
      price: t('price'),
      activeAtOrdering: t('activeAtOrdering'),
      yes: this.i18n.t('shared.yes'),
      no: this.i18n.t('shared.no'),
      count: t('count'),
      labNameEn: this.i18n.t('pricing.review.labNameEn'),
      labNameAr: this.i18n.t('pricing.review.labNameAr'),
    };
  }

  protected tierLabel(nameEn: string, nameAr: string): string {
    return localizedPick({ en: nameEn, ar: nameAr }, this.i18n.lang());
  }

  protected amount(value: number | null): string {
    return value === null ? '—' : formatAmount(value, this.i18n.lang());
  }

  protected problemText(code: string): string {
    return problemMessage(this.i18n, 'pricing', { code });
  }

  /** CanDeactivate: unsaved prices ask before leaving. */
  async canLeave(): Promise<boolean> {
    if (this.facade.dirtyRows().length === 0) {
      return true;
    }

    return this.confirm.confirm({ title: this.i18n.t('pricing.editor.leaveTitle'), message: this.i18n.t('pricing.editor.leaveMessage'), danger: true });
  }
}
