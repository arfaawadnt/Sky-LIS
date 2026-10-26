import { SAMPLE_COMPATIBILITY_LIMITS } from '../../../core/api/sample-compatibility.contracts';

/** A member row of the editor; the sample type is null until chosen. */
export interface CompatibilityMemberDraft {
  readonly sampleTypeId: string | null;
  readonly sampleConditionId: string | null;
}

/** The server codes the editor checks before saving (LIS-CR-015 M4), plus `required` for a row without a sample type. */
export type CompatibilityRuleCode = 'CAT-280' | 'CAT-283' | 'CAT-287' | 'required';

export interface CompatibilityRuleCheck {
  /** A rule of the whole list (too few / too many members), or null. */
  readonly list: CompatibilityRuleCode | null;
  /** The rule each row breaks, by row index. */
  readonly rows: ReadonlyMap<number, CompatibilityRuleCode>;
}

/**
 * The rules of a group's member list as the server applies them: at least two members (CAT-280), at most thirty (CAT-287),
 * a sample type on every row, and a (type, condition) pair once per group (CAT-283; "no condition" counts as one value).
 * A pair held by another active group (CAT-284) needs the other groups: the server answers it.
 */
export function checkCompatibilityMembers(members: readonly CompatibilityMemberDraft[]): CompatibilityRuleCheck {
  const rows = new Map<number, CompatibilityRuleCode>();
  const seen = new Set<string>();
  members.forEach((member, index) => {
    if (member.sampleTypeId === null) {
      rows.set(index, 'required');
      return;
    }

    const key = `${member.sampleTypeId}|${member.sampleConditionId ?? '-'}`;
    if (seen.has(key)) {
      rows.set(index, 'CAT-283');
    }

    seen.add(key);
  });

  const list = members.length < SAMPLE_COMPATIBILITY_LIMITS.minMembers ? 'CAT-280' : members.length > SAMPLE_COMPATIBILITY_LIMITS.maxMembers ? 'CAT-287' : null;
  return { list, rows };
}

/** Moves the row at `index` by `delta` (−1 up, +1 down); out-of-range moves return the list unchanged. */
export function moveMember<T>(members: readonly T[], index: number, delta: -1 | 1): readonly T[] {
  const target = index + delta;
  if (index < 0 || index >= members.length || target < 0 || target >= members.length) {
    return members;
  }

  const next = [...members];
  const [moved] = next.splice(index, 1);
  if (moved === undefined) {
    return members;
  }

  next.splice(target, 0, moved);
  return next;
}
