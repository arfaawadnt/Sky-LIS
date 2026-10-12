/** Column and row contracts of `<app-data-table>`. */

import type { ServerSort } from '../../forms/paging';

export type CellAlign = 'start' | 'center' | 'end';

export type CellValue = string | number | boolean | null | undefined;

export interface TableColumn<T> {
  /** The row property (or the key of a cell template) this column shows. */
  readonly key: string;
  /** i18n key of the header, translated by the table. */
  readonly header: string;
  /** CSS width (`120px`, `20%`); omitted columns share the rest. */
  readonly width?: string;
  readonly align?: CellAlign;
  readonly sortable?: boolean;
  /**
   * The key the server sorts this column by when the list sorts on the server (`serverSort`); defaults to `key`. A
   * function picks it per language (a bilingual name: `localizedSortKey('name')` → `nameEn` / `nameAr`).
   */
  readonly sortKey?: string | ((lang: string) => string);
  /** The cell text (and client-side sort key) when the row property is not what to show. */
  readonly value?: (row: T) => CellValue;
  /** What a client-side sort compares when the cell text does not order correctly (a formatted date: sort by the instant). */
  readonly sortValue?: (row: T) => CellValue;
  /** Monospace cell (codes, numbers, references). */
  readonly mono?: boolean;
  /** Hidden under 960px to keep the card layout short. */
  readonly secondary?: boolean;
}

export type SortDirection = 'asc' | 'desc';

/** The server key of a bilingual column: `<base>En` in English, `<base>Ar` in Arabic (validation v0.5.0 B-03). */
export function localizedSortKey(base: string): (lang: string) => string {
  return (lang) => (lang === 'ar' ? `${base}Ar` : `${base}En`);
}

/** The server key a column sorts by in a language. */
export function serverSortKey<T>(column: TableColumn<T>, lang: string): string {
  const key = column.sortKey ?? column.key;
  return typeof key === 'function' ? key(lang) : key;
}

export interface TableSort {
  readonly key: string;
  readonly direction: SortDirection;
}

/**
 * The server keys of a facade-driven list (validation v0.5.0 B-03), by table column key: a fixed key, or a function of
 * the language for a bilingual column (`localizedSortKey('name')`). A column absent from the map has no server order.
 */
export type SortKeyMap = Readonly<Record<string, string | ((lang: string) => string)>>;

/**
 * The server order of a table sort: the column's key in `keys` and the direction; `null` without a sort or for a column
 * the map does not know. `lang` is read only when there is a sort, so a language switch reloads a sorted list alone.
 */
export function serverSortOf(sort: TableSort | null, keys: SortKeyMap, lang: () => string): ServerSort | null {
  if (sort === null) {
    return null;
  }

  const key = keys[sort.key];
  if (key === undefined) {
    return null;
  }

  return { key: typeof key === 'function' ? key(lang()) : key, direction: sort.direction };
}

export type SelectionMode = 'none' | 'single' | 'multi';

/** The context of `<ng-template appCell="…" let-row>` and `<ng-template appRowActions let-row>`. */
export interface RowContext<T> {
  readonly $implicit: T;
  readonly row: T;
  readonly index: number;
}

export type RowId<T> = (row: T) => string;

/** `id` when the row has one, else a stable serialisation. */
export function defaultRowId<T>(row: T): string {
  const candidate = (row as unknown as { id?: unknown }).id;
  return typeof candidate === 'string' || typeof candidate === 'number' ? String(candidate) : JSON.stringify(row);
}

export function cellValue<T>(column: TableColumn<T>, row: T): CellValue {
  if (column.value) {
    return column.value(row);
  }

  const raw = (row as unknown as Record<string, unknown>)[column.key];
  return typeof raw === 'string' || typeof raw === 'number' || typeof raw === 'boolean' || raw === null || raw === undefined ? raw : String(raw);
}

/** Client-side ordering: numbers numerically, strings by locale, empties last. */
export function compareCells(a: CellValue, b: CellValue, lang: string): number {
  const aEmpty = a === null || a === undefined || a === '';
  const bEmpty = b === null || b === undefined || b === '';
  if (aEmpty && bEmpty) {
    return 0;
  }

  if (aEmpty) {
    return 1;
  }

  if (bEmpty) {
    return -1;
  }

  if (typeof a === 'number' && typeof b === 'number') {
    return a - b;
  }

  if (typeof a === 'boolean' && typeof b === 'boolean') {
    return Number(a) - Number(b);
  }

  return String(a).localeCompare(String(b), lang, { numeric: true, sensitivity: 'base' });
}

/**
 * The rows in the order of `sort` (a copy; the input as is without a sort or an unknown column). A list that loads every
 * row and pages in the browser sorts with this **before** it slices the page, so a sort covers the whole list (B-03).
 */
export function sortRows<T>(rows: readonly T[], columns: readonly TableColumn<T>[], sort: TableSort | null, lang: string): readonly T[] {
  if (sort === null) {
    return rows;
  }

  const column = columns.find((candidate) => candidate.key === sort.key);
  if (!column) {
    return rows;
  }

  const direction = sort.direction === 'asc' ? 1 : -1;
  const key = column.sortValue ?? ((row: T): CellValue => cellValue(column, row));
  return [...rows].sort((a, b) => compareCells(key(a), key(b), lang) * direction);
}
