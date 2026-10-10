import { csvFileName, toCsv } from './csv';
import { formatAmount, hasScale, round2, roundToStep } from './money';
import { normalize } from './service-catalog.api';

describe('pricing data helpers', () => {
  it('writes RFC 4180 CSV with a BOM, quoting and a CSV-injection guard', () => {
    const csv = toCsv(['Code', 'Service'], [
      ['CBC', 'Complete blood count'],
      ['LIP', 'Lipid, full "panel"'],
      ['=X', 'تحليل الدهون'],
      ['N', 12.5],
      ['B', true],
      ['E', null],
    ]);

    expect(csv.startsWith('﻿')).toBe(true);
    const lines = csv.slice(1).split('\r\n');
    expect(lines[0]).toBe('Code,Service');
    expect(lines[2]).toBe('LIP,"Lipid, full ""panel"""');
    expect(lines[3]).toBe("'=X,تحليل الدهون");
    expect(lines[4]).toBe('N,12.5');
    expect(lines[5]).toBe('B,true');
    expect(lines[6]).toBe('E,');
    expect(lines[7]).toBe('');
  });

  it('builds a safe file name from a code and the date', () => {
    expect(csvFileName('PL26 prices/2026', new Date('2026-10-09T10:00:00Z'))).toBe('pl26-prices-2026-2026-10-09.csv');
    expect(csvFileName('***', new Date('2026-10-09T10:00:00Z'))).toBe('export-2026-10-09.csv');
  });

  it('rounds like the server: two decimals and steps, half away from zero', () => {
    expect(round2(1.005)).toBe(1.01);
    expect(round2(-1.005)).toBe(-1.01);
    expect(roundToStep(12.37, 0.25)).toBe(12.25);
    expect(roundToStep(12.38, 0.25)).toBe(12.5);
    expect(roundToStep(99.5, 1)).toBe(100);
    expect(roundToStep(198.2, 0.5)).toBe(198);
    expect(hasScale(10.25, 2)).toBe(true);
    expect(hasScale(10.255, 2)).toBe(false);
    expect(hasScale(0.1234, 4)).toBe(true);
    expect(formatAmount(1234.5, 'en')).toBe('1,234.50');
  });

  it('normalises both shapes of the catalogue lookup and both code spellings', () => {
    expect(normalize([{ id: 'a', code: 'CBC', nameEn: 'Blood count', nameAr: 'صورة دم' }, { id: '', code: 'X' }])).toEqual([{ id: 'a', code: 'CBC', nameEn: 'Blood count', nameAr: 'صورة دم' }]);
    expect(normalize({ items: [{ id: 'b', shortName: 'TSH', nameEn: 'TSH', nameAr: null }], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 })).toEqual([
      { id: 'b', code: 'TSH', nameEn: 'TSH', nameAr: 'TSH' },
    ]);
    expect(normalize(null)).toEqual([]);
  });
});
