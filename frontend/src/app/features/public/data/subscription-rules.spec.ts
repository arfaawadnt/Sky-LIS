import { FormControl } from '@angular/forms';

import { allPassed, countryMobileValidator, isUsernameShapeValid, normaliseMobile, passwordChecks } from './subscription-rules';

const EGYPT = { dialCode: '+20', mobileLength: 10 };

describe('Subscription rules (CP-02, mirrors of the server)', () => {
  it('normalises a mobile by the country length like Country.NormaliseMobile', () => {
    expect(normaliseMobile('01012345678', EGYPT)).toBe('+201012345678');
    expect(normaliseMobile('1012345678', EGYPT)).toBe('+201012345678');
    expect(normaliseMobile('+20 101 234 5678', EGYPT)).toBe('+201012345678');
    expect(normaliseMobile('00201012345678', EGYPT)).toBe('+201012345678');
    expect(normaliseMobile('0101234567', EGYPT)).toBeNull();
    expect(normaliseMobile('+966501234567', EGYPT)).toBeNull();
    expect(normaliseMobile('010-1234-567x', EGYPT)).toBeNull();
    expect(normaliseMobile('0012345678', EGYPT)).toBeNull();
  });

  it('validates a mobile only once a country is chosen', () => {
    let country: typeof EGYPT | null = null;
    const control = new FormControl('123', { nonNullable: true, validators: [countryMobileValidator(() => country)] });
    expect(control.valid).toBe(true);

    country = EGYPT;
    control.updateValueAndValidity();
    expect(control.errors).toEqual({ mobileLength: { length: 10 } });

    control.setValue('01012345678');
    expect(control.valid).toBe(true);
  });

  it('checks the username shape (4 to 64 of a-z 0-9 . _ - @)', () => {
    expect(isUsernameShapeValid('Lab.Admin')).toBe(true);
    expect(isUsernameShapeValid('abc')).toBe(false);
    expect(isUsernameShapeValid('lab admin')).toBe(false);
    expect(isUsernameShapeValid('مدير')).toBe(false);
  });

  it('applies the length-only password rule and never the username', () => {
    expect(passwordChecks('short', 'short', 'lab.admin', 8, 128)).toEqual({ length: false, notUsername: true, matches: true });
    expect(passwordChecks('my-lab.admin-2026', 'my-lab.admin-2026', 'lab.admin', 8, 128).notUsername).toBe(false);
    expect(passwordChecks('a quiet morning walk', 'a quiet morning walk', 'lab.admin', 8, 128)).toEqual({ length: true, notUsername: true, matches: true });
    expect(allPassed(passwordChecks('a quiet morning walk', 'a quiet morning', 'lab.admin', 8, 128))).toBe(false);
  });
});
