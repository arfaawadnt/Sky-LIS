/**
 * LIS-CR-014 T4: the rows of a list table as a document — what *Print* and *Excel* of `<app-data-table [exportable]="true">`
 * hand over. Pure functions (no Angular): the table builds a `TableExport` of its **visible columns** and of the selected rows
 * (or every loaded row), the print component renders it, `buildTableWorkbook` writes it as `.xlsx`.
 *
 * A cell is a plain value (text, number, boolean, `Date`) or a `TypedCell` carrying both the typed value Excel stores and
 * the text a printed page shows (a money amount: `{ value: 150, text: '150.00', numFmt: '#,##0.00' }`). `exceljs` is
 * CommonJS and heavy, so it is loaded on demand (`await import('exceljs')`) and never reaches the eager bundle.
 */

/** A cell whose typed value (Excel) and text (print) differ; `numFmt` is the Excel number format of a number or date. */
export interface TypedCell {
  readonly value: string | number | boolean | Date | null;
  readonly text: string;
  readonly numFmt?: string;
}

export type ExportCell = string | number | boolean | Date | TypedCell | null | undefined;

export type ExportAlign = 'start' | 'center' | 'end';

export interface ExportColumn {
  readonly key: string;
  /** The translated header. */
  readonly header: string;
  readonly align: ExportAlign;
}

export interface TableExport {
  /** The translated list title: print title, sheet name and file name. */
  readonly title: string;
  /** The filters in force (`[printSubtitle]`), or null. */
  readonly subtitle: string | null;
  readonly columns: readonly ExportColumn[];
  readonly rows: readonly (readonly ExportCell[])[];
}

/** The language-bound texts of a printed cell: yes / no of a boolean, the locale of numbers and dates. */
export interface ExportTexts {
  readonly lang: string;
  readonly yes: string;
  readonly no: string;
}

export const XLSX_MIME = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';

/** A money amount: Excel keeps the number with two decimals, the page shows `text`. */
export function moneyCell(value: number | null | undefined, text: string): TypedCell | null {
  return typeof value === 'number' && Number.isFinite(value) ? { value, text, numFmt: '#,##0.00' } : null;
}

/** An instant (ISO text): Excel keeps the date-time, the page shows `text`. */
export function dateCell(iso: string | null | undefined, text: string): TypedCell | null {
  if (!iso) {
    return null;
  }

  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? null : { value: date, text, numFmt: 'yyyy-mm-dd hh:mm' };
}

export function isTypedCell(cell: ExportCell): cell is TypedCell {
  return typeof cell === 'object' && cell !== null && !(cell instanceof Date) && 'text' in cell;
}

function locale(lang: string): string {
  return lang === 'ar' ? 'ar-EG' : 'en-GB';
}

/** The text a printed page shows for a cell ("—" when empty). */
export function exportText(cell: ExportCell, texts: ExportTexts): string {
  if (isTypedCell(cell)) {
    return cell.text.trim() === '' ? '—' : cell.text;
  }

  if (cell === null || cell === undefined || cell === '') {
    return '—';
  }

  if (typeof cell === 'boolean') {
    return cell ? texts.yes : texts.no;
  }

  if (typeof cell === 'number') {
    return new Intl.NumberFormat(locale(texts.lang), { maximumFractionDigits: 2 }).format(cell);
  }

  if (cell instanceof Date) {
    return Number.isNaN(cell.getTime()) ? '—' : new Intl.DateTimeFormat(locale(texts.lang), { dateStyle: 'medium', timeStyle: 'short' }).format(cell);
  }

  return cell;
}

