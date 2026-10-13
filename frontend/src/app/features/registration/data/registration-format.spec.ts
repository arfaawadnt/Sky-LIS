import registrationAr from '../../../../../public/i18n/registration.ar.json';
import registrationEn from '../../../../../public/i18n/registration.en.json';
import { classifySearch, count, countKey, dateTime, discountAmount, discountPercent, labelDateTime, localDayEndUtc, localDayStartUtc, money, pick, printDateTime, printMoney, round2, utcOffsetMinutes } from './registration-format';

describe('registration-format', () => {
  it('writes counts and percentages in the digits of the language (UAT A-07)', () => {
    expect(count(3, 'en')).toBe('3');
    expect(count(3, 'ar')).toBe('٣');
    expect(count(10, 'ar')).toBe('١٠');
    expect(count(null, 'ar')).toBe('٠');
  });

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

  it('picks the singular, dual and plural keys of a counted sentence (validation 2026-10-11: "1 tube(s) were issued")', () => {
    expect([0, 1, 2, 7, 25].map((value) => countKey('registration.success.tubes', value, 'en'))).toEqual([
      'registration.success.tubesNone',
      'registration.success.tubesOne',
      'registration.success.tubes',
      'registration.success.tubes',
      'registration.success.tubes',
    ]);
    expect([0, 1, 2, 3, 10, 11, 103, 100].map((value) => countKey('t', value, 'ar'))).toEqual(['tNone', 'tOne', 'tTwo', 't', 't', 'tMany', 't', 'tMany']);

    const en = registrationEn as Record<string, string>;
    const ar = registrationAr as Record<string, string>;
    expect(en['success.tubesOne']).toBe('1 tube was issued.');
    expect(en['success.tubes']).toBe('{count} tubes were issued.');
    for (const key of ['success.tubesNone', 'success.tubesOne', 'success.tubes']) {
      expect(en[key]).not.toContain('(s)');
    }

    for (const key of ['success.tubesNone', 'success.tubesOne', 'success.tubesTwo', 'success.tubes', 'success.tubesMany']) {
      expect(ar[key]).toBeTruthy();
    }
  });

  it('writes on-screen dates like the other pages and labels in Latin digits (validation 2026-10-11)', () => {
    const utc = '2026-10-12T09:30:00Z';
    expect(dateTime(utc, 'en')).toMatch(/^12 Oct 2026, \d{2}:\d{2}$/);
    expect(dateTime(utc, 'ar')).toMatch(/[٠-٩]/);
    expect(labelDateTime(utc)).toMatch(/^12\/10\/2026, \d{2}:\d{2}$/);
    expect(dateTime(null)).toBe('—');
    expect(labelDateTime('x')).toBe('—');
  });

  it('F-10: printed documents write amounts and dates with Latin digits in Arabic, the screen with Arabic-Indic', () => {
    const utc = '2026-10-12T09:30:00Z';
    expect(printDateTime(utc, 'ar')).not.toMatch(/[٠-٩]/);
    expect(printDateTime(utc, 'ar')).toMatch(/2026/);
    expect(printDateTime(utc, 'en')).toMatch(/^12 Oct 2026, \d{2}:\d{2}$/);
    expect(printMoney(150, 'ar')).toMatch(/^150[.,٫]00$/);
    expect(printMoney(1234.5, 'en')).toBe('1,234.50');
    expect(money(150, 'ar')).toMatch(/[٠-٩]/);
    expect(printDateTime(null, 'ar')).toBe('—');
  });

  it('F-11: the repeat window of REG-201 is a counted phrase in both languages', () => {
    const en = registrationEn as Record<string, string>;
    const ar = registrationAr as Record<string, string>;
    expect(en['warning.REG-201']).toContain('{days}');
    expect(en['warning.REG-201']).not.toContain('{days} days');
    expect(ar['warning.REG-201']).not.toContain('يوماً');
    expect([en['dayCountOne'], en['dayCount']]).toEqual(['1 day', '{count} days']);
    expect([ar['dayCountOne'], ar['dayCountTwo'], ar['dayCount'], ar['dayCountMany']]).toEqual(['يوم واحد', 'يومان', '{count} أيام', '{count} يومًا']);
    expect(ar['services.setAddedTwo']).toContain('خدمتان');
  });

  it('names the repeated tests of a REG-109 refusal and a REG-206 warning in both languages (validation O-3)', () => {
    for (const dictionary of [registrationEn, registrationAr] as Record<string, string>[]) {
      expect(dictionary['error.REG-109']).toContain('{detail}');
      expect(dictionary['warning.REG-206']).toContain('{service}');
      expect(dictionary['warning.REG-206']).toContain('{detail}');
    }
  });
});
