/** A CSV cell: text, a number (written with the invariant `.` decimal separator) or empty. */
export type CsvCell = string | number | boolean | null | undefined;

/**
 * RFC 4180 CSV with a UTF-8 byte-order mark so spreadsheet programs show Arabic names correctly (intake Q17).
 * Cells containing a separator, a quote or a line break are quoted; formula-leading text is neutralised with a quote
 * prefix so a cell such as `=HYPERLINK(…)` stays text (CSV injection).
 */
export function toCsv(header: readonly string[], rows: readonly (readonly CsvCell[])[]): string {
  const lines = [header, ...rows].map((row) => row.map(cell).join(','));
  return `\uFEFF${lines.join('\r\n')}\r\n`;
}

function cell(value: CsvCell): string {
  if (value === null || value === undefined) {
    return '';
  }

  if (typeof value === 'number') {
    return Number.isFinite(value) ? String(value) : '';
  }

  if (typeof value === 'boolean') {
    return value ? 'true' : 'false';
  }

  const guarded = /^[=+\-@\t\r]/.test(value) ? `'${value}` : value;
  return /[",\r\n]/.test(guarded) ? `"${guarded.replace(/"/g, '""')}"` : guarded;
}

/** Hands the CSV to the browser as a download (no server round trip; the data is what the page shows). */
export function downloadCsv(fileName: string, content: string, doc: Document = document): void {
  const blob = new Blob([content], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  try {
    const link = doc.createElement('a');
    link.href = url;
    link.download = fileName;
    link.rel = 'noopener';
    doc.body.appendChild(link);
    link.click();
    link.remove();
  } finally {
    URL.revokeObjectURL(url);
  }
}

/** `pl26-prices-2026-10-09.csv`: a safe file name from a code and today's date. */
export function csvFileName(stem: string, today: Date): string {
  const safe = stem.toLowerCase().replace(/[^a-z0-9._-]+/g, '-').replace(/^-+|-+$/g, '') || 'export';
  return `${safe}-${today.toISOString().slice(0, 10)}.csv`;
}
