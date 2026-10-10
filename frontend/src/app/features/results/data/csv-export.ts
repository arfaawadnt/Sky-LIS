/** One column of a CSV export: the header (already translated) and the cell value of a row. */
export interface CsvColumn<T> {
  readonly header: string;
  readonly value: (row: T) => string | number | boolean | null | undefined;
}

/** The most rows a client-side export writes (intake §14). */
export const CSV_MAX_ROWS = 5000;

const FORMULA_START = /^[=+\-@\t\r]/;

/**
 * RFC 4180 text with CRLF line ends: fields with a comma, quote or line break are quoted, quotes doubled. Text that a
 * spreadsheet would run as a formula (`=`, `+`, `-`, `@` at the start) is prefixed with an apostrophe (CSV injection).
 * The kit has no export helper yet (reported to the integrator); this mirrors the Samples one.
 */
export function toCsv<T>(rows: readonly T[], columns: readonly CsvColumn<T>[]): string {
  const lines = [columns.map((column) => escapeCell(column.header)).join(',')];
  for (const row of rows) {
    lines.push(columns.map((column) => escapeCell(column.value(row))).join(','));
  }

  return lines.join('\r\n');
}

function escapeCell(value: string | number | boolean | null | undefined): string {
  if (value === null || value === undefined) {
    return '';
  }

  if (typeof value !== 'string') {
    return String(value);
  }

  const safe = FORMULA_START.test(value) ? `'${value}` : value;
  return /[",\r\n]/.test(safe) ? `"${safe.replace(/"/g, '""')}"` : safe;
}

/** Saves the text as a UTF-8 file with a byte-order mark, so spreadsheet programs read Arabic names correctly. */
export function downloadCsv(document: Document, fileName: string, csv: string): void {
  const view = document.defaultView;
  if (!view) {
    return;
  }

  const blob = new Blob(['﻿', csv], { type: 'text/csv;charset=utf-8' });
  const url = view.URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.rel = 'noopener';
  link.style.display = 'none';
  document.body.appendChild(link);
  link.click();
  link.remove();
  view.setTimeout(() => view.URL.revokeObjectURL(url), 0);
}
