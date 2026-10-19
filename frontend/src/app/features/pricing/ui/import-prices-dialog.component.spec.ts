import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { ImportPriceLinesResult, ImportPriceRowRequest } from '../../../core/api/pricing.contracts';
import { loadExcel } from '../data/excel';
import { ImportPricesDialogComponent } from './import-prices-dialog.component';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

/** A workbook as a user would upload it: the given rows on one sheet, as a File whose bytes the dialog reads. */
async function workbookFile(name: string, rows: readonly (readonly unknown[])[]): Promise<File> {
  const excel = await loadExcel();
  const workbook = new excel.Workbook();
  const sheet = workbook.addWorksheet('PL26');
  for (const row of rows) {
    sheet.addRow([...row]);
  }

  const content = await workbook.xlsx.writeBuffer();
  const view = content instanceof ArrayBuffer ? new Uint8Array(content) : (content as Uint8Array);
  const bytes = view.buffer.slice(view.byteOffset, view.byteOffset + view.byteLength) as ArrayBuffer;
  const file = new File([bytes], name);
  // jsdom's File has no arrayBuffer(): the dialog reads the bytes through it
  Object.defineProperty(file, 'arrayBuffer', { value: () => Promise.resolve(bytes) });
  return file;
}

describe('Upload Excel dialog (C21)', () => {
  async function render() {
    await TestBed.configureTestingModule({ imports: [ImportPricesDialogComponent], providers: [provideZonelessChangeDetection()] }).compileComponents();
    const fixture = TestBed.createComponent(ImportPricesDialogComponent);
    fixture.componentRef.setInput('listCode', 'PL26');
    fixture.componentInstance.open.set(true);
    TestBed.tick();
    await settle();
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const apply = [...host.querySelectorAll<HTMLButtonElement>('button')].find((button) => button.textContent?.includes('pricing.import.apply'));
    if (!apply) {
      throw new Error('apply button missing');
    }

    return { fixture, host, apply };
  }

  it('parses the chosen workbook, asks for a dry run, lists the refused rows, then applies once every row passed', async () => {
    const { fixture, host, apply } = await render();
    const checks: (readonly ImportPriceRowRequest[])[] = [];
    const applies: (readonly ImportPriceRowRequest[])[] = [];
    fixture.componentInstance.check.subscribe((rows) => checks.push(rows));
    fixture.componentInstance.apply.subscribe((rows) => applies.push(rows));

    await fixture.componentInstance.read(await workbookFile('pl26-prices.xlsx', [['Code', 'Name', 'Current price', 'New price'], ['CBC', 'Complete blood count', 180, 190], ['NOPE', 'Unknown', 1, 2]]));
    await settle();
    fixture.detectChanges();
    expect(checks).toEqual([[{ row: 2, serviceCode: 'CBC', price: 190 }, { row: 3, serviceCode: 'NOPE', price: 2 }]]);
    expect(host.textContent).toContain('pl26-prices.xlsx');
    expect(host.textContent).toContain('pricing.import.parsed');
    expect(apply.disabled).toBe(true);

    const refused: ImportPriceLinesResult = { accepted: 1, added: 1, changed: 0, applied: false, errors: [{ row: 3, serviceCode: 'NOPE', code: 'PRC-015', message: 'unknown' }] };
    fixture.componentRef.setInput('result', refused);
    TestBed.tick();
    fixture.detectChanges();
    const errorRow = host.querySelector('tbody tr');
    const cells = [...(errorRow?.querySelectorAll('td') ?? [])].map((cell) => cell.textContent?.trim());
    expect(cells.slice(0, 2)).toEqual(['3', 'NOPE']);
    expect(cells[2]).toBeTruthy();   // the code's message (the generic text here: no dictionary is loaded in specs)
    expect(host.textContent).toContain('pricing.import.refused');
    expect(apply.disabled).toBe(true);

    fixture.componentRef.setInput('result', { accepted: 2, added: 1, changed: 1, applied: false, errors: [] } satisfies ImportPriceLinesResult);
    TestBed.tick();
    fixture.detectChanges();
    expect(host.textContent).toContain('pricing.import.ready');
    expect(apply.disabled).toBe(false);
    apply.click();
    await settle();
    expect(applies).toEqual([[{ row: 2, serviceCode: 'CBC', price: 190 }, { row: 3, serviceCode: 'NOPE', price: 2 }]]);
  });

  it('reports a sheet without a header row or without data rows, and resets when closed', async () => {
    const { fixture, host } = await render();
    const checks: (readonly ImportPriceRowRequest[])[] = [];
    fixture.componentInstance.check.subscribe((rows) => checks.push(rows));

    await fixture.componentInstance.read(await workbookFile('no-header.xlsx', [['Service', 'Amount'], ['CBC', 1]]));
    await settle();
    fixture.detectChanges();
    expect(host.querySelector('.problem')?.textContent).toContain('pricing.import.fault.noHeader');

    await fixture.componentInstance.read(await workbookFile('empty.xlsx', [['Code', 'New price']]));
    await settle();
    fixture.detectChanges();
    expect(host.querySelector('.problem')?.textContent).toContain('pricing.import.fault.noRows');
    expect(checks).toEqual([]);

    fixture.componentInstance.open.set(false);
    TestBed.tick();
    await settle();
    fixture.componentInstance.open.set(true);
    TestBed.tick();
    await settle();
    fixture.detectChanges();
    expect(host.querySelector('.problem')).toBeNull();
    expect(host.textContent).toContain('pricing.import.noFile');
  });
});
