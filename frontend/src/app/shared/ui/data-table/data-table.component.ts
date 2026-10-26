import { DOCUMENT, NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, ElementRef, Injector, computed, contentChild, contentChildren, inject, input, model, output, signal } from '@angular/core';

import { LaboratoryBrandService } from '../../../core/brand/laboratory-brand.service';
import { I18nService } from '../../../core/i18n/i18n.service';
import { type ExportTexts, type TableExport, buildTableWorkbook, exportFileName, saveBlob } from '../../export/table-export';
import { PrintService } from '../../print/print.service';
import { EmptyStateComponent } from '../empty-state/empty-state.component';
import { IconComponent, type IconName } from '../icon/icon.component';
import { ToastService } from '../toast/toast.service';
import { CellTemplateDirective, RowActionsTemplateDirective } from './cell-template.directive';
import { type ColumnChoice, ColumnChooserComponent } from './column-chooser.component';
import { type RowId, type SelectionMode, type TableColumn, type TableSort, cellValue, defaultRowId, optionalColumns, sortRows, tableExport, visibleColumns } from './data-table.types';
import { TABLE_PREFERENCES, type TablePreferences } from './table-preferences';
import { TablePrintComponent, tablePrintStyles } from './table-print.component';

/**
 * `<app-data-table [columns]="columns" [rows]="rows()" [loading]="…" (rowActivate)="open($event)">` with optional
 * `<ng-template appCell="key" let-row>` cells and one `<ng-template appRowActions let-row>`.
 *
 * - Typed columns (`key`, i18n `header`, `width`, `align`, `sortable`, `value`), sticky header, zebra rows.
 * - Sorting is client-side (over the rows given) unless `serverSort`; `sortChange` always emits so a paged list sorts
 *   on the server before paging (validation v0.5.0 B-03: sorting a server page here would reorder that page only).
 * - Keyboard: the rows are a roving tab stop; Arrow Up/Down move, Home/End jump, Enter activates
 *   (`rowActivate`), Space toggles selection. A click emits `rowClick` and `rowActivate`.
 * - Selection: `selection="single|multi"` with `[(selected)]` holding row ids (`rowId`, default `row.id`).
 * - Under 720px every row becomes a card with the header text before each cell.
 * - LIS-CR-014 T2: columns marked `optional` can be hidden with the "⋯" chooser at the end of the header row; with a
 *   `listKey` the choice is saved per user (`TABLE_PREFERENCES` → `AuthStore.updateListPreferences`), otherwise it lasts
 *   while the page is open. Hidden columns are not rendered (header and cells).
 * - LIS-CR-014 T4: `[exportable]="true"` adds *Print* (A4 landscape document through the browser's print dialog, so a PDF
 *   with "Save as PDF") and *Excel* (`.xlsx`) above the table: the visible columns of the selected rows (`selection="multi"`)
 *   or of every row shown when none is selected; `exportTitle` (default `caption`) and `printSubtitle` head the document.
 */
