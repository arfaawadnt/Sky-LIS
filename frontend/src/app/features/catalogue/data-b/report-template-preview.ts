import type { ReportTemplateListItem, ReportTemplateRequest } from '../../../core/api/catalogue-b.contracts';
import type { ReportDesign } from '../../../core/api/report-designs';
import type { LaboratoryReportDocument, ReportLine } from '../../../core/api/results.contracts';

/** The language a template preview prints in: the template's own, or the screen's for a bilingual template. */
export type PreviewLanguage = 'en' | 'ar';

export function previewLanguageOf(template: Pick<ReportTemplateListItem, 'language'>, screen: string): PreviewLanguage {
  if (template.language === 'En') {
    return 'en';
  }

  if (template.language === 'Ar') {
    return 'ar';
  }

  return screen === 'ar' ? 'ar' : 'en';
}

/** The laboratory named on the sample (the platform catalogue has no laboratory; a client's real name prints on real reports). */
export interface PreviewLetterhead {
  readonly labNameEn: string;
  readonly labNameAr: string;
}

// Sample data of the preview: a fictitious patient and two results, in both languages (data of the sample document,
// like the dictionaries' words; never sent to the server). The document says "Preview" across the top.
const SAMPLE_LINES: readonly ReportLine[] = [
  sampleLine('preview-hb', 'Haemoglobin', 'الهيموجلوبين', '13.8', 'g/dL', '12.0 – 16.0', 'Normal'),
  sampleLine('preview-glu', 'Glucose (fasting)', 'الجلوكوز (صائم)', '132', 'mg/dL', '70 – 100', 'High'),
  sampleLine('preview-chol', 'Cholesterol, total', 'الكوليسترول الكلي', '186', 'mg/dL', '< 200', 'Normal'),
];

/**
 * A sample laboratory report laid out with a report template (validation F-02): the template's header and footer
 * texts, QR choice and language, a verified result table so the signature shows, and the template id and file ids so
 * the renderer's caller can load the logo and the signature. Rendered by the results report renderer itself.
 */
export function sampleReportDocument(
  template: ReportTemplateListItem,
  language: PreviewLanguage,
  letterhead: PreviewLetterhead,
  nowUtc: string,
  design: ReportDesign = template.design ?? 'Classic',
): LaboratoryReportDocument {
  return {
    letterhead: { labNameEn: letterhead.labNameEn, labNameAr: letterhead.labNameAr, branchNameEn: null, branchNameAr: null },
    template: {
      templateId: template.id,
      nameEn: template.nameEn,
      language: template.language,
      showQr: template.showQr,
      logoFileId: template.logoFileId,
      signatureFileId: template.signatureFileId,
      headerTextEn: template.headerTextEn,
      headerTextAr: template.headerTextAr,
      footerTextEn: template.footerTextEn,
      footerTextAr: template.footerTextAr,
    },
    language,
    reportUnitKey: 'preview',
    titleEn: 'Laboratory report',
    titleAr: 'تقرير المختبر',
    patient: { number: 'P-000001', fullName: 'Sample Patient', nameAr: 'مريض نموذجي', gender: 'Female', ageYears: 34, ageMonths: null, ageDays: null, dateOfBirth: null },
    visit: { accession: 'PREVIEW-0001', registeredAtUtc: nowUtc, payerNameEn: 'Cash', payerNameAr: 'نقدي', doctorNameEn: 'Dr. Sample', doctorNameAr: 'د. نموذج' },
    samples: [{ sampleNumber: 'S-000001', collectedAtUtc: nowUtc, receivedAtUtc: nowUtc }],
    sections: [{ serviceId: 'preview-profile', headingEn: 'Sample profile', headingAr: 'ملف نموذجي', isProfile: true, lines: design === 'Cumulative' ? withHistory(SAMPLE_LINES, nowUtc) : SAMPLE_LINES }],
    signature: { verifiedByName: 'Dr. Sample Verifier', verifiedAtUtc: nowUtc, reviewedByName: null, reviewedAtUtc: null },
    isPreliminary: false,
    isPreview: true,
    printCount: 0,
    amendmentCount: 0,
    printListNumber: null,
    qrValue: 'PREVIEW-0001',
    generatedAtUtc: nowUtc,
    generatedBy: null,
    design,
    designSource: 'Template',
  };
}

function sampleLine(resultId: string, nameEn: string, nameAr: string, value: string, unit: string, range: string, flag: 'Normal' | 'High'): ReportLine {
  return { resultId, nameEn, nameAr, testType: 'Numeric', value, unit, range, flag, isPanic: false, status: 'Verified', comment: null, paragraphs: null, culture: null, methodology: null };
}

/** The Cumulative sample: three earlier visits of each line, 30, 60 and 90 days back (fictitious values). */
function withHistory(lines: readonly ReportLine[], nowUtc: string): readonly ReportLine[] {
  const now = new Date(nowUtc).getTime();
  const day = 24 * 60 * 60 * 1000;
  return lines.map((line) => {
    const base = Number(line.value);
    return {
      ...line,
      previous: [1, 2, 3].map((step) => ({
        accession: `PREVIEW-000${1 + step}`,
        registeredAtUtc: new Date(now - step * 30 * day).toISOString(),
        value: Number.isFinite(base) ? (base * (1 - step * 0.04)).toFixed(1) : line.value,
        unit: line.unit,
        flag: line.flag === 'High' && step === 3 ? 'Normal' : line.flag,
      })),
    };
  });
}

/** The edit request of a template as it is, with another design (the preview's "Use this design"); the images keep their own calls. */
export function templateRequestWithDesign(template: ReportTemplateListItem, design: ReportDesign | null): ReportTemplateRequest {
  return {
    nameEn: template.nameEn,
    nameAr: template.nameAr,
    type: template.type,
    showQr: template.showQr,
    language: template.language,
    headerTextEn: template.headerTextEn,
    headerTextAr: template.headerTextAr,
    footerTextEn: template.footerTextEn,
    footerTextAr: template.footerTextAr,
    design,
  };
}

/** Only the templates of result reports carry a design (CAT-273). */
export function templateTakesDesign(template: Pick<ReportTemplateListItem, 'type'>): boolean {
  return template.type === 'Results' || template.type === 'Culture' || template.type === 'Text';
}
