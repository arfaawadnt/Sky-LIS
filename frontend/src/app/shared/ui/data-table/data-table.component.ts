import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, ElementRef, computed, contentChild, contentChildren, inject, input, model, output, signal } from '@angular/core';

import { I18nService } from '../../../core/i18n/i18n.service';
import { EmptyStateComponent } from '../empty-state/empty-state.component';
import { IconComponent, type IconName } from '../icon/icon.component';
import { CellTemplateDirective, RowActionsTemplateDirective } from './cell-template.directive';
import { type RowId, type SelectionMode, type TableColumn, type TableSort, cellValue, defaultRowId, sortRows } from './data-table.types';

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
 */
@Component({
  selector: 'app-data-table',
  imports: [NgTemplateOutlet, IconComponent, EmptyStateComponent],
  templateUrl: './data-table.component.html',
  styleUrl: './data-table.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DataTableComponent<T> {
  protected readonly i18n = inject(I18nService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

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

  readonly rowClick = output<T>();
  readonly rowActivate = output<T>();
  readonly selectionChange = output<readonly T[]>();

  private readonly cells = contentChildren(CellTemplateDirective);
  protected readonly actions = contentChild(RowActionsTemplateDirective);

  protected readonly focusedIndex = signal(-1);

  /** The rows as shown: sorted here unless the server did. */
  protected readonly view = computed<readonly T[]>(() => (this.serverSort() ? this.rows() : sortRows(this.rows(), this.columns(), this.sort(), this.i18n.lang())));

  protected readonly columnCount = computed(() => this.columns().length + (this.selection() === 'multi' ? 1 : 0) + (this.actions() ? 1 : 0));

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

  private applySelection(ids: readonly string[]): void {
    this.selected.set(ids);
    const wanted = new Set(ids);
    this.selectionChange.emit(this.rows().filter((row) => wanted.has(this.idOf(row))));
  }
}