@Component({
  selector: 'app-data-table',
  imports: [NgTemplateOutlet, IconComponent, EmptyStateComponent, ColumnChooserComponent],
  templateUrl: './data-table.component.html',
  styleUrl: './data-table.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DataTableComponent<T> {
  protected readonly i18n = inject(I18nService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly injector = inject(Injector);
  private preferenceStore: TablePreferences | null = null;

  readonly columns = input.required<readonly TableColumn<T>[]>();
  readonly rows = input.required<readonly T[]>();
  readonly rowId = input<RowId<T>>(defaultRowId);
  readonly loading = input(false);
  /** Translated texts of the empty state; defaults to `shared.table.empty`. */
  readonly emptyTitle = input<string | null>(null);
  readonly emptyMessage = input<string | null>(null);
  /** Caption for assistive technology (translated). */
  readonly caption = input<string | null>(null);
  readonly selection = input<SelectionMode>('none');
  readonly selected = model<readonly string[]>([]);
  /** The current sort; its `sortChange` output is what a facade listens to for server-side sorting. */
  readonly sort = model<TableSort | null>(null);
  readonly serverSort = input(false);
  readonly compact = input(false);
  readonly zebra = input(true);
  /** Rows the user can open: pointer cursor and hover (default when `rowActivate` is used). */
  readonly interactive = input(true);
  readonly rowClass = input<((row: T) => string | null) | null>(null);
  /** Cell templates handed down by a wrapping component (the master-data page forwards its content templates). */
  readonly cellTemplates = input<readonly CellTemplateDirective<T>[] | null>(null);
  /** LIS-CR-014 T2: the key the user's column choice is saved under (`visits`, `tracker` …); null = not saved. */
  readonly listKey = input<string | null>(null);
  /** LIS-CR-014 T4: shows *Print* and *Excel* above the table. */
  readonly exportable = input(false);
  /** The translated title of the printed list, the sheet and the file; defaults to `caption`. */
  readonly exportTitle = input<string | null>(null);
  /** The filters in force, printed under the title (translated). */
  readonly printSubtitle = input<string | null>(null);

  readonly rowClick = output<T>();
  readonly rowActivate = output<T>();
  readonly selectionChange = output<readonly T[]>();

  private readonly cells = contentChildren(CellTemplateDirective);
  protected readonly actions = contentChild(RowActionsTemplateDirective);

  protected readonly focusedIndex = signal(-1);
  protected readonly exporting = signal(false);

  /** The hidden column keys while the table has no `listKey` (the choice then lasts while the page is open). */
  private readonly localHidden = signal<readonly string[]>([]);

  /** LIS-CR-014 T2: the keys hidden by the user's choice (saved per user under `listKey`). */
  protected readonly hiddenKeys = computed<ReadonlySet<string>>(() => {
    const key = this.listKey();
    return new Set(key === null ? this.localHidden() : this.preferences().hidden(key));
  });

  /** The columns rendered: the given ones without the hidden optional ones. */
  protected readonly shownColumns = computed(() => visibleColumns(this.columns(), this.hiddenKeys()));

  protected readonly choices = computed<readonly ColumnChoice[]>(() =>
    optionalColumns(this.columns()).map((column) => ({ key: column.key, label: this.i18n.t(column.header), visible: !this.hiddenKeys().has(column.key) })),
  );

  /** The rows as shown: sorted here unless the server did. */
  protected readonly view = computed<readonly T[]>(() => (this.serverSort() ? this.rows() : sortRows(this.rows(), this.columns(), this.sort(), this.i18n.lang())));

  /** The chooser's own header cell when the table has no actions column (it then sits at the end of the header row). */
  protected readonly chooserCell = computed(() => this.choices().length > 0 && !this.actions());

  protected readonly columnCount = computed(() => this.shownColumns().length + (this.selection() === 'multi' ? 1 : 0) + (this.actions() || this.chooserCell() ? 1 : 0));

  protected readonly selectedSet = computed(() => new Set(this.selected()));

  protected readonly allSelected = computed(() => this.view().length > 0 && this.view().every((row) => this.selectedSet().has(this.rowId()(row))));

  protected readonly someSelected = computed(() => !this.allSelected() && this.view().some((row) => this.selectedSet().has(this.rowId()(row))));

  private readonly templates = computed(() => new Map([...this.cells(), ...(this.cellTemplates() ?? [])].map((cell) => [cell.appCell(), cell.template])));

  protected templateFor(key: string): CellTemplateDirective<T>['template'] | null {
    return (this.templates().get(key) as CellTemplateDirective<T>['template'] | undefined) ?? null;
  }

  protected rowActionsTemplate(): RowActionsTemplateDirective<T>['template'] | null {
    return (this.actions()?.template as RowActionsTemplateDirective<T>['template'] | undefined) ?? null;
  }

  protected cellText(column: TableColumn<T>, row: T): string {
    const value = cellValue(column, row);
    if (value === null || value === undefined || value === '') {
      return '—';
    }

    if (typeof value === 'boolean') {
      return this.i18n.t(value ? 'shared.yes' : 'shared.no');
    }

    return String(value);
  }

  protected idOf(row: T): string {
    return this.rowId()(row);
  }

  protected isSelected(row: T): boolean {
    return this.selectedSet().has(this.idOf(row));
  }

  protected tabIndexFor(index: number): number {
    const focused = this.focusedIndex();
    return focused === index || (focused < 0 && index === 0) ? 0 : -1;
  }

  protected ariaSort(column: TableColumn<T>): 'ascending' | 'descending' | 'none' | null {
    if (!column.sortable) {
      return null;
    }

    const sort = this.sort();
    return sort?.key === column.key ? (sort.direction === 'asc' ? 'ascending' : 'descending') : 'none';
  }

  protected sortIcon(column: TableColumn<T>): IconName {
    const sort = this.sort();
    return sort?.key === column.key ? (sort.direction === 'asc' ? 'arrow-up' : 'arrow-down') : 'sort';
  }

  protected toggleSort(column: TableColumn<T>): void {
    if (!column.sortable) {
      return;
    }

    const current = this.sort();
    const next: TableSort | null = current?.key !== column.key ? { key: column.key, direction: 'asc' } : current.direction === 'asc' ? { key: column.key, direction: 'desc' } : null;
    this.sort.set(next);
  }

  protected onRowClick(event: MouseEvent, row: T, index: number): void {
    const target = event.target as HTMLElement | null;
    if (target?.closest('button, a, input, select, textarea, label, .actions')) {
      return;
    }

    this.focusedIndex.set(index);
    this.rowClick.emit(row);
    if (this.selection() !== 'none') {
      this.toggle(row);
    } else {
      this.rowActivate.emit(row);
    }
  }

  protected onRowKeydown(event: KeyboardEvent, row: T, index: number): void {
    if (event.target !== event.currentTarget) {
      return;
    }

    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        this.focusRow(index + 1);
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.focusRow(index - 1);
        break;
      case 'Home':
        event.preventDefault();
        this.focusRow(0);
        break;
      case 'End':
        event.preventDefault();
        this.focusRow(this.view().length - 1);
        break;
      case 'Enter':
        event.preventDefault();
        this.rowActivate.emit(row);
        break;
      case ' ':
        if (this.selection() !== 'none') {
          event.preventDefault();
          this.toggle(row);
        }

        break;
      default:
        break;
    }
  }

  protected onRowFocus(index: number): void {
    this.focusedIndex.set(index);
  }

  protected toggle(row: T): void {
    const mode = this.selection();
    if (mode === 'none') {
      return;
    }

    const id = this.idOf(row);
    const current = this.selectedSet();
    let next: string[];
    if (mode === 'single') {
      next = current.has(id) ? [] : [id];
    } else {
      next = current.has(id) ? [...current].filter((candidate) => candidate !== id) : [...current, id];
    }

    this.applySelection(next);
  }

  protected toggleAll(): void {
    const next = this.allSelected() ? [] : this.view().map((row) => this.idOf(row));
    this.applySelection(next);
  }

  /** LIS-CR-014 T2: shows or hides one optional column; saved per user when the table has a `listKey`. */
  protected toggleColumn(key: string): void {
    const hidden = this.hiddenKeys();
    const next = optionalColumns(this.columns())
      .map((column) => column.key)
      .filter((candidate) => (candidate === key ? !hidden.has(candidate) : hidden.has(candidate)));
    this.saveHidden(next);
  }

  /** *Reset*: every optional column shows again. */
  protected resetColumns(): void {
    this.saveHidden([]);
  }

  /** True while some of the rows shown are selected: *Print* and *Excel* then take those rows only. */
  protected readonly exportsSelection = computed(() => this.selection() !== 'none' && this.view().some((row) => this.selectedSet().has(this.idOf(row))));

  /** The rows *Print* and *Excel* take: the selected rows of the view, or every row shown when none is selected. */
  protected readonly exportRows = computed<readonly T[]>(() =>
    this.exportsSelection() ? this.view().filter((row) => this.selectedSet().has(this.idOf(row))) : this.view(),
  );

  protected readonly exportHint = computed(() =>
    this.i18n.tc(this.exportsSelection() ? 'shared.table.exportSelected' : 'shared.table.exportShown', this.exportRows().length),
  );

  /** The document of *Print* and *Excel*: the visible columns of `exportRows`. */
  exportDocument(): TableExport {
    const title = this.exportTitle() ?? this.caption() ?? '';
    return tableExport(this.shownColumns(), this.exportRows(), title, this.printSubtitle(), (key) => this.i18n.t(key));
  }

  /** LIS-CR-014 T4: the A4 landscape print document of the rows; the browser's print dialog gives the PDF. */
  async print(): Promise<void> {
    if (this.exporting()) {
      return;
    }

    this.exporting.set(true);
    try {
      const table = this.exportDocument();
      const printer = this.injector.get(PrintService);
      const page = this.i18n.t('shared.table.page');
      const body = printer.render({
        type: TablePrintComponent,
        inputs: {
          table,
          laboratory: this.laboratoryName(),
          printedAt: new Intl.DateTimeFormat(this.i18n.lang() === 'ar' ? 'ar-EG' : 'en-GB', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date()),
          labels: { printedAt: this.i18n.t('shared.print.printedAt'), rows: this.i18n.tc('shared.table.rowCount', table.rows.length), page },
          texts: this.exportTexts(),
        },
      });
      await printer.print({ title: table.title, size: 'A4', landscape: true, html: `<style>${tablePrintStyles(page)}</style>${body}` });
    } catch {
      this.injector.get(ToastService).error(this.i18n.t('shared.table.exportFailed'));
    } finally {
      this.exporting.set(false);
    }
  }

  /** LIS-CR-014 T4: the same rows as an `.xlsx` download (one sheet named after the list). */
  async excel(): Promise<void> {
    if (this.exporting()) {
      return;
    }

    this.exporting.set(true);
    try {
      const table = this.exportDocument();
      const blob = await buildTableWorkbook(table, this.exportTexts());
      const now = new Date();
      const day = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
      saveBlob(this.injector.get(DOCUMENT), blob, exportFileName(table.title, day, 'xlsx'));
    } catch {
      this.injector.get(ToastService).error(this.i18n.t('shared.table.exportFailed'));
    } finally {
      this.exporting.set(false);
    }
  }

  protected clearSelection(): void {
    this.applySelection([]);
  }

  /** Focuses a row by index (used by pages after a reload to keep the user's place). */
  focusRow(index: number): void {
    const rows = this.host.querySelectorAll<HTMLTableRowElement>('tbody tr[data-row]');
    const clamped = Math.max(0, Math.min(index, rows.length - 1));
    const row = rows[clamped];
    if (row) {
      this.focusedIndex.set(clamped);
      row.focus();
    }
  }

  private preferences(): TablePreferences {
    this.preferenceStore ??= this.injector.get(TABLE_PREFERENCES);
    return this.preferenceStore;
  }

  private saveHidden(hidden: readonly string[]): void {
    const key = this.listKey();
    if (key === null) {
      this.localHidden.set(hidden);
      return;
    }

    this.preferences()
      .save(key, hidden)
      .catch(() => this.injector.get(ToastService).error(this.i18n.t('shared.table.columnsSaveFailed')));
  }

  private exportTexts(): ExportTexts {
    return { lang: this.i18n.lang(), yes: this.i18n.t('shared.yes'), no: this.i18n.t('shared.no') };
  }

  /** The laboratory of the session for the head of a printed list (the product's name in the Super Admin portal). */
  private laboratoryName(): string {
    return this.injector.get(LaboratoryBrandService).name() ?? 'Sky LIS';
  }

  private applySelection(ids: readonly string[]): void {
    this.selected.set(ids);
    const wanted = new Set(ids);
    this.selectionChange.emit(this.rows().filter((row) => wanted.has(this.idOf(row))));
  }
}
