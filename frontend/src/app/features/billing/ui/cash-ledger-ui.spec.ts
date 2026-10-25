import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { FormControl, FormGroup } from '@angular/forms';

import type { PostCashEntryRequest } from '../../../core/api/billing.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PrintService } from '../../../shared/print/print.service';
import { buildLedgerWorkbook, ledgerFileName, ledgerSheetRows, loadExcel, localStamp } from '../data/cash-ledger-excel';
import { ledgerRow, ledgerView, methodOptions, preview } from '../testing/billing.fixtures';
import { CashEntryDialogComponent } from './cash-entry-dialog.component';
import { canVoidLine, ledgerDetail, ledgerScopeLabel, ledgerTone } from './cash-ledger-view';
import { type DayCloseInput, DayCloseDialogComponent } from './day-close-dialog.component';
import { CashSheetDocumentComponent } from './documents/cash-sheet-document.component';

interface EntryInternals {
  readonly form: FormGroup<{
    category: FormControl<string>;
    amount: FormControl<number | null>;
    methodKey: FormControl<string>;
    reference: FormControl<string>;
    reason: FormControl<string>;
    note: FormControl<string>;
  }>;
  readonly categories: () => readonly string[];
  readonly referenceRequired: () => boolean;
  onMethodChange(): void;
  submit(): void;
}

interface CloseInternals {
  readonly form: FormGroup<{ countedCash: FormControl<number | null>; note: FormControl<string> }>;
  readonly difference: () => number | null;
  readonly noteMissing: () => boolean;
  readonly ready: () => boolean;
  submit(): void;
}

