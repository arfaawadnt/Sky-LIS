import { checkCompatibilityMembers, moveMember } from './compatibility-rules';

describe('compatibility group rules (LIS-CR-015 M4)', () => {
  it('accepts two distinct members, "no condition" included', () => {
    expect(checkCompatibilityMembers([{ sampleTypeId: 'ser', sampleConditionId: 'fast' }, { sampleTypeId: 'ser', sampleConditionId: null }])).toEqual({ list: null, rows: new Map() });
  });

  it('marks too few members, a row without a type and a repeated pair', () => {
    expect(checkCompatibilityMembers([{ sampleTypeId: 'ser', sampleConditionId: null }]).list).toBe('CAT-280');
    const check = checkCompatibilityMembers([
      { sampleTypeId: 'ser', sampleConditionId: null },
      { sampleTypeId: null, sampleConditionId: null },
      { sampleTypeId: 'ser', sampleConditionId: null },
    ]);
    expect([...check.rows]).toEqual([
      [1, 'required'],
      [2, 'CAT-283'],
    ]);
  });

  it('refuses more than thirty members', () => {
    expect(checkCompatibilityMembers(Array.from({ length: 31 }, (_, index) => ({ sampleTypeId: `t${String(index)}`, sampleConditionId: null }))).list).toBe('CAT-287');
  });

  it('moves a member up and down within the list', () => {
    expect(moveMember(['a', 'b', 'c'], 2, -1)).toEqual(['a', 'c', 'b']);
    expect(moveMember(['a', 'b', 'c'], 0, 1)).toEqual(['b', 'a', 'c']);
    expect(moveMember(['a', 'b'], 0, -1)).toEqual(['a', 'b']);
  });
});
