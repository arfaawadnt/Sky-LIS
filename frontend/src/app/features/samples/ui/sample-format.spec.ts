import { sampleItem } from '../testing/samples.fixtures';
import { formatDuration, normalizeSampleNumber, patientLabel, pickName, sampleTypeLabel, todayWindow, unitLabel } from './sample-format';

const units = { days: 'd', hours: 'h', minutes: 'min', seconds: 's' };

describe('sample formatting helpers', () => {
  it('formats durations in the LDM delay notation', () => {
    expect(formatDuration(null, units)).toBe('—');
    expect(formatDuration(42, units)).toBe('42s');
    expect(formatDuration(600, units)).toBe('10min');
    expect(formatDuration(3600, units)).toBe('1h');
    expect(formatDuration(2 * 86400 + 12 * 3600 + 38 * 60, units)).toBe('2d 12h 38min');
  });

  it('picks the name of the current language with fallbacks', () => {
    expect(pickName('Serum', 'مصل', 'ar')).toBe('مصل');
    expect(pickName('Serum', null, 'ar')).toBe('Serum');
    expect(pickName(null, ' ', 'en', 'id')).toBe('id');
  });

  it('labels patients, units and sample types, falling back to short ids when the catalogue has no names', () => {
    const row = sampleItem({ sampleConditionId: 'cond-1', sampleConditionNameEn: 'Fasting', sampleConditionNameAr: 'صائم' });
    expect(patientLabel(row, 'en')).toBe('PAT-2026-00001 · Mona Adel');
    expect(unitLabel(row, 'ar')).toBe('C · الكيمياء');
    expect(sampleTypeLabel(row, 'en')).toBe('Serum · Fasting');

    const bare = sampleItem({ sampleTypeId: '0199a1b2-c3d4-7e5f', sampleTypeNameEn: null, sampleTypeNameAr: null, unitCode: null, clinicalGroupNameEn: null, clinicalGroupNameAr: null, clinicalGroupId: 'abcdef0123456789' });
    expect(sampleTypeLabel(bare, 'en')).toBe('0199a1b2…');
    expect(unitLabel(bare, 'en')).toBe('abcdef01…');
  });

  it('gives the local day as a UTC window and normalises scanned barcodes', () => {
    const now = new Date(2026, 9, 9, 15, 30);
    const window = todayWindow(now);
    expect(new Date(window.from).getTime()).toBe(new Date(2026, 9, 9).getTime());
    expect(new Date(window.to).getTime()).toBe(new Date(2026, 9, 10).getTime());
    expect(normalizeSampleNumber('  smp-2026-00001 ')).toBe('SMP-2026-00001');
  });
});
