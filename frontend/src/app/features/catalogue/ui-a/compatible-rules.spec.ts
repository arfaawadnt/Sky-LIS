import type { CompatibleConditionView } from '../../../core/api/compatible-conditions.contracts';
import { compatibleBreaches, targetChoices } from './compatible-rules';

function condition(id: string, isActive = true): CompatibleConditionView {
  return { id, code: id.toUpperCase(), nameEn: id, nameAr: id, isActive, mergesIntoConditionId: null };
}

const conditions = [condition('fast'), condition('rand'), condition('pp'), condition('old', false)];

describe('compatible condition rules (LIS-CR-015 M4)', () => {
  it('accepts a map of active one-level merges', () => {
    expect(compatibleBreaches(conditions, new Map([['rand', 'fast'], ['pp', 'fast'], ['fast', null]]))).toEqual([]);
  });

  it('marks itself, an inactive target and both sides of a chain', () => {
    expect(compatibleBreaches(conditions, new Map([['rand', 'rand']]))).toEqual([{ conditionId: 'rand', code: 'CAT-281' }]);
    expect(compatibleBreaches(conditions, new Map([['rand', 'old']]))).toEqual([{ conditionId: 'rand', code: 'CAT-282' }]);
    expect(compatibleBreaches(conditions, new Map([['pp', 'rand'], ['rand', 'fast']]))).toEqual([
      { conditionId: 'pp', code: 'CAT-283' },
      { conditionId: 'rand', code: 'CAT-284' },
    ]);
  });

  it('offers the other active conditions that merge into nothing, plus the current choice', () => {
    const map = new Map<string, string | null>([['rand', 'fast'], ['fast', null], ['pp', null], ['old', null]]);
    expect(targetChoices(conditions, 'pp', map).map((choice) => choice.id)).toEqual(['fast']);
    expect(targetChoices(conditions, 'rand', map).map((choice) => choice.id)).toEqual(['fast', 'pp']);
    expect(targetChoices(conditions, 'fast', map)).toEqual([]);
  });
});
