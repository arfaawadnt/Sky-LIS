import { reportDesignOf, reportDesignOverride } from '../../../../../core/api/report-designs';
import { flagToneOf, previousColumns, printDay, rangeBar, rangeLimits } from './report-design-format';

describe('Report design helpers (LIS-CR-010)', () => {
  it('reads two-sided and upper-only printed ranges, and nothing else', () => {
    expect(rangeLimits('3.5 – 5.0')).toEqual({ low: 3.5, high: 5 });
    expect(rangeLimits('70-110')).toEqual({ low: 70, high: 110 });
    expect(rangeLimits('< 200')).toEqual({ low: 0, high: 200 });
    expect(rangeLimits('Negative')).toBeNull();
    expect(rangeLimits('5 – 3')).toBeNull();
    expect(rangeLimits(null)).toBeNull();
  });

  it('places the marker on a track whose normal zone is 25–75 %, clamped at the ends', () => {
    expect(rangeBar('95.0', '70 – 110')).toEqual({ marker: 56.3, position: 'inside' });
    expect(rangeBar('6.8', '3.5 – 5.0')).toEqual({ marker: 98, position: 'above' });
    expect(rangeBar('1', '3.5 – 5.0')).toEqual({ marker: 2, position: 'below' });
    expect(rangeBar('Positive', '3.5 – 5.0')).toBeNull();
    expect(rangeBar('4.0', 'see comment')).toBeNull();
  });

  it('gives a panic value the critical tone whatever its flag', () => {
    expect(flagToneOf('Normal')).toBe('normal');
    expect(flagToneOf('High')).toBe('abnormal');
    expect(flagToneOf('CriticalLow')).toBe('critical');
    expect(flagToneOf('Normal', true)).toBe('critical');
    expect(flagToneOf(null)).toBe('none');
  });

  it('pads the previous values to three columns and prints their day with Latin digits', () => {
    expect(previousColumns(['a'])).toEqual(['a', null, null]);
    expect(previousColumns(null)).toEqual([null, null, null]);
    expect(printDay('2026-09-20T08:00:00Z', 'en')).toBe('20/09/2026');
    expect(printDay('2026-09-20T08:00:00Z', 'ar')).not.toMatch(/[٠-٩]/);
    expect(printDay(null, 'en')).toBe('—');
  });

  it('maps stored values to designs: the parameter form, the member form, and Classic or no override for anything else', () => {
    expect(reportDesignOf('cumulative')).toBe('Cumulative');
    expect(reportDesignOf('Modern')).toBe('Modern');
    expect(reportDesignOf('fancy')).toBe('Classic');
    expect(reportDesignOf(undefined)).toBe('Classic');
    expect(reportDesignOverride('letterhead')).toBe('Letterhead');
    expect(reportDesignOverride('')).toBeNull();
  });
});
