import { daysToMask, maskToDays, numberOrNull, textOrNull } from './catalogue-a.mapping';
import { csvCell, csvFileName, toCsv } from './csv-export';

describe('catalogue A data helpers', () => {
  it('writes CSV cells with quotes, doubled quotes and a guard against spreadsheet formulas', () => {
    expect(csvCell(null)).toBe('');
    expect(csvCell(12.5)).toBe('12.5');
    expect(csvCell(-3)).toBe('-3');
    expect(csvCell(true)).toBe('true');
    expect(csvCell('Glucose, fasting')).toBe('"Glucose, fasting"');
    expect(csvCell('say "hi"')).toBe('"say ""hi"""');
    expect(csvCell('=HYPERLINK("x")')).toBe(`"'=HYPERLINK(""x"")"`);
    expect(csvCell('@SUM(A1)')).toBe("'@SUM(A1)");
    expect(csvCell('جلوكوز')).toBe('جلوكوز');
  });

  it('builds the file with a byte-order mark, a header row and CRLF lines', () => {
    const csv = toCsv([{ code: 'GLU', name: 'Glucose' }], [
      { header: 'Code', value: (row) => row.code },
      { header: 'Name', value: (row) => row.name },
    ]);

    expect(csv.charCodeAt(0)).toBe(0xfeff);
    expect(csv.slice(1)).toBe('Code,Name\r\nGLU,Glucose\r\n');
    expect(csvFileName('tests', new Date('2026-10-09T10:00:00Z'))).toBe('tests-2026-10-09.csv');
  });

  it('turns the weekday mask into seven flags and back (bit 0 = Sunday)', () => {
    expect(maskToDays(127)).toEqual([true, true, true, true, true, true, true]);
    expect(maskToDays(0b0011111)).toEqual([true, true, true, true, true, false, false]);
    expect(daysToMask([true, false, false, false, false, false, true])).toBe(65);
    expect(daysToMask(maskToDays(42))).toBe(42);
  });

  it('sends empty texts and numbers as null', () => {
    expect(textOrNull('  ')).toBeNull();
    expect(textOrNull(' mg/dL ')).toBe('mg/dL');
    expect(numberOrNull('')).toBeNull();
    expect(numberOrNull(null)).toBeNull();
    expect(numberOrNull(0)).toBe(0);
    expect(numberOrNull('12.5')).toBe(12.5);
    expect(numberOrNull('abc')).toBeNull();
  });
});
