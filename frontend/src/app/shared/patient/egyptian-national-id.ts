import type { PatientGender } from '../../core/api/patients.contracts';

/** What an Egyptian national id tells about its holder. */
export interface NationalIdFacts {
  /** ISO date `yyyy-mm-dd`. */
  readonly dateOfBirth: string;
  readonly gender: PatientGender;
  /** English name of the governorate of birth as the platform list seeds it; null when born abroad (88) or unknown. */
  readonly governorateNameEn: string | null;
}

/**
 * Governorate codes of the Egyptian national id (digits 8–9) → the English names of the platform's Egyptian governorate
 * list (seeded in the `Modules` migration). 88 = born abroad, no governorate.
 */
const GOVERNORATES: Readonly<Record<string, string>> = {
  '01': 'Cairo',
  '02': 'Alexandria',
  '03': 'Port Said',
  '04': 'Suez',
  '11': 'Damietta',
  '12': 'Dakahlia',
  '13': 'Sharqia',
  '14': 'Qalyubia',
  '15': 'Kafr El Sheikh',
  '16': 'Gharbia',
  '17': 'Monufia',
  '18': 'Beheira',
  '19': 'Ismailia',
  '21': 'Giza',
  '22': 'Beni Suef',
  '23': 'Faiyum',
  '24': 'Minya',
  '25': 'Asyut',
  '26': 'Sohag',
  '27': 'Qena',
  '28': 'Aswan',
  '29': 'Luxor',
  '31': 'Red Sea',
  '32': 'New Valley',
  '33': 'Matrouh',
  '34': 'North Sinai',
  '35': 'South Sinai',
};

/**
 * Reads an Egyptian national id: 14 digits `C YYMMDD GG SSSS K` — century (2 = 1900s, 3 = 2000s), date of birth,
 * governorate of birth, a sequence whose last digit is odd for a male and even for a female, and a check digit. Arabic-
 * Indic digits, spaces and dashes are accepted. Returns null for anything else (another country's id, a passport, a typo
 * that makes an impossible or future date): the result only suggests values, so the check digit, whose formula is not
 * published, is not used to refuse.
 */
export function readEgyptianNationalId(raw: string, todayIso: string): NationalIdFacts | null {
  const digits = raw.replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 0x0660)).replace(/[\s-]/g, '');
  if (!/^[23]\d{13}$/.test(digits)) {
    return null;
  }

  const century = digits[0] === '2' ? 1900 : 2000;
  const year = century + Number(digits.slice(1, 3));
  const month = Number(digits.slice(3, 5));
  const day = Number(digits.slice(5, 7));
  const date = new Date(Date.UTC(year, month - 1, day));
  if (date.getUTCFullYear() !== year || date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day) {
    return null;
  }

  const dateOfBirth = `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
  if (dateOfBirth > todayIso) {
    return null;
  }

  return {
    dateOfBirth,
    gender: Number(digits[12]) % 2 === 1 ? 'Male' : 'Female',
    governorateNameEn: GOVERNORATES[digits.slice(7, 9)] ?? null,
  };
}
