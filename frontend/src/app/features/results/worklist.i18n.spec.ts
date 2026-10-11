import resultsAr from '../../../../public/i18n/results.ar.json';
import resultsEn from '../../../../public/i18n/results.en.json';

/** UAT R1-06: the worklist search finds a patient by name too (A-14), and its placeholder says so in both languages. */
describe('Worklist search placeholder', () => {
  it('mentions the patient name', () => {
    expect((resultsEn as Record<string, string>)['worklist.searchPlaceholder']).toContain('name');
    expect((resultsAr as Record<string, string>)['worklist.searchPlaceholder']).toContain('اسمه');
  });
});
