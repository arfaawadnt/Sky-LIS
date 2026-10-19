import { PriceSheetError, buildPriceWorkbook, cellNumber, cellText, loadExcel, mapPriceRows, parsePriceWorkbook, safeSheetName, xlsxFileName } from './excel';

/** A workbook built in memory with exceljs, as a user would upload it. */
async function workbookBytes(sheetName: string, rows: readonly (readonly unknown[])[]): Promise<ArrayBuffer> {
  const excel = await loadExcel();
  const workbook = new excel.Workbook();
  const sheet = workbook.addWorksheet(sheetName);
  for (const row of rows) {
    sheet.addRow([...row]);
  }

  const content = await workbook.xlsx.writeBuffer();
  return toArrayBuffer(content);
}

function toArrayBuffer(content: unknown): ArrayBuffer {
  if (content instanceof ArrayBuffer) {
    return content;
  }

  const view = content as Uint8Array;
  return view.buffer.slice(view.byteOffset, view.byteOffset + view.byteLength) as ArrayBuffer;
}

describe('Excel round trip of the price list editor (C21)', () => {
  it('maps the rows below the header by column name, case-insensitively, keeping the sheet row numbers', () => {
    const parsed = mapPriceRows([
      ['Main price list 2026'],
      [],
      ['code', 'Name', 'CURRENT PRICE', 'new PRICE'],
      ['CBC', 'Complete blood count', 180, 190],
      ['LIP', 'Lipid profile', 300, '1,250.50'],
      ['', '', null, null],
      ['TSH', 'Thyroid', 90, ''],
      [null, 'no code', 10, 12],
    ]);

    expect(parsed.headerRow).toBe(3);
    expect(parsed.skipped).toBe(1);
    expect(parsed.rows).toEqual([
      { row: 4, serviceCode: 'CBC', price: 190 },
      { row: 5, serviceCode: 'LIP', price: 1250.5 },
      { row: 7, serviceCode: 'TSH', price: null },
      { row: 8, serviceCode: null, price: 12 },
    ]);
  });

  it('accepts a plain "Price" header when there is no "New price", and refuses a sheet without a header', () => {
    expect(mapPriceRows([['Code', 'Price'], ['GLU', 55]]).rows).toEqual([{ row: 2, serviceCode: 'GLU', price: 55 }]);
    expect(mapPriceRows([['Service code', 'Current price', 'Price'], ['GLU', 50, 55]]).rows).toEqual([{ row: 2, serviceCode: 'GLU', price: 55 }]);
    expect(() => mapPriceRows([['Code', 'Current price'], ['GLU', 55]])).toThrow(PriceSheetError);
    expect(() => mapPriceRows([])).toThrow(expect.objectContaining({ fault: 'noHeader' }));
  });

  it('reads exceljs cell shapes: formulas, rich text, hyperlinks, numeric text, dates', () => {
    expect(cellText({ richText: [{ text: 'CB' }, { text: 'C' }] })).toBe('CBC');
    expect(cellText({ formula: 'A1', result: 'GLU' })).toBe('GLU');
    expect(cellText({ text: 'LIP', hyperlink: 'https://example.test' })).toBe('LIP');
    expect(cellText(12)).toBe('12');
    expect(cellText('  ')).toBeNull();
    expect(cellNumber({ formula: 'C2*1.1', result: 198 })).toBe(198);
    expect(cellNumber(' 1 234.50 ')).toBe(1234.5);
    expect(cellNumber('abc')).toBeNull();
    expect(cellNumber({ error: '#REF!' })).toBeNull();
    expect(cellNumber(Number.NaN)).toBeNull();
  });

  it('downloads the lines as a sheet named after the list and reads the same file back', async () => {
    const blob = await buildPriceWorkbook('PL26', [
      { code: 'CBC', name: 'Complete blood count', price: 180 },
      { code: 'LIP', name: 'Lipid profile', price: 300.5 },
    ]);
    expect(blob.type).toContain('spreadsheetml');

    const parsed = await parsePriceWorkbook(await blob.arrayBuffer());
    expect(parsed.sheetName).toBe('PL26');
    expect(parsed.headerRow).toBe(1);
    // the "New price" column is empty on purpose: the user fills it in
    expect(parsed.rows).toEqual([
      { row: 2, serviceCode: 'CBC', price: null },
      { row: 3, serviceCode: 'LIP', price: null },
    ]);
  });

  it('parses a user workbook with filled prices and reports a workbook without a usable sheet', async () => {
    const filled = await parsePriceWorkbook(await workbookBytes('Prices', [['Code', 'Name', 'New price'], ['CBC', 'Complete blood count', 195], ['GLU', 'Glucose', '60']]));
    expect(filled.rows).toEqual([
      { row: 2, serviceCode: 'CBC', price: 195 },
      { row: 3, serviceCode: 'GLU', price: 60 },
    ]);

    await expect(parsePriceWorkbook(await workbookBytes('Notes', [['Just text']]))).rejects.toMatchObject({ fault: 'noHeader' });
    await expect(parsePriceWorkbook(new TextEncoder().encode('not a workbook').buffer as ArrayBuffer)).rejects.toMatchObject({ fault: 'unreadable' });
  });

  it('names sheets and files safely', () => {
    expect(safeSheetName('PL/26:[A]*?')).toBe('PL-26--A---');
    expect(safeSheetName('')).toBe('Prices');
    expect(safeSheetName('X'.repeat(40)).length).toBe(31);
    expect(xlsxFileName('PL26-prices', new Date(2026, 9, 19))).toBe('pl26-prices-2026-10-19.xlsx');
  });
});
