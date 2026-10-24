import { composeMobile, mobileDialCountryOf, mobileValueOf, splitMobile, type MobileDialCountry } from './mobile-number';

const EGYPT: MobileDialCountry = { dialCode: '+20', mobileLength: 10, isoCode: 'EG', nameEn: 'Egypt', nameAr: 'مصر' };
const SAUDI: MobileDialCountry = { dialCode: '+966', mobileLength: 9, isoCode: 'SA', nameEn: 'Saudi Arabia', nameAr: 'السعودية' };
const LIST = [EGYPT, SAUDI];

describe('Mobile number rules (LIS-CR-007 C-05)', () => {
  it('drops the trunk 0 and composes the E.164 value', () => {
    expect(mobileValueOf('01004439855', LIST, null, '+20')).toBe('+201004439855');
    expect(mobileValueOf('1004439855', LIST, null, '+20')).toBe('+201004439855');
    expect(mobileValueOf('0100 443-9855', [], EGYPT, '+20')).toBe('+201004439855');
    expect(mobileValueOf('٠١٠٠٤٤٣٩٨٥٥', [], EGYPT, '+20')).toBe('+201004439855');
  });

  it('recognises the pasted international forms', () => {
    expect(splitMobile('+20 100 443 9855', LIST, null, '+966')).toEqual({ dial: '+20', national: '1004439855' });
    expect(splitMobile('0020 100 443 9855', LIST, null, '+966')).toEqual({ dial: '+20', national: '1004439855' });
    expect(splitMobile('+20 0100 443 9855', LIST, null, '+20')).toEqual({ dial: '+20', national: '1004439855' });
    expect(splitMobile('+966501234567', LIST, null, '+20')).toEqual({ dial: '+966', national: '501234567' });
    expect(splitMobile('0100 443 9855', LIST, null, '+20')).toEqual({ dial: '+20', national: '1004439855' });
  });

  it('keeps an international number of another country visible when the country is fixed', () => {
    expect(splitMobile('+966501234567', LIST, EGYPT, '+20')).toEqual({ dial: '+20', national: '+966501234567' });
    expect(mobileValueOf('+966501234567', LIST, EGYPT, '+20')).toBe('+966501234567');
    expect(splitMobile('+44 7911 123456', LIST, null, '+20')).toEqual({ dial: '+20', national: '+447911123456' });
  });

  it('answers empty for nothing, passes letters on and keeps the digits without a dial code', () => {
    expect(composeMobile({ dial: '+20', national: '' })).toBe('');
    expect(composeMobile({ dial: '+20', national: '0' })).toBe('');
    expect(composeMobile({ dial: '+20', national: '12ab' })).toBe('12ab');
    expect(composeMobile({ dial: '', national: '0100 123 4567' })).toBe('01001234567');
    expect(splitMobile('01001234567', [], null, '')).toEqual({ dial: '', national: '01001234567' });
  });

  it('reads a country of the public list and refuses a row without a dial code', () => {
    expect(mobileDialCountryOf({ id: 'eg', isoCode: 'EG', nameEn: 'Egypt', nameAr: 'مصر', dialCode: '+20', mobileLength: 10 })).toEqual(EGYPT);
    expect(mobileDialCountryOf({ code: 'KW', nameEn: 'Kuwait', dialCode: '965' })).toEqual({ dialCode: '+965', mobileLength: null, isoCode: 'KW', nameEn: 'Kuwait', nameAr: null });
    expect(mobileDialCountryOf({ isoCode: 'XX', dialCode: '' })).toBeNull();
    expect(mobileDialCountryOf({ isoCode: 'EG', dialCode: '+20', isActive: false })).toBeNull();
    expect(mobileDialCountryOf(null)).toBeNull();
  });
});
