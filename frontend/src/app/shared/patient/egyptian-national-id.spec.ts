import { readEgyptianNationalId } from './egyptian-national-id';

const today = '2026-10-12';

describe('readEgyptianNationalId', () => {
  it('reads the date of birth, the gender and the governorate of birth', () => {
    expect(readEgyptianNationalId('29601010100022', today)).toEqual({ dateOfBirth: '1996-01-01', gender: 'Female', governorateNameEn: 'Cairo' });
    expect(readEgyptianNationalId('28512252101235', today)).toEqual({ dateOfBirth: '1985-12-25', gender: 'Male', governorateNameEn: 'Giza' });
    expect(readEgyptianNationalId('30803158800018', today)).toEqual({ dateOfBirth: '2008-03-15', gender: 'Male', governorateNameEn: null });   // 88: born abroad
  });

  it('accepts Arabic-Indic digits, spaces and dashes', () => {
    expect(readEgyptianNationalId('٢٩٦٠١٠١٠١٠٠٠٢٢', today)?.dateOfBirth).toBe('1996-01-01');
    expect(readEgyptianNationalId('2 960101 01 0002 2', today)?.gender).toBe('Female');
    expect(readEgyptianNationalId('2-960101-01-0002-2', today)?.governorateNameEn).toBe('Cairo');
  });

  it('ignores anything that is not an Egyptian id', () => {
    expect(readEgyptianNationalId('', today)).toBeNull();
    expect(readEgyptianNationalId('2960101010002', today)).toBeNull();      // 13 digits
    expect(readEgyptianNationalId('19601010100022', today)).toBeNull();     // century 1
    expect(readEgyptianNationalId('A1234567', today)).toBeNull();           // passport
    expect(readEgyptianNationalId('29602300100022', today)).toBeNull();     // 30 February
    expect(readEgyptianNationalId('32701010100022', today)).toBeNull();     // 2027: in the future
  });
});
