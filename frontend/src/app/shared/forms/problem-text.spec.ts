import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, FormGroup } from '@angular/forms';

import sharedAr from '../../../../public/i18n/shared.ar.json';
import sharedEn from '../../../../public/i18n/shared.en.json';
import type { ProblemDetails } from '../../core/api/problem-details';
import { I18nService } from '../../core/i18n/i18n.service';
import { fieldErrorText, problemMessage } from './problem-text';
import { SERVER_RULES_KEY, clearServerErrors, problemToFormErrors } from './problem-to-form-errors';

const DICTIONARIES: Readonly<Record<string, unknown>> = {
  '/i18n/shared.en.json': sharedEn,
  '/i18n/shared.ar.json': sharedAr,
  '/i18n/demo.en.json': { 'field.currencyCode': 'Currency', 'field.headerText': 'Header text' },
  '/i18n/demo.ar.json': { 'field.currencyCode': 'العملة', 'field.headerText': 'نص الترويسة' },
};

/** Serves the dictionaries the I18nService fetches; the real service, so the lookups and fallbacks are the real ones. */
async function i18nWith(language: 'en' | 'ar', ...features: string[]): Promise<I18nService> {
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (input: string | URL | Request) =>
    new Response(JSON.stringify(DICTIONARIES[String(input)] ?? {}), { status: 200, headers: { 'Content-Type': 'application/json' } }),
  );
  const i18n = TestBed.inject(I18nService);
  i18n.setLanguage(language);
  await Promise.all(features.map((feature) => i18n.load(feature)));
  return i18n;
}

function validation(errors: Record<string, string[]>, rules: NonNullable<ProblemDetails['rules']>): ProblemDetails {
  return { type: 'about:blank', title: 'The request is not valid.', status: 400, code: 'VALIDATION-400', traceId: null, correlationId: null, errors, rules };
}

describe('localized server validation (C-06, B-03)', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
  });

  afterEach(() => vi.restoreAllMocks());

  it('translates the known rules with their bounds instead of the validator English, in both languages', async () => {
    let i18n = await i18nWith('en', 'shared');
    expect(fieldErrorText(i18n, [{ rule: 'NotEmpty' }], ["'Header Text Ar' must not be empty."])).toBe('This field is required.');
    expect(fieldErrorText(i18n, [{ rule: 'ExactLength', minLength: 3, maxLength: 3 }], ["'Currency Code' must be 3 characters in length. You entered 2 characters."])).toBe('Exactly 3 characters.');
    expect(fieldErrorText(i18n, [{ rule: 'MaximumLength', minLength: 0, maxLength: 200 }], ['x'])).toBe('At most 200 characters.');
    expect(fieldErrorText(i18n, [{ rule: 'Length', minLength: 2, maxLength: 10 }], ['x'])).toBe('Between 2 and 10 characters.');
    expect(fieldErrorText(i18n, [{ rule: 'GreaterThanOrEqual', comparisonValue: 0 }], ['x'])).toBe('The value must be at least 0.');
    expect(fieldErrorText(i18n, [{ rule: 'InclusiveBetween', from: 1, to: 9 }], ['x'])).toBe('The value must be between 1 and 9.');
    expect(fieldErrorText(i18n, [{ rule: 'Email' }], ['x'])).toBe('Enter a valid e-mail address.');
    expect(fieldErrorText(i18n, [{ rule: 'Format' }], ['The JSON value could not be converted.'])).toBe('The value has the wrong format.');

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    i18n = await i18nWith('ar', 'shared');
    expect(fieldErrorText(i18n, [{ rule: 'NotEmpty' }], ["'Header Text Ar' must not be empty."])).toBe('هذا الحقل مطلوب.');
    expect(fieldErrorText(i18n, [{ rule: 'ExactLength', minLength: 3, maxLength: 3 }], ['x'])).toBe('3 أحرف بالضبط.');
  });

  it('keeps a hand-written English reason of an unknown rule, never a validator default, and never English in Arabic', async () => {
    const en = await i18nWith('en', 'shared');
    expect(fieldErrorText(en, [{ rule: 'Predicate' }], ["The language must be 'en' or 'ar'."])).toBe("The language must be 'en' or 'ar'.");
    expect(fieldErrorText(en, [{ rule: 'Predicate' }], ["The specified condition was not met for 'Language'."])).toBe('The value is not valid.');

    en.setLanguage('ar');
    await en.load('shared');
    expect(fieldErrorText(en, [{ rule: 'Predicate' }], ["The language must be 'en' or 'ar'."])).toBe('القيمة غير صالحة.');
  });

  it('shows the messages as sent when the answer carries no rules (older server)', async () => {
    const i18n = await i18nWith('en', 'shared');
    expect(fieldErrorText(i18n, undefined, ['The code exists.'])).toBe('The code exists.');
  });

  it('names each field in the general message of a VALIDATION-400 (module label, localized halves)', async () => {
    const i18n = await i18nWith('en', 'shared', 'demo');
    const problem = validation(
      { currencyCode: ["'Currency Code' must be 3 characters in length."], headerTextAr: ["'Header Text Ar' must not be empty."], 'lines[0].qty': ['x'] },
      { currencyCode: [{ rule: 'ExactLength', minLength: 3, maxLength: 3 }], headerTextAr: [{ rule: 'NotEmpty' }], 'lines[0].qty': [{ rule: 'GreaterThan', comparisonValue: 0 }] },
    );

    expect(problemMessage(i18n, 'demo', problem)).toBe(
      'Please check the entered values. Currency: Exactly 3 characters. Header text (Arabic): This field is required. The value must be greater than 0.',
    );
    expect(problemMessage(i18n, 'demo', { code: 'CONCURRENCY-409' })).toBe('The record was changed by someone else. Reload and try again.');
  });

  it('carries the rules onto the matched control and clears them with the message', () => {
    const form = new FormGroup({ currencyCode: new FormControl('EG', { nonNullable: true }) });
    problemToFormErrors(validation({ CurrencyCode: ['raw'] }, { CurrencyCode: [{ rule: 'ExactLength', minLength: 3, maxLength: 3 }] }), form);

    expect(form.controls.currencyCode.errors).toEqual({ server: 'raw', [SERVER_RULES_KEY]: [{ rule: 'ExactLength', minLength: 3, maxLength: 3 }] });
    clearServerErrors(form);
    expect(form.controls.currencyCode.errors).toBeNull();
  });
});
