import { classifySearch, discountAmount, discountPercent, localDayEndUtc, localDayStartUtc, money, pick, round2, utcOffsetMinutes } from './registration-format';

describe('registration-format', () => {
  it('formats money with two decimals and treats missing values as zero', () => {
    expect(money(1234.5)).toBe('1,234.50');
    expect(money(null)).toBe('0.00');
    expect(money(Number.NaN)).toBe('0.00');
  });

  it('rounds to cents', () => {
    expect(round2(10.005)).toBe(10.01);
    expect(round2(1.1 + 2.2)).toBe(3.3);
  });

  it('classifies the single search box of CP-10', () => {
    expect(classifySearch('')).toBeNull();
    expect(classifySearch(' acc-2026-00001 ')).toEqual({ target: 'accession', value: 'ACC-2026-00001' });
    expect(classifySearch('2026-15')).toEqual({ target: 'accession', value: '2026-15' });
    expect(classifySearch('123')).toEqual({ target: 'accession', value: '123' });
    expect(classifySearch('pat-2026-00007')).toEqual({ target: 'patientNo', value: 'PAT-2026-00007' });
    expect(classifySearch('0100 123 4567')).toEqual({ target: 'mobile', value: '01001234567' });
    expect(classifySearch('+20 100 123 4567')).toEqual({ target: 'mobile', value: '201001234567' });
    expect(classifySearch('Mona Adel')).toEqual({ target: 'name', value: 'Mona Adel' });
  });

  it('computes the discount as the server does (intake §5): percent of the base, amount capped at the base', () => {
    expect(discountAmount('Percent', 10, 300)).toBe(30);
    expect(discountAmount('Percent', 150, 300)).toBe(300);
    expect(discountAmount('Amount', 50, 300)).toBe(50);
    expect(discountAmount('Amount', 500, 300)).toBe(300);
    expect(discountAmount('Amount', 50, 0)).toBe(0);
    expect(discountAmount('Percent', 0, 300)).toBe(0);
    expect(discountPercent(30, 300)).toBe(10);
    expect(discountPercent(30, 0)).toBe(0);
  });

  it('turns a local day into a UTC range (start inclusive, end exclusive) and refuses other text', () => {
    const start = localDayStartUtc('2026-10-10');
    const end = localDayEndUtc('2026-10-10');
    expect(start).not.toBeNull();
    expect(end).not.toBeNull();
    expect(new Date(end!).getTime() - new Date(start!).getTime()).toBe(24 * 60 * 60 * 1000);
    expect(new Date(start!).getDate()).toBe(10);
    expect(localDayStartUtc('')).toBeNull();
    expect(localDayStartUtc('10/10/2026')).toBeNull();
    expect(localDayEndUtc(null)).toBeNull();
  });

  it('reports the browser offset east-positive', () => {
    const at = new Date(2026, 0, 15, 12, 0, 0);
    expect(utcOffsetMinutes(at)).toBe(-at.getTimezoneOffset());
  });

  it('picks the name of the page language with a fallback to the other', () => {
    expect(pick('Glucose', 'جلوكوز', 'en')).toBe('Glucose');
    expect(pick('Glucose', 'جلوكوز', 'ar')).toBe('جلوكوز');
    expect(pick('Glucose', '', 'ar')).toBe('Glucose');
    expect(pick(null, null, 'en')).toBe('');
  });
});
