import type { ImportPriceRowRequest } from '../../../core/api/pricing.contracts';

/**
 * The Excel round trip of the price list editor (CR 2026-10-19 C21): *Download Excel* writes the list's lines with an
 * empty "New price" column, *Upload Excel* reads the first sheet back by header names. `exceljs` is CommonJS and heavy,
 * so it is loaded on demand (`await import`) inside the action and never reaches the eager bundle.
 */

/** The fixed English headers of the template (the upload matches them case-insensitively; Arabic spellings are accepted too). */
export const EXCEL_HEADERS = ['Code', 'Name', 'Current price', 'New price'] as const;

export const XLSX_MIME = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';

/** One line as the download writes it. */
export interface PriceSheetLine {
  readonly code: string;
  readonly name: string;
  readonly price: number;
}

/** What the upload parsed: the data rows (sheet row numbers kept for the error list), the header row and the rows skipped for being empty. */
export interface ParsedPriceRows {
  readonly rows: readonly ImportPriceRowRequest[];
  readonly headerRow: number;
  readonly skipped: number;
}

export interface ParsedPriceSheet extends ParsedPriceRows {
  readonly sheetName: string;
}

export type PriceSheetFault = 'noSheet' | 'noHeader' | 'noRows' | 'unreadable';

/** The workbook could not be used: no sheet, no recognisable header row, nothing below it, or not an .xlsx at all. */
export class PriceSheetError extends Error {
  constructor(readonly fault: PriceSheetFault) {
    super(fault);
    this.name = 'PriceSheetError';
  }
}

type ExcelModule = typeof import('exceljs');

/** The lazily loaded module, whatever interop shape the bundler gives a CommonJS package (`default` or the namespace). */
export async function loadExcel(): Promise<ExcelModule> {
  const loaded = (await import('exceljs')) as ExcelModule & { readonly default?: ExcelModule };
  return loaded.default ?? loaded;
}

/** The lines of a list as an `.xlsx` blob: one sheet named after the list code, bold headers, prices formatted with two decimals. */
export async function buildPriceWorkbook(sheetName: string, lines: readonly PriceSheetLine[]): Promise<Blob> {
  const excel = await loadExcel();
  const workbook = new excel.Workbook();
  const sheet = workbook.addWorksheet(safeSheetName(sheetName));
  sheet.columns = [
    { header: EXCEL_HEADERS[0], key: 'code', width: 16 },
    { header: EXCEL_HEADERS[1], key: 'name', width: 48 },
    { header: EXCEL_HEADERS[2], key: 'current', width: 16 },
    { header: EXCEL_HEADERS[3], key: 'new', width: 16 },
  ];
  sheet.getRow(1).font = { bold: true };
  sheet.getColumn('current').numFmt = '#,##0.00';
  sheet.getColumn('new').numFmt = '#,##0.00';
  for (const line of lines) {
    sheet.addRow({ code: line.code, name: line.name, current: line.price, new: null });
  }

  const content = await workbook.xlsx.writeBuffer();
  return new Blob([content as unknown as ArrayBuffer], { type: XLSX_MIME });
}

/** Reads the first sheet of an uploaded workbook into import rows (see `mapPriceRows`). */
export async function parsePriceWorkbook(data: ArrayBuffer): Promise<ParsedPriceSheet> {
  const excel = await loadExcel();
  const workbook = new excel.Workbook();
  try {
    await workbook.xlsx.load(data as never);
  } catch {
    throw new PriceSheetError('unreadable');
  }

  const sheet = workbook.worksheets[0];
  if (sheet === undefined) {
    throw new PriceSheetError('noSheet');
  }

  const grid: unknown[][] = [];
  sheet.eachRow({ includeEmpty: true }, (row, number) => {
    const values = Array.isArray(row.values) ? row.values : [];
    grid[number - 1] = values.slice(1);   // exceljs row values are 1-based: index 0 is always empty
  });
  for (let index = 0; index < grid.length; index++) {
    grid[index] ??= [];
  }

  return { ...mapPriceRows(grid), sheetName: sheet.name };
}

const CODE_HEADER = /^(code|service code|الكود|الرمز)$/i;
const NEW_PRICE_HEADER = /^(new price|السعر الجديد)$/i;
const PRICE_HEADER = /^(price|السعر)$/i;
const HEADER_SEARCH_ROWS = 10;

