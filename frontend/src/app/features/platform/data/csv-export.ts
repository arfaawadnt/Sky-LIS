import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';

/** A column of an export: the header text (already translated) and the cell value of a row. */
export interface CsvColumn<T> {
  readonly header: string;
  readonly value: (row: T) => string | number | boolean | null | undefined;
}

/** RFC 4180 text with a UTF-8 BOM so spreadsheet programs read Arabic correctly; CRLF line ends. */
export function toCsv<T>(columns: readonly CsvColumn<T>[], rows: readonly T[]): string {
  const escape = (value: string | number | boolean | null | undefined): string => {
    if (value === null || value === undefined) {
      return '';
    }

    const text = String(value);
    // a leading = + - @ would be read as a formula by spreadsheet programs (CSV injection)
    const safe = /^[=+\-@\t\r]/.test(text) && !/^-?\d+(\.\d+)?$/.test(text) ? `'${text}` : text;
    return /[",\r\n]/.test(safe) ? `"${safe.replace(/"/g, '""')}"` : safe;
  };

  const lines = [columns.map((column) => escape(column.header)).join(',')];
  for (const row of rows) {
    lines.push(columns.map((column) => escape(column.value(row))).join(','));
  }

  return '﻿' + lines.join('\r\n') + '\r\n';
}

/** "Export CSV" of the SRS grids: builds the file in memory and hands it to the browser's download; nothing is stored. */
@Injectable({ providedIn: 'root' })
export class CsvExportService {
  private readonly document = inject(DOCUMENT);

  download<T>(fileName: string, columns: readonly CsvColumn<T>[], rows: readonly T[]): void {
    this.downloadBlob(fileName, new Blob([toCsv(columns, rows)], { type: 'text/csv;charset=utf-8' }));
  }

  /** Hands a file the server built (a CSV export) to the browser's download under the server's file name. */
  downloadBlob(fileName: string, blob: Blob): void {
    const url = URL.createObjectURL(blob);
    const anchor = this.document.createElement('a');
    anchor.href = url;
    anchor.download = fileName.endsWith('.csv') ? fileName : `${fileName}.csv`;
    anchor.rel = 'noopener';
    anchor.style.display = 'none';
    this.document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), 0);
  }
}