/** The value an Excel cell stores (numbers as numbers, dates as dates, booleans as yes / no text) and its number format. */
export function excelValue(cell: ExportCell, texts: ExportTexts): { readonly value: string | number | Date | null; readonly numFmt: string | null } {
  if (isTypedCell(cell)) {
    const value = typeof cell.value === 'boolean' ? (cell.value ? texts.yes : texts.no) : cell.value;
    return { value: value instanceof Date ? excelLocalDate(value) : value, numFmt: cell.numFmt ?? (value instanceof Date ? 'yyyy-mm-dd hh:mm' : null) };
  }

  if (cell === null || cell === undefined || cell === '') {
    return { value: null, numFmt: null };
  }

  if (typeof cell === 'boolean') {
    return { value: cell ? texts.yes : texts.no, numFmt: null };
  }

  if (cell instanceof Date) {
    return Number.isNaN(cell.getTime()) ? { value: null, numFmt: null } : { value: excelLocalDate(cell), numFmt: 'yyyy-mm-dd hh:mm' };
  }

  return { value: cell, numFmt: null };
}

/**
 * exceljs stores a `Date` by its UTC fields; a sheet shows wall-clock times without a zone, so the instant is moved by the
 * browser's offset and the cell reads the local time the screen showed.
 */
export function excelLocalDate(date: Date): Date {
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000);
}

/** A worksheet name Excel accepts: no `[]:*?/\`, at most 31 characters, never empty. */
export function safeSheetName(name: string): string {
  const clean = name.replace(/[[\]:*?/\\]/g, ' ').replace(/\s+/g, ' ').trim().slice(0, 31).trim();
  return clean === '' ? 'Sheet1' : clean;
}

/** `<title>-<yyyy-mm-dd>.<ext>` with the characters a file system refuses removed (Arabic titles stay readable). */
export function exportFileName(title: string, day: string, extension: string): string {
  const base = title.replace(/[\\/:*?"<>|]+/g, ' ').replace(/\s+/g, '-').replace(/^-+|-+$/g, '').slice(0, 60);
  return `${base === '' ? 'list' : base}-${day}.${extension}`;
}

type ExcelModule = typeof import('exceljs');

async function loadExcel(): Promise<ExcelModule> {
  const loaded = (await import('exceljs')) as ExcelModule & { readonly default?: ExcelModule };
  return loaded.default ?? loaded;
}

/** The workbook of a table: one sheet named after the list, the header row bold and frozen, typed cells, right-to-left in Arabic. */
export async function buildTableWorkbook(table: TableExport, texts: ExportTexts): Promise<Blob> {
  const excel = await loadExcel();
  const workbook = new excel.Workbook();
  workbook.created = new Date();
  const sheet = workbook.addWorksheet(safeSheetName(table.title), { views: [{ state: 'frozen', ySplit: 1, rightToLeft: texts.lang === 'ar' }] });
  sheet.columns = table.columns.map((column, index) => ({
    header: column.header,
    key: `c${index}`,
    width: Math.min(50, Math.max(10, column.header.length + 2, ...table.rows.map((row) => exportText(row[index], texts).length + 2))),
  }));
  sheet.getRow(1).font = { bold: true };

  for (const row of table.rows) {
    const added = sheet.addRow(table.columns.map((_, index) => excelValue(row[index], texts).value));
    table.columns.forEach((column, index) => {
      const { numFmt } = excelValue(row[index], texts);
      const cell = added.getCell(index + 1);
      if (numFmt !== null) {
        cell.numFmt = numFmt;
      }

      if (column.align !== 'start') {
        cell.alignment = { horizontal: column.align === 'end' ? 'right' : 'center' };
      }
    });
  }

  const content = await workbook.xlsx.writeBuffer();
  return new Blob([content as unknown as ArrayBuffer], { type: XLSX_MIME });
}

/** Hands a blob to the browser's download through a temporary object URL (no-op without object URLs, e.g. tests). */
export function saveBlob(document: Document, blob: Blob, fileName: string): void {
  const view = document.defaultView;
  if (!view?.URL.createObjectURL) {
    return;
  }

  const url = view.URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.rel = 'noopener';
  anchor.style.display = 'none';
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  view.setTimeout(() => view.URL.revokeObjectURL(url), 0);
}
