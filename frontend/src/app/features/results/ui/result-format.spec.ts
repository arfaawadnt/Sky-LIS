import { ageText, delayClass, flagSymbol, formatDuration, initialDraft, localDayStart, logValueText, pickName, testLabel, toValueItem } from './result-format';
import { resultRow } from '../testing/results.fixtures';

describe('result-format', () => {
  it('maps flags to the report symbols and rows to delay classes', () => {
    expect(flagSymbol('High')).toBe('H');
    expect(flagSymbol('CriticalLow')).toBe('LL');
    expect(flagSymbol('Abnormal')).toBe('A');
    expect(flagSymbol('Normal')).toBe('');
    expect(flagSymbol(null)).toBe('');
    expect(delayClass('Late')).toBe('delay-late');
    expect(delayClass('Warning')).toBe('delay-warn');
    expect(delayClass('OnTime')).toBe('');
  });

  it('formats durations like the LDM delay column and ages from their parts', () => {
    const units = { days: 'd', hours: 'h', minutes: 'm' };
    expect(formatDuration(30, units)).toBe('');
    expect(formatDuration(5 * 60, units)).toBe('5m');
    expect(formatDuration(2 * 86400 + 12 * 3600 + 38 * 60, units)).toBe('2d 12h');
    expect(ageText(36, 5, 9, { y: 'y', m: 'm', d: 'd' })).toBe('36y');
    expect(ageText(2, 3, 0, { y: 'y', m: 'm', d: 'd' })).toBe('2y 3m');
    expect(ageText(0, 4, 12, { y: 'y', m: 'm', d: 'd' })).toBe('4m 12d');
    expect(ageText(null, null, null, { y: 'y', m: 'm', d: 'd' })).toBe('—');
  });

  it('turns a typed draft into a save item: numbers for numeric tests, text otherwise', () => {
    const numeric = resultRow({ decimals: 2 });
    expect(toValueItem(numeric, ' 95.25 ')).toEqual({ resultId: 'res-1', numericValue: 95.25, textValue: null, paragraphs: null, culture: null });
    expect(toValueItem(numeric, '> 400')).toEqual({ resultId: 'res-1', numericValue: null, textValue: '> 400', paragraphs: null, culture: null });
    const list = resultRow({ testType: 'RestrictedList' });
    expect(toValueItem(list, 'Positive').textValue).toBe('Positive');
    expect(initialDraft(resultRow({ numericValue: 95.25, decimals: 1 }))).toBe('95.3');
    expect(initialDraft(resultRow({ testType: 'NonNumeric', textValue: 'Trace' }))).toBe('Trace');
  });

  it('converts local calendar days to the UTC instant of their start', () => {
    expect(localDayStart('2026-10-10')).toBe(new Date(2026, 9, 10).toISOString());
    expect(localDayStart('2026-10-10', 1)).toBe(new Date(2026, 9, 11).toISOString());
    expect(localDayStart('')).toBeNull();
    expect(localDayStart('10/10/2026')).toBeNull();
  });

  it('picks names by language with a fallback and renders log values', () => {
    expect(pickName('Glucose', 'جلوكوز', 'ar')).toBe('جلوكوز');
    expect(pickName('Glucose', null, 'ar')).toBe('Glucose');
    expect(pickName(null, null, 'en')).toBe('—');
    expect(testLabel({ testCode: 'GLU', testNameEn: 'Glucose', testNameAr: 'جلوكوز', reportName: 'Fasting glucose' }, 'en')).toBe('Fasting glucose');
    expect(testLabel({ testCode: 'GLU', testNameEn: null, testNameAr: null }, 'en')).toBe('GLU');
    expect(logValueText(null, '95.0')).toBe('— → 95.0');
    expect(logValueText(null, null)).toBe('');
  });
});