/**
 * Pure mapping of a sheet (rows of cell values, row 1 first) to import rows: the header row is the first of the first ten
 * rows holding a "Code" cell and a "New price" (else "Price") cell, matched case-insensitively after trimming; every row
 * below becomes `{ row, serviceCode, price }` with the sheet row number. Fully empty rows are skipped (counted); a row with
 * a code and an empty or non-numeric price keeps `price: null` so the server reports it (PRC-006). Throws `noHeader`.
 */
export function mapPriceRows(grid: readonly (readonly unknown[])[]): ParsedPriceRows {
  const header = findHeader(grid);
  if (header === null) {
    throw new PriceSheetError('noHeader');
  }

  const rows: ImportPriceRowRequest[] = [];
  let skipped = 0;
  for (let index = header.row; index < grid.length; index++) {
    const cells = grid[index] ?? [];
    const code = cellText(cells[header.code]);
    const priceCell = cells[header.price];
    if (code === null && isBlank(priceCell)) {
      skipped++;
      continue;
    }

    rows.push({ row: index + 1, serviceCode: code, price: cellNumber(priceCell) });
  }

  return { rows, headerRow: header.row, skipped };
}

function findHeader(grid: readonly (readonly unknown[])[]): { row: number; code: number; price: number } | null {
  for (let index = 0; index < Math.min(grid.length, HEADER_SEARCH_ROWS); index++) {
    const cells = (grid[index] ?? []).map((cell) => cellText(cell) ?? '');
    const code = cells.findIndex((cell) => CODE_HEADER.test(cell));
    const newPrice = cells.findIndex((cell) => NEW_PRICE_HEADER.test(cell));
    const price = newPrice >= 0 ? newPrice : cells.findIndex((cell) => PRICE_HEADER.test(cell));
    if (code >= 0 && price >= 0 && price !== code) {
      return { row: index + 1, code, price };
    }
  }

  return null;
}

/** The text of a cell: strings trimmed, numbers as typed, formula results and rich text unwrapped; empty → null. */
export function cellText(value: unknown): string | null {
  const plain = unwrap(value);
  if (plain === null || plain === undefined) {
    return null;
  }

  if (typeof plain === 'number') {
    return Number.isFinite(plain) ? String(plain) : null;
  }

  if (plain instanceof Date) {
    return plain.toISOString();
  }

  const text = String(plain).trim();
  return text === '' ? null : text;
}

/** The number of a cell: a numeric cell, or a numeric text (thousand separators and spaces, breaking or not, removed); otherwise null. */
export function cellNumber(value: unknown): number | null {
  const plain = unwrap(value);
  if (typeof plain === 'number') {
    return Number.isFinite(plain) ? plain : null;
  }

  if (typeof plain === 'string') {
    const text = plain.replace(/[,\s]/g, '');
    if (text === '' || !/^[+-]?\d+(\.\d+)?$/.test(text)) {
      return null;
    }

    const parsed = Number(text);
    return Number.isFinite(parsed) ? parsed : null;
  }

  return null;
}

function isBlank(value: unknown): boolean {
  return cellText(value) === null;
}

/** exceljs cell values: plain, `{ result }` (formula), `{ richText }`, `{ text }` (hyperlink), `{ error }`. */
function unwrap(value: unknown): unknown {
  if (value === null || value === undefined || typeof value !== 'object' || value instanceof Date) {
    return value;
  }

  const cell = value as { result?: unknown; richText?: readonly { text?: unknown }[]; text?: unknown; error?: unknown };
  if (cell.richText !== undefined) {
    return cell.richText.map((part) => (typeof part.text === 'string' ? part.text : '')).join('');
  }

  if (cell.result !== undefined) {
    return unwrap(cell.result);
  }

  if (cell.text !== undefined) {
    return unwrap(cell.text);
  }

  return null;
}

/** Excel sheet names: at most 31 characters, none of `[ ] : * ? / \`. */
export function safeSheetName(name: string): string {
  const cleaned = name.replace(/[[\]:*?/\\]/g, '-').trim();
  return (cleaned === '' ? 'Prices' : cleaned).slice(0, 31);
}

/** `pl26-prices-2026-10-19.xlsx`: a safe file name from the list code and the user's own date (as the CSV export names). */
export function xlsxFileName(stem: string, today: Date): string {
  const safe = stem.toLowerCase().replace(/[^a-z0-9._-]+/g, '-').replace(/^-+|-+$/g, '') || 'export';
  const pad = (value: number): string => String(value).padStart(2, '0');
  return `${safe}-${today.getFullYear()}-${pad(today.getMonth() + 1)}-${pad(today.getDate())}.xlsx`;
}
