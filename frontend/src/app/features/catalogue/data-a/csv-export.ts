/**
 * CSV export of the catalogue-A registers (SRS "Grids & editors": export). The server returns the full filtered list
 * under `catalogue.export`; this file turns it into a UTF-8 CSV with a byte-order mark (Excel reads Arabic correctly)
 * and hands it to the browser. Cells that a spreadsheet would read as a formula are prefixed with an apostrophe
 * (CSV injection). Candidate for `shared/` (reported to the integrator); kept here until the kit offers it.
 */

export interface CsvColumn<T> {
  /** Already translated header text. */
  readonly header: string;
  readonly value: (row: T) => string | number | boolean | null | undefined;
}

const BYTE_ORDER_MARK = String.fromCharCode(0xfeff);

const FORMULA_START = /^[=+\-@\t\r]/;

export function csvCell(value: string | number | boolean | null | undefined): string {
  if (value === null || value === undefined) {
    return '';
  }

  let text = typeof value === 'boolean' ? (value ? 'true' : 'false') : String(value);
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

/** `catalogue-tests-2026-10-09.csv`. */
export function csvFileName(name: string, now: Date = new Date()): string {
  return `${name}-${now.toISOString().slice(0, 10)}.csv`;
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
