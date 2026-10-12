import { FormControl, FormGroup } from '@angular/forms';

import { toCsv } from '../data/csv-export';
import { statusTone } from './status';
import {
  countryMobileValidator,
  fieldProblem,
  formatJson,
  hasAtMostTwoDecimals,
  ianaTimeZones,
  isDialCodeValid,
  jsonValidator,
  matchingPasswords,
  maxTwoDecimals,
  normaliseCountryMobile,
  notContaining,
  SLUG_PATTERN,
} from './validators';

describe('Platform helpers', () => {
  it('writes RFC 4180 CSV with a BOM, quoting and formula neutralisation', () => {
    const csv = toCsv(
      [
        { header: 'Name', value: (row: { name: string; amount: number; note: string | null }) => row.name },
        { header: 'Amount', value: (row) => row.amount },
        { header: 'Note', value: (row) => row.note },
      ],
      [
        { name: 'Al Noor, Lab', amount: -12.5, note: 'said "hi"' },
        { name: '=HYPERLINK("x")', amount: 3, note: null },
      ],
    );

    expect(csv.startsWith('﻿Name,Amount,Note\r\n')).toBe(true);
    expect(csv).toContain('"Al Noor, Lab",-12.5,"said ""hi"""\r\n');
    expect(csv).toContain(`"'=HYPERLINK(""x"")",3,\r\n`);
  });

  it('checks JSON documents and formats them', () => {
    const validator = jsonValidator((detail) => `bad ${detail}`.trim());
    expect(validator(new FormControl('{"a": 1}'))).toBeNull();
    expect(validator(new FormControl('[1, 2]'))).toBeNull();
    expect(validator(new FormControl(''))).toBeNull();
    expect(validator(new FormControl('42'))).toEqual({ server: 'bad' });
    expect(Object.keys(validator(new FormControl('{ a: 1 }')) ?? {})).toEqual(['server']);
    expect(formatJson('{"a":[1,2]}')).toBe('{\n  "a": [\n    1,\n    2\n  ]\n}');
    expect(formatJson('not json')).toBe('not json');
  });

  it('flags a confirmation that differs and a password containing the username', () => {
    const form = new FormGroup(
      {
        username: new FormControl('noor.admin', { nonNullable: true }),
        password: new FormControl('', { nonNullable: true, validators: [notContaining('username', () => 'contains')] }),
        confirm: new FormControl('', { nonNullable: true }),
      },
      { validators: matchingPasswords('password', 'confirm', () => 'mismatch') },
    );

    form.setValue({ username: 'noor.admin', password: 'my-NOOR.ADMIN-pass', confirm: 'other' });
    expect(form.controls.password.errors).toEqual({ server: 'contains' });
    expect(form.controls.confirm.errors).toEqual({ server: 'mismatch' });

    form.setValue({ username: 'noor.admin', password: 'correct horse battery', confirm: 'correct horse battery' });
    expect(form.valid).toBe(true);
  });

  it('accepts portal slugs of the server rule', () => {
    expect(['al-noor', 'lab1', 'a1b'].every((slug) => SLUG_PATTERN.test(slug))).toBe(true);
    expect(['ab', '-lab', 'lab-', 'Al-Noor', 'al noor'].some((slug) => SLUG_PATTERN.test(slug))).toBe(false);
  });

  it('C-03: normalises a mobile by the country length and refuses landlines and short numbers', () => {
    const egypt = { dialCode: '+20', mobileLength: 10 };
    expect(normaliseCountryMobile('01012345678', egypt)).toBe('+201012345678');
    expect(normaliseCountryMobile('+20 101 234 5678', egypt)).toBe('+201012345678');
    expect(normaliseCountryMobile('0020-101-234-5678', egypt)).toBe('+201012345678');
    expect(normaliseCountryMobile('0233351111', egypt)).toBeNull();
    expect(normaliseCountryMobile('+2010111100', egypt)).toBeNull();
    expect(normaliseCountryMobile('+966512345678', egypt)).toBeNull();
    expect(normaliseCountryMobile('51234567', { dialCode: '+965', mobileLength: 8 })).toBe('+96551234567');
  });

  it('C-03: the country mobile validator reads the sibling country and names the rule', () => {
    const form = new FormGroup({
      countryId: new FormControl('eg', { nonNullable: true }),
      mobile: new FormControl('', { nonNullable: true, validators: [countryMobileValidator('countryId', (id) => (id === 'eg' ? { dialCode: '+20', mobileLength: 10 } : null), (country) => `${country.mobileLength} digits`)] }),
    });
    form.controls.mobile.setValue('0233351111');
    expect(form.controls.mobile.errors).toEqual({ server: '10 digits' });
    form.controls.mobile.setValue('01012345678');
    expect(form.controls.mobile.errors).toBeNull();
    form.controls.countryId.setValue('unknown');
    form.controls.mobile.setValue('123');
    expect(form.controls.mobile.errors).toBeNull();   // unknown country: the server decides
  });

  it('C-19: a money amount has at most two decimals and is never rounded', () => {
    expect([250.55, 250.5, 250, 0.01, 100000000].map(hasAtMostTwoDecimals)).toEqual([true, true, true, true, true]);
    expect([250.555, 0.001, 10.0001].map(hasAtMostTwoDecimals)).toEqual([false, false, false]);
    const amount = new FormControl<number | null>(250.555, { validators: [maxTwoDecimals(() => 'two decimals')] });
    expect(amount.errors).toEqual({ server: 'two decimals' });
    amount.setValue(250.56);
    expect(amount.errors).toBeNull();
  });

  it('C-09: a dial code is + and one to four digits, not starting with 0 (GEO-002)', () => {
    expect(['+20', '+1', '+998', '+1 684', ' +966 '].map(isDialCodeValid)).toEqual([true, true, true, true, true]);
    expect(['998', '20', '+', '+0', '+12345', '+2a', ''].map(isDialCodeValid)).toEqual([false, false, false, false, false, false, false]);
    const problem = fieldProblem('dialCode', 'The dial code is +.');
    expect([problem.code, problem.problem.errors]).toEqual(['VALIDATION-400', { dialCode: ['The dial code is +.'] }]);
  });

  it('C-08: offers the IANA time zones, keeping UTC and the zones already stored', () => {
    const zones = ianaTimeZones(['Asia/Calcutta']);
    expect(zones).toContain('Africa/Cairo');
    expect(zones).toContain('UTC');
    expect(zones).toContain('Asia/Calcutta');
    expect(zones).not.toContain('Mars/Base');
    expect([...zones].sort((a, b) => a.localeCompare(b, 'en'))).toEqual(zones);
  });

  it('colours the platform statuses', () => {
    expect([statusTone('Trial'), statusTone('Active'), statusTone('Inactive'), statusTone('Declined'), statusTone('Unknown')]).toEqual(['info', 'ok', 'neutral', 'bad', 'neutral']);
  });
});
