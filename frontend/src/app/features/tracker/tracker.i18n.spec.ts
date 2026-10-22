import trackerAr from '../../../../public/i18n/tracker.ar.json';
import trackerEn from '../../../../public/i18n/tracker.en.json';
import { TRACKER_ATTENTION, TRACKER_LANES } from '../../core/api/tracker.contracts';

type Dictionary = Readonly<Record<string, string>>;

const en: Dictionary = trackerEn;
const ar: Dictionary = trackerAr;
const FORMS = /(None|One|Two|Many)$/;

function placeholders(text: string | undefined): readonly string[] {
  return Array.from((text ?? '').matchAll(/\{(\w+)\}/g), (match) => match[1] ?? '').sort();
}

describe('Lab Tracker dictionaries', () => {
  it('hold the same keys in both languages (apart from the counted forms) with the same placeholders', () => {
    const base = (dictionary: Dictionary): string[] => Object.keys(dictionary).filter((key) => !(FORMS.test(key) && key.replace(FORMS, '') in dictionary)).sort();
    expect(base(ar)).toEqual(base(en));
    expect(base(en).filter((key) => placeholders(en[key]).join() !== placeholders(ar[key]).join())).toEqual([]);
  });

  it('name every stage, attention flag and next step', () => {
    const keys = [
      ...['Registered', ...TRACKER_LANES, 'Cancelled'].map((stage) => `stage.${stage}`),
      ...TRACKER_ATTENTION.map((attention) => `attention.${attention}`),
      ...['CollectSample', 'RecollectSample', 'EnterResults', 'ReviewResults', 'VerifyResults', 'TakePayment', 'PrintReport', 'None'].map((step) => `next.${step}`),
      ...['Paid', 'Partial', 'Unpaid', 'RefundDue'].map((status) => `payment.${status}`),
    ];
    expect(keys.filter((key) => !(key in en) || !(key in ar))).toEqual([]);
  });

  it('count in the Arabic dual and plural and never write "(s)"', () => {
    expect([ar['servicesTwo'], ar['servicesMany'], ar['live.secondsTwo']]).toEqual(['خدمتان', '{count} خدمة', 'مباشر · حُدِّث قبل ثانيتين']);
    expect(Object.values(en).filter((text) => /\w\((s|es)\)/.test(text))).toEqual([]);
  });
});
