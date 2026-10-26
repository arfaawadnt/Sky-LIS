import { buildTableWorkbook, dateCell, excelLocalDate, excelValue, exportFileName, exportText, moneyCell, safeSheetName, type TableExport } from './table-export';

const EN = { lang: 'en', yes: 'Yes', no: 'No' } as const;

describe('table export (LIS-CR-014 T4)', () => {
  it('prints a cell as text: typed cells by their text, empties as a dash, booleans as yes / no', () => {
    expect(exportText({ value: 12.5, text: '12.50' }, EN)).toBe('12.50');
    expect(exportText(null, EN)).toBe('—');
    expect(exportText('', EN)).toBe('—');
    expect(exportText(true, EN)).toBe('Yes');
    expect(exportText(1234.5, EN)).toBe('1,234.5');
    expect(exportText('V-1', EN)).toBe('V-1');
  });

  it('stores numbers as numbers and dates as local dates with a format', () => {
    const money = moneyCell(150, '150.00');
    expect(excelValue(money, EN)).toEqual({ value: 150, numFmt: '#,##0.00' });
    expect(moneyCell(null, '—')).toBeNull();

    const when = dateCell('2026-10-25T07:30:00Z', '25 Oct 2026, 10:30');
    const stored = excelValue(when, EN);
    expect(stored.numFmt).toBe('yyyy-mm-dd hh:mm');
    expect(stored.value).toEqual(excelLocalDate(new Date('2026-10-25T07:30:00Z')));
    expect(dateCell('not a date', 'x')).toBeNull();
    expect(excelValue(false, EN)).toEqual({ value: 'No', numFmt: null });
  });

  it('names the sheet and the file safely', () => {
    expect(safeSheetName('Search registrations: [today]/all?')).toBe('Search registrations today all');
    expect(safeSheetName('   ')).toBe('Sheet1');
    expect(safeSheetName('x'.repeat(40)).length).toBe(31);
    expect(exportFileName('Search registrations', '2026-10-25', 'xlsx')).toBe('Search-registrations-2026-10-25.xlsx');
    expect(exportFileName('متابعة المختبر', '2026-10-25', 'xlsx')).toBe('متابعة-المختبر-2026-10-25.xlsx');
    expect(exportFileName('', '2026-10-25', 'xlsx')).toBe('list-2026-10-25.xlsx');
  });

  it('builds one sheet named after the list with a bold header and typed cells', async () => {
    const table: TableExport = {
      title: 'Search registrations',
      subtitle: null,
      columns: [
        { key: 'accession', header: 'Accession', align: 'start' },
        { key: 'date', header: 'Date', align: 'start' },
        { key: 'share', header: 'Patient share', align: 'end' },
      ],
      rows: [
        ['A-1', dateCell('2026-10-25T07:30:00Z', '25 Oct'), moneyCell(150, '150.00')],
        ['A-2', null, 75],
      ],
    };

    const blob = await buildTableWorkbook(table, EN);
    expect(blob.type).toContain('spreadsheetml');

    const loaded = (await import('exceljs')) as typeof import('exceljs') & { readonly default?: typeof import('exceljs') };
    const excel = loaded.default ?? loaded;
    const workbook = new excel.Workbook();
    await workbook.xlsx.load((await blob.arrayBuffer()) as never);
    const sheet = workbook.worksheets[0]!;
    expect(sheet.name).toBe('Search registrations');
    expect(sheet.getRow(1).getCell(1).value).toBe('Accession');
    expect(sheet.getRow(1).font?.bold).toBe(true);
    expect(sheet.getRow(2).getCell(3).value).toBe(150);
    expect(sheet.getRow(2).getCell(3).numFmt).toBe('#,##0.00');
    expect(sheet.getRow(2).getCell(2).value).toBeInstanceOf(Date);
    expect(sheet.getRow(3).getCell(3).value).toBe(75);
    expect(sheet.rowCount).toBe(3);
  });
});
