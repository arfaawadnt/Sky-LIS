import type { CashLedgerView } from '../../../core/api/billing.contracts';
import type { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { ledgerDetail, ledgerScopeLabel } from '../ui/cash-ledger-view';

/**
 * The Excel export of the cash ledger (CP-70, change request LIS-CR-011): one sheet "Ledger" with the heading (laboratory,
 * branch, scope, period), the opening balance, every line with money in / out and the running balance, and the closing
 * balance; one sheet "Summary" with the figures. Headers in the page language. `exceljs` is CommonJS and heavy, so it is
 * loaded on demand (`await import`) inside the action and never reaches the eager bundle (the pattern of the price lists).
 */

export const XLSX_MIME = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';

type ExcelModule = typeof import('exceljs');

/** The lazily loaded module, whatever interop shape the bundler gives a CommonJS package (`default` or the namespace). */
export async function loadExcel(): Promise<ExcelModule> {
  const loaded = (await import('exceljs')) as ExcelModule & { readonly default?: ExcelModule };
  return loaded.default ?? loaded;
}

/** A cell row of the "Ledger" sheet: the texts and numbers the page shows, in the page language. */
export type LedgerSheetRow = readonly [string, string, string, string, string, number | null, number | null, number, string, string];

/** The data rows of the "Ledger" sheet (pure, so it is tested without a workbook): opening, the lines, closing. */
export function ledgerSheetRows(i18n: I18nService, view: CashLedgerView, now: (iso: string) => string = localStamp): readonly LedgerSheetRow[] {
  const opening: LedgerSheetRow = [now(view.fromUtc), i18n.t('billing.ledger.opening'), view.summary.openingClosingNumber ?? '', '', '', null, null, view.summary.openingBalance, '', ''];
  const lines = view.rows.map<LedgerSheetRow>((row) => [
    now(row.atUtc),
    i18n.t(`billing.ledger.kind.${row.kind}`),
    row.number,
    row.accession ?? '',
    ledgerDetail(i18n, row),
    row.moneyIn === 0 ? null : row.moneyIn,
    row.moneyOut === 0 ? null : row.moneyOut,
    row.balance,
    row.cashierName ?? '',
    i18n.t(`billing.status.${row.status}`),
  ]);
  const closing: LedgerSheetRow = [now(view.toUtc), i18n.t('billing.ledger.closingBalance'), '', '', '', null, null, view.summary.closingBalance, '', ''];
  return [opening, ...lines, closing];
}

/** The workbook as an `.xlsx` blob. */
export async function buildLedgerWorkbook(i18n: I18nService, view: CashLedgerView): Promise<Blob> {
  const excel = await loadExcel();
  const workbook = new excel.Workbook();
  const lang = i18n.lang();
  const lab = localizedPick({ en: view.letterhead.labNameEn, ar: view.letterhead.labNameAr }, lang);
  const branch = localizedPick({ en: view.letterhead.branchNameEn ?? '', ar: view.letterhead.branchNameAr ?? '' }, lang);
  const period = view.fromDay === view.toDay ? view.fromDay : `${view.fromDay} – ${view.toDay}`;

  const sheet = workbook.addWorksheet('Ledger', { views: [{ rightToLeft: i18n.dir() === 'rtl' }] });
  sheet.addRow([[lab, branch].filter((part) => part !== '').join(' · ')]).font = { bold: true };
  sheet.addRow([`${i18n.t('billing.ledger.title')} · ${ledgerScopeLabel(i18n, view)} · ${period} (${view.timeZone}) · ${view.currency}`]);
  sheet.addRow([]);
  const header = sheet.addRow([
    i18n.t('billing.doc.date'),
    i18n.t('billing.column.kind'),
    i18n.t('billing.column.number'),
    i18n.t('billing.column.accession'),
    i18n.t('billing.ledger.detail'),
    i18n.t('billing.ledger.in'),
    i18n.t('billing.ledger.out'),
    i18n.t('billing.ledger.balance'),
    i18n.t('billing.column.cashier'),
    i18n.t('billing.column.status'),
  ]);
  header.font = { bold: true };
  for (const row of ledgerSheetRows(i18n, view)) {
    sheet.addRow([...row]);
  }

  const widths = [18, 14, 18, 16, 48, 14, 14, 14, 22, 12];
  widths.forEach((width, index) => {
    sheet.getColumn(index + 1).width = width;
  });
  for (const column of [6, 7, 8]) {
    sheet.getColumn(column).numFmt = '#,##0.00';
  }

  const summary = workbook.addWorksheet('Summary', { views: [{ rightToLeft: i18n.dir() === 'rtl' }] });
  summary.getColumn(1).width = 36;
  summary.getColumn(2).width = 16;
  summary.getColumn(2).numFmt = '#,##0.00';
  const figures: readonly (readonly [string, number])[] = [
    [i18n.t('billing.ledger.opening'), view.summary.openingBalance],
    [`${i18n.t('billing.doc.payments')} · ${i18n.t('billing.method.Cash')}`, view.summary.receipts.cash],
    [`${i18n.t('billing.doc.payments')} · ${i18n.t('billing.method.Card')}`, view.summary.receipts.card],
    [`${i18n.t('billing.doc.payments')} · ${i18n.t('billing.method.Pos')}`, view.summary.receipts.pos],
    [`${i18n.t('billing.doc.payments')} · ${i18n.t('billing.method.Transfer')}`, view.summary.receipts.transfer],
    [`${i18n.t('billing.doc.refunds')} · ${i18n.t('billing.method.Cash')}`, view.summary.refunds.cash],
    [i18n.t('billing.doc.refunds'), view.summary.refunds.total],
    [i18n.t('billing.ledger.cashIn'), view.summary.cashIn],
    [i18n.t('billing.ledger.cashOut'), view.summary.cashOut],
    [i18n.t('billing.ledger.countDifferences'), view.summary.countDifferences],
    [i18n.t('billing.ledger.voided'), view.summary.voidedAmount],
    [i18n.t('billing.ledger.closingBalance'), view.summary.closingBalance],
  ];
  for (const [label, value] of figures) {
    summary.addRow([label, value]);
  }

  const content = await workbook.xlsx.writeBuffer();
  return new Blob([content as unknown as ArrayBuffer], { type: XLSX_MIME });
}

/** `cash-ledger-2026-10-24.xlsx` (or `…-2026-10-01-2026-10-24.xlsx` for a range). */
export function ledgerFileName(view: Pick<CashLedgerView, 'fromDay' | 'toDay'>): string {
  return view.fromDay === view.toDay ? `cash-ledger-${view.fromDay}.xlsx` : `cash-ledger-${view.fromDay}-${view.toDay}.xlsx`;
}

/** `2026-10-24 14:05` in the browser's zone, as the page shows the times. */
export function localStamp(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return '';
  }

  const pad = (value: number): string => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
