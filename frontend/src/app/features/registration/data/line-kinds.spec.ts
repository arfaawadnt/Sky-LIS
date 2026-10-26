import { followsLine, formulaCodeOf } from './line-kinds';

describe('line kinds (LIS-CR-015)', () => {
  it('package members and formula operands follow another line', () => {
    expect(followsLine('PackageMember')).toBe(true);
    expect(followsLine('FormulaOperand')).toBe(true);
    expect(followsLine('Service')).toBe(false);
    expect(followsLine('Package')).toBe(false);
  });

  it('names the formula line of an operand only', () => {
    const lines = [
      { id: 'a', code: 'GFR' },
      { id: 'b', code: 'CREA' },
    ];
    expect(formulaCodeOf({ kind: 'FormulaOperand', parentLineId: 'a' }, lines)).toBe('GFR');
    expect(formulaCodeOf({ kind: 'PackageMember', parentLineId: 'a' }, lines)).toBeNull();
    expect(formulaCodeOf({ kind: 'FormulaOperand', parentLineId: 'z' }, lines)).toBeNull();
    expect(formulaCodeOf({ kind: 'Service', parentLineId: null }, lines)).toBeNull();
  });
});
