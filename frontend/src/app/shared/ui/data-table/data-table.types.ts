/** Column and row contracts of `<app-data-table>`. */

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
  /** The cell text (and client-side sort key) when the row property is not what to show. */
  readonly value?: (row: T) => CellValue;
  /** Monospace cell (codes, numbers, references). */
  readonly mono?: boolean;
  /** Hidden under 960px to keep the card layout short. */
  readonly secondary?: boolean;
}

export type SortDirection = 'asc' | 'desc';

export interface TableSort {
  readonly key: string;
  readonly direction: SortDirection;
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
