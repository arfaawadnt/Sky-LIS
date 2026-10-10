import { FormControl, FormGroup } from '@angular/forms';

import type { ProblemDetails } from '../../core/api/problem-details';
import { localizedTextGroup } from './localized-text';
import { clearServerErrors, problemToFormErrors, toControlPath } from './problem-to-form-errors';

function problem(errors: Record<string, string[]>): ProblemDetails {
  return { type: 'about:blank', title: 'The request is not valid.', status: 400, code: 'VALIDATION-400', traceId: null, correlationId: null, errors };
}

describe('problemToFormErrors (shared/forms)', () => {
  it('normalises server property paths to control paths', () => {
    expect(toControlPath('NameEn')).toBe('nameEn');
    expect(toControlPath('Lines[0].Qty')).toBe('lines.0.qty');
    expect(toControlPath('address.City')).toBe('address.city');
  });

  it('puts each message on its control (flat DTO names reach the localized halves) and returns the rest', () => {
    const form = new FormGroup({
      code: new FormControl('', { nonNullable: true }),
      name: localizedTextGroup({ en: 'x', ar: 'y' }),
    });

    const unmatched = problemToFormErrors(problem({ Code: ['The code exists.'], NameAr: ['Too long.'], 'Lines[0].Qty': ['Must be positive.'] }), form);

    expect(form.controls.code.errors).toEqual({ server: 'The code exists.' });
    expect(form.controls.code.touched).toBe(true);
    expect(form.controls.name.controls.ar.errors).toEqual({ server: 'Too long.' });
    expect(form.controls.name.controls.en.errors).toBeNull();
    expect(unmatched).toEqual({ 'Lines[0].Qty': ['Must be positive.'] });

    clearServerErrors(form);
    expect(form.controls.code.errors).toBeNull();
    expect(form.controls.name.controls.ar.errors).toBeNull();
  });

  it('does nothing for a problem without field errors', () => {
    const form = new FormGroup({ code: new FormControl('', { nonNullable: true }) });
    expect(problemToFormErrors({ ...problem({}), errors: null }, form)).toEqual({});
    expect(problemToFormErrors(null, form)).toEqual({});
    expect(form.controls.code.errors).toBeNull();
  });
});
