import catalogueBAr from '../../../../../public/i18n/catalogue-b.ar.json';
import catalogueBEn from '../../../../../public/i18n/catalogue-b.en.json';
import { REPORT_TEMPLATE_LANGUAGES, REPORT_TEMPLATE_TYPES } from '../../../core/api/catalogue-b.contracts';
import { TEMPLATE_LANGUAGE_LABELS, TEMPLATE_TYPE_LABELS } from './report-templates.page';

/**
 * UAT R2-03: in Arabic the report template drawer showed the Document type and Language options in English. The options
 * carry both texts, and they are the dictionaries' words, so the grid column and the drawer read the same.
 */
describe('Report template option labels (UAT R2-03)', () => {
  const en = catalogueBEn as Record<string, string>;
  const ar = catalogueBAr as Record<string, string>;

  it('every document type has its English and Arabic label, as in the dictionaries', () => {
    for (const type of REPORT_TEMPLATE_TYPES) {
      expect(TEMPLATE_TYPE_LABELS[type]).toEqual([en[`templateType.${type}`], ar[`templateType.${type}`]]);
      expect(TEMPLATE_TYPE_LABELS[type][1]).toMatch(/[؀-ۿ]/);
    }
  });

  it('every language has its English and Arabic label, as in the dictionaries', () => {
    for (const language of REPORT_TEMPLATE_LANGUAGES) {
      expect(TEMPLATE_LANGUAGE_LABELS[language]).toEqual([en[`templateLanguage.${language}`], ar[`templateLanguage.${language}`]]);
      expect(TEMPLATE_LANGUAGE_LABELS[language][1]).toMatch(/[؀-ۿ]/);
    }
  });
});
