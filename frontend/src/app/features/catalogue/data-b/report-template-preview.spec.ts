import type { ReportTemplateListItem } from '../../../core/api/catalogue-b.contracts';
import { previewLanguageOf, sampleReportDocument } from './report-template-preview';

const template: ReportTemplateListItem = {
  id: 't1',
  nameEn: 'Results A4',
  nameAr: 'النتائج',
  type: 'Results',
  logoFileId: 'file-logo',
  signatureFileId: 'file-sig',
  showQr: false,
  language: 'Both',
  headerTextEn: 'Header',
  headerTextAr: 'رأس',
  footerTextEn: 'Footer',
  footerTextAr: 'تذييل',
  isActive: true,
};

describe('Report template preview (validation F-02)', () => {
  it('prints in the template language, or in the screen language for a bilingual template', () => {
    expect(previewLanguageOf({ language: 'En' }, 'ar')).toBe('en');
    expect(previewLanguageOf({ language: 'Ar' }, 'en')).toBe('ar');
    expect(previewLanguageOf({ language: 'Both' }, 'ar')).toBe('ar');
    expect(previewLanguageOf({ language: 'Both' }, 'en')).toBe('en');
  });

  it('lays a verified sample report out with the template: texts, QR choice and the image references', () => {
    const doc = sampleReportDocument(template, 'ar', { labNameEn: 'Sample Laboratory', labNameAr: 'مختبر نموذجي' }, '2026-10-12T08:00:00.000Z');

    expect(doc.language).toBe('ar');
    expect(doc.template).toEqual({
      templateId: 't1',
      nameEn: 'Results A4',
      language: 'Both',
      showQr: false,
      logoFileId: 'file-logo',
      signatureFileId: 'file-sig',
      headerTextEn: 'Header',
      headerTextAr: 'رأس',
      footerTextEn: 'Footer',
      footerTextAr: 'تذييل',
    });
    expect(doc.letterhead.labNameAr).toBe('مختبر نموذجي');
    expect(doc.isPreview).toBe(true);   // the renderer marks it "preview — not a report"
    expect(doc.isPreliminary).toBe(false);
    expect(doc.signature.verifiedByName).not.toBeNull();   // so the signature image shows
    expect(doc.sections[0]?.lines.length).toBeGreaterThan(0);
  });
});
