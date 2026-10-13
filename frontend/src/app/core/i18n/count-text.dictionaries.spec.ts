import accountAr from '../../../../public/i18n/account.ar.json';
import accountEn from '../../../../public/i18n/account.en.json';
import adminHomeAr from '../../../../public/i18n/adminHome.ar.json';
import adminHomeEn from '../../../../public/i18n/adminHome.en.json';
import authAr from '../../../../public/i18n/auth.ar.json';
import authEn from '../../../../public/i18n/auth.en.json';
import billingAr from '../../../../public/i18n/billing.ar.json';
import billingEn from '../../../../public/i18n/billing.en.json';
import catalogueAAr from '../../../../public/i18n/catalogue-a.ar.json';
import catalogueAEn from '../../../../public/i18n/catalogue-a.en.json';
import catalogueBAr from '../../../../public/i18n/catalogue-b.ar.json';
import catalogueBEn from '../../../../public/i18n/catalogue-b.en.json';
import homeAr from '../../../../public/i18n/home.ar.json';
import homeEn from '../../../../public/i18n/home.en.json';
import integrationAr from '../../../../public/i18n/integration.ar.json';
import integrationEn from '../../../../public/i18n/integration.en.json';
import organizationAr from '../../../../public/i18n/organization.ar.json';
import organizationEn from '../../../../public/i18n/organization.en.json';
import patientsAr from '../../../../public/i18n/patients.ar.json';
import patientsEn from '../../../../public/i18n/patients.en.json';
import platformAr from '../../../../public/i18n/platform.ar.json';
import platformEn from '../../../../public/i18n/platform.en.json';
import pricingAr from '../../../../public/i18n/pricing.ar.json';
import pricingEn from '../../../../public/i18n/pricing.en.json';
import publicAr from '../../../../public/i18n/public.ar.json';
import publicEn from '../../../../public/i18n/public.en.json';
import registrationAr from '../../../../public/i18n/registration.ar.json';
import registrationEn from '../../../../public/i18n/registration.en.json';
import resultsAr from '../../../../public/i18n/results.ar.json';
import resultsEn from '../../../../public/i18n/results.en.json';
import samplesAr from '../../../../public/i18n/samples.ar.json';
import samplesEn from '../../../../public/i18n/samples.en.json';
import sharedAr from '../../../../public/i18n/shared.ar.json';
import sharedEn from '../../../../public/i18n/shared.en.json';
import shellAr from '../../../../public/i18n/shell.ar.json';
import shellEn from '../../../../public/i18n/shell.en.json';
import treasuryAr from '../../../../public/i18n/treasury.ar.json';
import treasuryEn from '../../../../public/i18n/treasury.en.json';

type Dictionary = Readonly<Record<string, string>>;

/**
 * Validation v0.5.0 F-04: counted sentences carry their forms (`<key>None/One/Two/Many`, `count-text.ts`), never "(s)" nor
 * "إعداد/إعدادات". Results, Registration, Billing and Patients are covered too (validation v0.5.0 F-11).
 */
const DICTIONARIES: readonly (readonly [string, Dictionary, Dictionary])[] = [
  ['account', accountEn, accountAr],
  ['adminHome', adminHomeEn, adminHomeAr],
  ['auth', authEn, authAr],
  ['billing', billingEn, billingAr],
  ['catalogue-a', catalogueAEn, catalogueAAr],
  ['catalogue-b', catalogueBEn, catalogueBAr],
  ['home', homeEn, homeAr],
  ['integration', integrationEn, integrationAr],
  ['organization', organizationEn, organizationAr],
  ['patients', patientsEn, patientsAr],
  ['platform', platformEn, platformAr],
  ['pricing', pricingEn, pricingAr],
  ['public', publicEn, publicAr],
  ['registration', registrationEn, registrationAr],
  ['results', resultsEn, resultsAr],
  ['samples', samplesEn, samplesAr],
  ['shared', sharedEn, sharedAr],
  ['shell', shellEn, shellAr],
  ['treasury', treasuryEn, treasuryAr],
];

const FORMS = ['None', 'One', 'Two', 'Many'] as const;

function placeholders(text: string): readonly string[] {
  return Array.from(text.matchAll(/\{(\w+)\}/g), (match) => match[1] ?? '').sort();
}

/** The variant keys of a dictionary with their base key: `parameters.savedOne` → `parameters.saved`. */
function variants(dictionary: Dictionary): readonly (readonly [string, string])[] {
  return Object.keys(dictionary).flatMap((key) => {
    const form = FORMS.find((suffix) => key.endsWith(suffix));
    const base = form === undefined ? '' : key.slice(0, -form.length);
    return form !== undefined && base in dictionary && /\{count\}/.test(dictionary[base] ?? '') ? [[key, base] as const] : [];
  });
}

describe('Counted sentences in the dictionaries (F-04)', () => {
  it.each(DICTIONARIES)('%s has no "(s)" plural in English', (_, en) => {
    expect(Object.entries(en).filter(([, text]) => /\w\((s|es)\)/.test(text))).toEqual([]);
  });

  it.each(DICTIONARIES)('%s has no "singular/plural" pair beside a count in Arabic', (_, __, ar) => {
    expect(Object.entries(ar).filter(([, text]) => /\{count\}[^.؟،]*[ء-ي]+\/[ء-ي]+/.test(text))).toEqual([]);
  });

  it.each(DICTIONARIES)('%s: every form of a counted sentence has its base in both languages and no placeholder the base lacks', (_, en, ar) => {
    const problems: string[] = [];
    for (const [language, dictionary, other] of [['en', en, ar], ['ar', ar, en]] as const) {
      for (const [key, base] of variants(dictionary)) {
        if (!(base in other)) {
          problems.push(`${language} ${key}: base missing in the other language`);
        }

        const allowed = new Set([...placeholders(dictionary[base] ?? ''), 'count']);
        const extra = placeholders(dictionary[key] ?? '').filter((name) => !allowed.has(name));
        if (extra.length > 0) {
          problems.push(`${language} ${key}: ${extra.join(', ')}`);
        }
      }
    }

    expect(problems).toEqual([]);
  });

  it('the singular forms read as such (one parameter, one member, one label)', () => {
    expect(platformEn['parameters.savedOne']).toBe('1 parameter saved.');
    expect(platformAr['parameters.savedOne']).toBe('تم حفظ إعداد واحد.');
    expect(organizationEn['roles.confirmPropagate.messageOne']).toContain('1 member.');
    expect(organizationEn['roles.keptLaterReleaseOne']).toContain('1 permission ');
    expect(samplesEn['labels.printedOne']).toBe('1 label sent to the printer.');
  });

  it('F-11: the Results, Registration and Billing counts read in the Arabic dual and plural', () => {
    expect([resultsAr['case.resultsPartTwo'], resultsAr['case.resultsPart'], resultsAr['case.unitsPartTwo']]).toEqual(['نتيجتان', '{count} نتائج', 'وحدتا تقرير']);
    expect(resultsAr['report.reprintTwo']).toBe('إعادة طباعة (طُبعت نسختان)');
    expect([resultsEn['report.amendedOne'], resultsEn['report.amended']]).toEqual(['Amended once', 'Amended {count} times']);
    expect(registrationAr['success.tubesTwo']).toBe('صدرت عينتان.');
    expect(billingAr['closing.countMany']).toBe('{count} إيصالًا');
  });
});
