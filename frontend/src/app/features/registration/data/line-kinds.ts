import type { VisitLineKind } from '../../../core/api/registration.contracts';

/**
 * A line that follows another one: a package member follows its package, a formula operand (LIS-CR-015) the line whose
 * formula reads it. Such a line carries no price of its own, is listed under its line and is never cancelled, released or
 * adjusted alone.
 */
export function followsLine(kind: VisitLineKind): boolean {
  return kind === 'PackageMember' || kind === 'FormulaOperand';
}

/** The code of the line a formula operand line was ordered for, among the visit's lines (null for any other line). */
export function formulaCodeOf(line: { readonly kind: VisitLineKind; readonly parentLineId: string | null }, lines: readonly { readonly id: string; readonly code: string }[]): string | null {
  if (line.kind !== 'FormulaOperand' || line.parentLineId === null) {
    return null;
  }

  return lines.find((candidate) => candidate.id === line.parentLineId)?.code ?? null;
}
