import patientsAr from '../../../../public/i18n/patients.ar.json';
import { ageBetween, ageParts, ageText, birthDateFromAge, defaultGender, fileSize, isValidAge, isValidMobile, localDigits, mobileForStorage, normalizeMobile, parseIso, todayIso } from './patient-demographics';

describe('patient demographics (the server rules, mirrored)', () => {
  it('computes the age exactly like PatientAge.Between', () => {
    expect(ageBetween('1990-05-01', '2026-10-09')).toEqual({ years: 36, months: 5, days: 8 });
    expect(ageBetween('2026-10-09', '2026-10-09')).toEqual({ years: 0, months: 0, days: 0 });
    expect(ageBetween('2026-09-30', '2026-10-09')).toEqual({ years: 0, months: 0, days: 9 });
    expect(ageBetween('2024-02-29', '2026-02-28')).toEqual({ years: 2, months: 0, days: 0 });
    expect(ageBetween('2024-02-29', '2026-03-01')).toEqual({ years: 2, months: 0, days: 1 });
    expect(ageBetween('2026-01-31', '2026-03-01')).toEqual({ years: 0, months: 1, days: 1 });
    expect(ageBetween('2000-12-31', '2026-01-01')).toEqual({ years: 25, months: 0, days: 1 });
    expect(ageBetween('2027-01-01', '2026-10-09')).toEqual({ years: 0, months: 0, days: 0 });
  });

  it('derives the birth date from a stated age and gives the same age back', () => {
    expect(birthDateFromAge({ years: 34, months: 2, days: 5 }, '2026-10-09')).toBe('1992-08-04');
    expect(ageBetween('1992-08-04', '2026-10-09')).toEqual({ years: 34, months: 2, days: 5 });
    expect(birthDateFromAge({ years: 1, months: 1, days: 0 }, '2028-02-29')).toBe('2027-01-28');
    expect(birthDateFromAge({ years: 151, months: 0, days: 0 }, '2026-10-09')).toBeNull();
    expect(isValidAge({ years: 10, months: 12, days: 0 })).toBe(false);
  });

  it('parses only real calendar dates and formats today in UTC', () => {
    expect(parseIso('2026-02-29')).toBeNull();
    expect(parseIso('2024-02-29')).toEqual({ year: 2024, month: 2, day: 29 });
    expect(parseIso('09/10/2026')).toBeNull();
    expect(todayIso(new Date(Date.UTC(2026, 9, 9, 23, 30)))).toBe('2026-10-09');
  });

  it('proposes a gender only for gendered titles', () => {
    expect(defaultGender('Mr')).toBe('Male');
    expect(defaultGender('Master')).toBe('Male');
    expect(defaultGender('Mrs')).toBe('Female');
    expect(defaultGender('Miss')).toBe('Female');
    expect(defaultGender('Ms')).toBe('Female');
    expect(defaultGender('Dr')).toBeNull();
    expect(defaultGender('Baby')).toBeNull();
    expect(defaultGender('Prof')).toBeNull();
  });

  it('normalises and checks mobiles, with a country length when one is chosen', () => {
    expect(normalizeMobile(' 0100 123-4567 ')).toBe('01001234567');
    expect(normalizeMobile('٠١٠٠١٢٣٤٥٦٧')).toBe('01001234567');
    expect(normalizeMobile('(010) 0123.4567')).toBe('01001234567');
    expect(isValidMobile('0100 123 4567')).toBe(true);
    expect(isValidMobile('+20 100 123 4567')).toBe(true);
    expect(isValidMobile('1234567')).toBe(false);
    expect(isValidMobile('12ab5678')).toBe(false);
    expect(isValidMobile('+201001234567', 10)).toBe(true);
  });

  it('accepts an Egyptian mobile with or without the trunk 0 and stores both the same way (remaining point A-09)', () => {
    // Egypt publishes 10 national digits: 1011140001 and 01011140001 are the same number
    expect(isValidMobile('1011140001', 10)).toBe(true);
    expect(isValidMobile('01011140001', 10)).toBe(true);
    expect(isValidMobile('010 1114 0001', 10)).toBe(true);
    expect(isValidMobile('0101114000', 10)).toBe(false);   // 10 digits starting with 0: a digit is missing
    expect(isValidMobile('101114000', 10)).toBe(false);
    expect(isValidMobile('001011140001', 10)).toBe(false);
    expect(isValidMobile('12-34', 10)).toBe(false);
    expect(mobileForStorage('1011140001', 10)).toBe('01011140001');
    expect(mobileForStorage('01011140001', 10)).toBe('01011140001');
    expect(mobileForStorage('٠١٠١١١٤٠٠٠١', 10)).toBe('01011140001');
    expect(mobileForStorage('+20 101 114 0001', 10)).toBe('+201011140001');
    expect(mobileForStorage('1011140001')).toBe('1011140001');   // any country: as typed
  });

  it('shows at most two age units and readable file sizes', () => {
    expect(ageParts({ years: 36, months: 5, days: 8 })).toEqual([
      { unit: 'years', value: 36 },
      { unit: 'months', value: 5 },
    ]);
    expect(ageParts({ years: 0, months: 0, days: 0 })).toEqual([{ unit: 'days', value: 0 }]);
    expect(ageParts({ years: 0, months: 3, days: 2 })).toEqual([
      { unit: 'months', value: 3 },
      { unit: 'days', value: 2 },
    ]);
    expect(fileSize(512)).toBe('512 B');
    expect(fileSize(2048)).toBe('2.0 KB');
    expect(fileSize(3 * 1024 * 1024)).toBe('3.0 MB');
  });

  it('writes the age numbers in the digits of the language (validation R-02)', () => {
    const units: Readonly<Record<string, string>> = { 'patients.age.years': '{value} سنة', 'patients.age.months': '{value} شهر', 'patients.age.days': '{value} يوم' };
    const arabic = (key: string, params: Readonly<Record<string, string | number>>): string => (units[key] ?? key).replace('{value}', String(params['value']));
    const english = (key: string, params: Readonly<Record<string, string | number>>): string => `${String(params['value'])} ${key.slice(key.lastIndexOf('.') + 1)}`;

    expect(ageText({ years: 52, months: 0, days: 0 }, 'ar', arabic)).toBe('٥٢ سنة');
    expect(ageText({ years: 36, months: 5, days: 8 }, 'ar', arabic)).toBe('٣٦ سنة ٥ شهر');
    expect(ageText({ years: 0, months: 0, days: 10 }, 'ar', arabic)).toBe('١٠ يوم');
    expect(ageText({ years: 52, months: 0, days: 0 }, 'en', english)).toBe('52 years');
    expect(localDigits(1974, 'ar')).toBe('١٩٧٤');   // never grouped
    expect(localDigits(1974, 'en')).toBe('1974');
  });

  it('F-11: the Arabic age units take their counted form (one, two, 3–10 plural, 11+ singular)', () => {
    const dictionary = patientsAr as Record<string, string>;
    const arabic = (key: string, params: Readonly<Record<string, string | number>>): string => (dictionary[key.slice('patients.'.length)] ?? key).replace('{value}', String(params['value']));

    expect(ageText({ years: 52, months: 0, days: 0 }, 'ar', arabic)).toBe('٥٢ سنة');
    expect(ageText({ years: 5, months: 0, days: 0 }, 'ar', arabic)).toBe('٥ سنوات');
    expect(ageText({ years: 2, months: 1, days: 0 }, 'ar', arabic)).toBe('سنتان شهر واحد');
    expect(ageText({ years: 0, months: 11, days: 3 }, 'ar', arabic)).toBe('١١ شهرًا ٣ أيام');
    expect(ageText({ years: 0, months: 0, days: 0 }, 'ar', arabic)).toBe('٠ يوم');
  });
});
