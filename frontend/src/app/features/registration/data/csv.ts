/**
 * CSV export of the registers (CP-10 "Export"): RFC 4180 quoting, UTF-8 with a byte-order mark so spreadsheet programs
 * read Arabic, and formula-injection protection (a cell starting with `= + - @`, tab or carriage return is prefixed with
 * an apostrophe). The server decides who may export (`registration.export`); this only writes the file.
 */
export function csvCell(value: string | number | boolean | null | undefined): string {
  if (value === null || value === undefined) {
    return '';
  }

  let text = typeof value === 'number' ? String(value) : typeof value === 'boolean' ? (value ? 'true' : 'false') : value;
  if (typeof value === 'string' && /^[=+\-@\t\r]/.test(text)) {
    text = `'${text}`;
  }

  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

export function csvText(headers: readonly string[], rows: readonly (readonly (string | number | boolean | null | undefined)[])[]): string {
  return [headers, ...rows].map((row) => row.map(csvCell).join(',')).join('\r\n');
}

/** Saves the text as a `.csv` file through a temporary object URL. */
export function downloadCsv(fileName: string, text: string): void {
  const blob = new Blob(['﻿', text], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.rel = 'noopener';
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  setTimeout(() => URL.revokeObjectURL(url), 0);
}
