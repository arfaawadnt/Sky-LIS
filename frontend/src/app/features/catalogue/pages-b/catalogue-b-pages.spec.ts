import { microbiologyTabOf } from './microbiology.page';
import { toRequest } from './report-templates.page';

describe('Catalogue B page helpers', () => {
  it('finds the microbiology tab of a client or admin URL', () => {
    expect(microbiologyTabOf('/catalogue/microbiology/antibiotics')).toBe('antibiotics');
    expect(microbiologyTabOf('/admin/catalogue/microbiology/sensitivity-values?x=1')).toBe('sensitivity-values');
    expect(microbiologyTabOf('/catalogue/microbiology/culture-specimens')).toBe('culture-specimens');
    expect(microbiologyTabOf('/catalogue/microbiology/drug-types')).toBe('drug-types');   // Antibiotic classes keeps its path (LIS-CR-007)
    expect(microbiologyTabOf('/catalogue/microbiology')).toBe('organisms');   // the first tab since LIS-CR-007
  });

  it('sends the texts of a report template, empty ones as null, and never the logo or signature (their own calls, F-02)', () => {
    const request = toRequest({ name: { en: ' Results A4 ', ar: 'النتائج' }, type: 'Culture', language: 'En', showQr: true, headerText: { en: 'Header', ar: 'رأس' }, footerText: { en: ' ', ar: '' } });

    expect(request).toEqual({
      nameEn: 'Results A4',
      nameAr: 'النتائج',
      type: 'Culture',
      showQr: true,
      language: 'En',
      headerTextEn: 'Header',
      headerTextAr: 'رأس',
      footerTextEn: null,
      footerTextAr: null,
    });
  });
});
