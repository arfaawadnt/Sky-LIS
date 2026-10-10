import type { ReportTemplateListItem } from '../../../core/api/catalogue-b.contracts';
import { microbiologyTabOf } from './microbiology.page';
import { toRequest } from './report-templates.page';

describe('Catalogue B page helpers', () => {
  it('finds the microbiology tab of a client or admin URL', () => {
    expect(microbiologyTabOf('/catalogue/microbiology/antibiotics')).toBe('antibiotics');
    expect(microbiologyTabOf('/admin/catalogue/microbiology/sensitivity-values?x=1')).toBe('sensitivity-values');
    expect(microbiologyTabOf('/catalogue/microbiology')).toBe('drug-types');
  });

  it('keeps the logo and signature of a report template and sends empty texts as null', () => {
    const item: ReportTemplateListItem = {
      id: 't1',
      nameEn: 'Results A4',
      nameAr: 'النتائج',
      type: 'Results',
      logoFileId: 'file-logo',
      signatureFileId: null,
      showQr: false,
      language: 'Both',
      headerTextEn: null,
      headerTextAr: null,
      footerTextEn: null,
      footerTextAr: null,
      isActive: true,
    };

    const request = toRequest(
      { name: { en: ' Results A4 ', ar: 'النتائج' }, type: 'Culture', language: 'En', showQr: true, headerText: { en: 'Header', ar: 'رأس' }, footerText: { en: ' ', ar: '' } },
      item,
    );

    expect(request).toEqual({
      nameEn: 'Results A4',
      nameAr: 'النتائج',
      type: 'Culture',
      logoFileId: 'file-logo',
      signatureFileId: null,
      showQr: true,
      language: 'En',
      headerTextEn: 'Header',
      headerTextAr: 'رأس',
      footerTextEn: null,
      footerTextAr: null,
    });
  });
});
