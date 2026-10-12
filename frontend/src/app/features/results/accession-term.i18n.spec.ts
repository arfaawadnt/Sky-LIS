import accountAr from '../../../../public/i18n/account.ar.json';
import resultsAr from '../../../../public/i18n/results.ar.json';
import platformAr from '../../../../public/i18n/platform.ar.json';
import shellAr from '../../../../public/i18n/shell.ar.json';

/**
 * Validation v0.5.0 R-04: the sidebar said "النتائج برقم الاستلام" and the page "النتائج برقم الطلب". The page carries
 * one name everywhere, with the Arabic term for "accession" the dictionaries use most (registration, samples, billing):
 * "رقم الزيارة".
 */
describe('Arabic term for the accession (R-04)', () => {
  const results = resultsAr as Record<string, string>;

  it('names Results by accession the same in the sidebar, the page title and the dashboard shortcut', () => {
    const title = results['case.title'];
    expect(title).toBe('النتائج برقم الزيارة');
    expect((shellAr as Record<string, string>)['nav.resultsByAccession']).toBe(title);
    expect((accountAr as Record<string, string>)['dashboard.action.resultsByAccession']).toBe(title);
  });

  it('uses no other term for the accession in the results, shell and account dictionaries', () => {
    for (const dictionary of [results, shellAr as Record<string, string>, accountAr as Record<string, string>, platformAr as Record<string, string>]) {
      const texts = Object.values(dictionary).join('\n');
      expect(texts).not.toContain('رقم الطلب');
      expect(texts).not.toContain('رقم الاستلام');
    }
  });
});