describe('Cash ledger UI (CP-70, LIS-CR-011)', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] }));

  it('the entry dialog offers the categories of its kind, asks a reference only when the method needs it and emits the request', () => {
    const fixture = TestBed.createComponent(CashEntryDialogComponent);
    const sent: PostCashEntryRequest[] = [];
    fixture.componentInstance.submitted.subscribe((request) => sent.push(request));
    fixture.componentRef.setInput('methods', methodOptions);
    fixture.componentRef.setInput('kind', 'CashIn');
    fixture.componentInstance.open.set(true);
    TestBed.tick();
    const dialog = fixture.componentInstance as unknown as EntryInternals;

    expect(dialog.categories()).toEqual(['OpeningFloat', 'OtherIncome', 'Other']);
    expect(dialog.form.controls.category.value).toBe('OpeningFloat');
    expect(dialog.form.controls.methodKey.value).toBe('CASH');
    expect(dialog.referenceRequired()).toBe(false);

    dialog.submit();
    expect(sent).toEqual([]);   // amount and reason are required

    dialog.form.patchValue({ amount: 200, reason: '  Morning float ' });
    dialog.submit();
    expect(sent[0]).toEqual({ kind: 'CashIn', category: 'OpeningFloat', amount: 200, reason: 'Morning float', method: 'Cash', methodCode: 'CASH', reference: null, note: null });

    fixture.componentRef.setInput('kind', 'CashOut');
    TestBed.tick();
    expect(dialog.categories()).toEqual(['Supplier', 'PettyCash', 'BankDeposit', 'Other']);
    dialog.form.patchValue({ category: 'BankDeposit', amount: 1000, reason: 'Deposit', methodKey: 'TRANSFER' });
    dialog.onMethodChange();
    expect(dialog.referenceRequired()).toBe(true);
    dialog.submit();
    expect(sent).toHaveLength(1);   // the deposit slip is missing
    dialog.form.patchValue({ reference: 'DEP-7', note: 'Bank Misr' });
    dialog.submit();
    expect(sent[1]).toEqual({ kind: 'CashOut', category: 'BankDeposit', amount: 1000, reason: 'Deposit', method: 'Transfer', methodCode: 'TRANSFER', reference: 'DEP-7', note: 'Bank Misr' });
  });

  it('the entry dialog without definitions runs on the four kinds with the legacy reference rule', () => {
    const fixture = TestBed.createComponent(CashEntryDialogComponent);
    const sent: PostCashEntryRequest[] = [];
    fixture.componentInstance.submitted.subscribe((request) => sent.push(request));
    fixture.componentInstance.open.set(true);
    TestBed.tick();
    const dialog = fixture.componentInstance as unknown as EntryInternals;

    expect(dialog.form.controls.methodKey.value).toBe('Cash');
    dialog.form.patchValue({ amount: 12.5, reason: 'Pens' });
    dialog.submit();
    expect(sent[0]).toEqual({ kind: 'CashOut', category: 'Supplier', amount: 12.5, reason: 'Pens', method: 'Cash', reference: null, note: null });
    expect(sent[0]).not.toHaveProperty('methodCode');
  });

  it('the day close dialog shows the expected cash, needs a note for a difference (BIL-034) and emits the count', () => {
    const fixture = TestBed.createComponent(DayCloseDialogComponent);
    const closes: DayCloseInput[] = [];
    fixture.componentInstance.closeRequested.subscribe((input) => closes.push(input));
    const drawer = preview({ cashierUserId: 'u-1', totals: { ...preview().totals, openingBalance: 100, cashIn: 0, cashOut: 40, expectedCash: 360 } });
    fixture.componentRef.setInput('preview', drawer);
    fixture.componentRef.setInput('cashierLabel', 'Cashier One');
    fixture.componentInstance.open.set(true);
    TestBed.tick();
    fixture.detectChanges();
    const dialog = fixture.componentInstance as unknown as CloseInternals;
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('360.00');

    dialog.form.controls.countedCash.setValue(350);
    TestBed.tick();
    expect(dialog.difference()).toBe(-10);
    expect(dialog.noteMissing()).toBe(true);
    dialog.submit();
    expect(closes).toEqual([]);

    dialog.form.controls.note.setValue('10 short');
    TestBed.tick();
    dialog.submit();
    expect(closes).toEqual([{ countedCash: 350, note: '10 short' }]);

    fixture.componentRef.setInput('preview', { ...drawer, overlapsClosedPeriod: true });
    TestBed.tick();
    expect(dialog.ready()).toBe(false);
  });

  it('names each line, its scope and its tone; only a posted cash entry offers a void', () => {
    const i18n = TestBed.inject(I18nService);
    expect(ledgerDetail(i18n, ledgerRow())).toBe('ACC-2026-00001 · billing.method.Cash');
    expect(ledgerDetail(i18n, ledgerRow({ method: 'Card', methodCode: 'VISA' }))).toBe('ACC-2026-00001 · billing.method.Card · VISA');
    expect(ledgerDetail(i18n, ledgerRow({ kind: 'CashOut', visitId: null, accession: null, category: 'Supplier', text: 'Courier' }))).toBe('billing.ledger.category.Supplier · Courier');
    expect(ledgerDetail(i18n, ledgerRow({ kind: 'Closing', expected: 300, counted: 295, text: '5 short' }))).toContain('5 short');
    expect(ledgerScopeLabel(i18n, ledgerView())).toBe('billing.ledger.scopeUser · Cashier One');
    expect(ledgerScopeLabel(i18n, ledgerView({ scope: 'Branch', cashierUserId: null, cashierName: null }))).toBe('billing.ledger.scopeBranch · billing.closing.allCashiers');

    expect(ledgerTone(ledgerRow())).toBe('ok');
    expect(ledgerTone(ledgerRow({ kind: 'CashOut' }))).toBe('warn');
    expect(ledgerTone(ledgerRow({ kind: 'Closing' }))).toBe('info');
    expect(ledgerTone(ledgerRow({ status: 'Voided' }))).toBe('neutral');
    expect(canVoidLine(ledgerRow({ kind: 'CashIn' }))).toBe(true);
    expect(canVoidLine(ledgerRow({ kind: 'CashOut', status: 'Voided' }))).toBe(false);
    expect(canVoidLine(ledgerRow())).toBe(false);
  });

  it('renders the Daily cash sheet with the opening, the figures, every line and its balance, in Arabic too', () => {
    const print = TestBed.inject(PrintService);
    const html = print.render({ type: CashSheetDocumentComponent, inputs: { doc: ledgerView() } });
    expect(html).toContain('Sky Lab');
    expect(html).toContain('CSH-2026-00001');
    expect(html).toContain('360.00');
    expect(html).toContain('2026-10-09');

    TestBed.inject(I18nService).setLanguage('ar');
    try {
      const arabic = print.render({ type: CashSheetDocumentComponent, inputs: { doc: ledgerView() } });
      expect(arabic).toContain('مختبر سكاي');
      expect(arabic).toContain('الفرع الرئيسي');
    } finally {
      TestBed.inject(I18nService).setLanguage('en');
    }
  });

  it('exports the ledger to Excel: opening, lines and closing with money in, out and the balance', async () => {
    const i18n = TestBed.inject(I18nService);
    const rows = ledgerSheetRows(i18n, ledgerView(), () => 'T');
    expect(rows.map((row) => [row[1], row[5], row[6], row[7]])).toEqual([
      ['billing.ledger.opening', null, null, 100],
      ['billing.ledger.kind.Receipt', 300, null, 400],
      ['billing.ledger.kind.CashOut', null, 40, 360],
      ['billing.ledger.closingBalance', null, null, 360],
    ]);
    expect(ledgerFileName(ledgerView())).toBe('cash-ledger-2026-10-09.xlsx');
    expect(ledgerFileName(ledgerView({ fromDay: '2026-10-01' }))).toBe('cash-ledger-2026-10-01-2026-10-09.xlsx');
    expect(localStamp('not a date')).toBe('');

    const blob = await buildLedgerWorkbook(i18n, ledgerView());
    const excel = await loadExcel();
    const workbook = new excel.Workbook();
    await workbook.xlsx.load((await blob.arrayBuffer()) as never);
    expect(workbook.worksheets.map((sheet) => sheet.name)).toEqual(['Ledger', 'Summary']);
    const ledger = workbook.getWorksheet('Ledger');
    expect(ledger?.getRow(4).getCell(8).value).toBe('billing.ledger.balance');
    expect(ledger?.getRow(6).getCell(3).value).toBe('RCT-2026-00001');
    expect(ledger?.getRow(8).getCell(8).value).toBe(360);
  });
});
