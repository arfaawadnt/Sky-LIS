import type { Paged } from '../../../core/api/paging';
import { MAX_PAGE_SIZE } from '../../../core/api/paging';

/**
 * CSV export of the treasury register (SRS "Grids & editors": export; UAT B-10). The page exports the rows of its
 * current filter, read page after page from the list endpoint, in the grid's own formats: translated headers and
 * labels, Yes / No, readable lists. UTF-8 with a byte-order mark (spreadsheet programs then read Arabic), RFC 4180
 * quoting, and formula-leading text prefixed with an apostrophe (CSV injection). Same format as the catalogue exports.
 */

export interface CsvColumn<T> {
  /** Already translated header text. */
  readonly header: string;
  /** Already formatted cell (labels translated, booleans as the translated Yes / No). */
  readonly value: (row: T) => string | number | null | undefined;
}

/** The most rows one export reads (the server's own export limit). */
export const MAX_EXPORT_ROWS = 10_000;

const BYTE_ORDER_MARK = String.fromCharCode(0xfeff);
const FORMULA_START = /^[=+\-@\t\r]/;

export function csvCell(value: string | number | null | undefined): string {
  if (value === null || value === undefined) {
    return '';
  }

  let text = String(value);
  if (typeof value === 'string' && FORMULA_START.test(text)) {
    text = `'${text}`;
  }

  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

export function toCsv<T>(rows: readonly T[], columns: readonly CsvColumn<T>[]): string {
  const lines = [columns.map((column) => csvCell(column.header)).join(',')];
  for (const row of rows) {
    lines.push(columns.map((column) => csvCell(column.value(row))).join(','));
  }

  return `${BYTE_ORDER_MARK}${lines.join('\r\n')}\r\n`;
}

/** `treasuries-2026-10-11.csv`, dated in the user's own calendar (not UTC). */
export function csvFileName(name: string, now: Date = new Date()): string {
  const pad = (value: number): string => String(value).padStart(2, '0');
  return `${name}-${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}.csv`;
}

/** Every row of the filtered register, page after page (largest page the server allows), up to {@link MAX_EXPORT_ROWS}. */
export async function allPages<T>(load: (page: number, pageSize: number) => Promise<Paged<T>>): Promise<readonly T[]> {
  const rows: T[] = [];
  for (let page = 1; rows.length < MAX_EXPORT_ROWS; page++) {
    const current = await load(page, MAX_PAGE_SIZE);
    rows.push(...current.items.slice(0, MAX_EXPORT_ROWS - rows.length));
    if (current.items.length < MAX_PAGE_SIZE || page >= current.totalPages) {
      break;
    }
  }

  return rows;
}

/** Hands the text to the browser as a file download (no-op where the platform has no object URLs, e.g. tests). */
export function downloadCsv(fileName: string, content: string, doc: Document = document): void {
  const view = doc.defaultView;
  if (!view?.URL.createObjectURL) {
    return;
  }

  const url = view.URL.createObjectURL(new Blob([content], { type: 'text/csv;charset=utf-8' }));
  const anchor = doc.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.rel = 'noopener';
  anchor.style.display = 'none';
  doc.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  view.URL.revokeObjectURL(url);
}
