import ar from '../../../../../public/i18n/catalogue-a.ar.json';
import en from '../../../../../public/i18n/catalogue-a.en.json';

/** Validation v0.5.0 (MDRD eGFR): the formula editor's help names the patient variables in both languages. */
describe('Formula help text', () => {
  it('names @AGE and @FEMALE in English and Arabic', () => {
    for (const dictionary of [en, ar] as Record<string, string>[]) {
      const help = dictionary['tests.formula.syntax'] ?? '';
      expect([help.includes('@AGE'), help.includes('@FEMALE')]).toEqual([true, true]);
    }
  });
});
