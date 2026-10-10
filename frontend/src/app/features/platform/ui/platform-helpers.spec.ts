import { FormControl, FormGroup } from '@angular/forms';

import { toCsv } from '../data/csv-export';
import { statusTone } from './status';
import { formatJson, jsonValidator, matchingPasswords, notContaining, SLUG_PATTERN } from './validators';

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

  it('colours the platform statuses', () => {
    expect([statusTone('Trial'), statusTone('Active'), statusTone('Inactive'), statusTone('Declined'), statusTone('Unknown')]).toEqual(['info', 'ok', 'neutral', 'bad', 'neutral']);
  });
});
