import accountAr from '../../../../public/i18n/account.ar.json';
import accountEn from '../../../../public/i18n/account.en.json';
import catalogueAAr from '../../../../public/i18n/catalogue-a.ar.json';
import catalogueAEn from '../../../../public/i18n/catalogue-a.en.json';
import catalogueBAr from '../../../../public/i18n/catalogue-b.ar.json';
import catalogueBEn from '../../../../public/i18n/catalogue-b.en.json';
import organizationAr from '../../../../public/i18n/organization.ar.json';
import organizationEn from '../../../../public/i18n/organization.en.json';
import patientsAr from '../../../../public/i18n/patients.ar.json';
import patientsEn from '../../../../public/i18n/patients.en.json';
import pricingAr from '../../../../public/i18n/pricing.ar.json';
import pricingEn from '../../../../public/i18n/pricing.en.json';
import registrationAr from '../../../../public/i18n/registration.ar.json';
import registrationEn from '../../../../public/i18n/registration.en.json';
import resultsAr from '../../../../public/i18n/results.ar.json';
import resultsEn from '../../../../public/i18n/results.en.json';
import treasuryAr from '../../../../public/i18n/treasury.ar.json';
import treasuryEn from '../../../../public/i18n/treasury.en.json';

type Dictionary = Record<string, string>;

/**
 * UAT R1-06: the export confirmation toast read "The CSV file was downloaded." on some pages and "The file was exported."
 * or "{count} rows were exported." on others. One wording per language, kept in each module's dictionary.
 */
describe('Export confirmation wording', () => {
  const toasts: readonly (readonly [string, Dictionary, Dictionary, string])[] = [
    ['account', accountEn, accountAr, 'audit.exported'],
    ['catalogue-a', catalogueAEn, catalogueAAr, 'exported'],
    ['catalogue-b', catalogueBEn, catalogueBAr, 'common.exported'],
    ['organization', organizationEn, organizationAr, 'exported'],
    ['patients', patientsEn, patientsAr, 'exported'],
    ['pricing', pricingEn, pricingAr, 'exported'],
    ['registration', registrationEn, registrationAr, 'exported'],
    ['results', resultsEn, resultsAr, 'worklist.exported'],
    ['treasury', treasuryEn, treasuryAr, 'exported'],
  ];

  it.each(toasts)('%s uses the one wording in English and Arabic', (_, en, ar, key) => {
    expect(en[key]).toBe('The CSV file was downloaded.');
    expect(ar[key]).toBe('تم تنزيل ملف CSV.');
  });

  it('the branch phone refusal states both limits, as the server does (ORG-015)', () => {
    for (const dictionary of [organizationEn as Dictionary, organizationAr as Dictionary]) {
      expect(dictionary['branches.error.ORG-015']).toBe(dictionary['error.ORG-015']);
      expect(dictionary['error.ORG-015']).toContain('5');
      expect(dictionary['error.ORG-015']).toContain('20');
    }
  });
});
