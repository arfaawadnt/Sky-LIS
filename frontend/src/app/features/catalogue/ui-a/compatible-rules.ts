import type { CompatibleConditionView } from '../../../core/api/compatible-conditions.contracts';

/**
 * The rules of a compatibility map, checked in the drawer before saving (the server checks them again, LIS-CR-015 M4):
 * never itself (CAT-281), the target active (CAT-282) and not merging into another (CAT-283, one level), a target cannot
 * merge (CAT-284). `map` holds every condition of the type: its target id or null (its own sample).
 */
export type CompatibleRuleCode = 'CAT-281' | 'CAT-282' | 'CAT-283' | 'CAT-284';

export interface CompatibleRuleBreach {
  readonly conditionId: string;
  readonly code: CompatibleRuleCode;
}

export function compatibleBreaches(conditions: readonly CompatibleConditionView[], map: ReadonlyMap<string, string | null>): readonly CompatibleRuleBreach[] {
  const byId = new Map(conditions.map((condition) => [condition.id, condition]));
  const targets = new Set([...map.values()].filter((target): target is string => target !== null));
  const breaches: CompatibleRuleBreach[] = [];
  for (const [conditionId, target] of map) {
    if (target === null) {
      continue;
    }

    if (target === conditionId) {
      breaches.push({ conditionId, code: 'CAT-281' });
    } else if (byId.get(target)?.isActive !== true) {
      breaches.push({ conditionId, code: 'CAT-282' });
    } else if (targets.has(conditionId)) {
      breaches.push({ conditionId, code: 'CAT-284' });
    } else if ((map.get(target) ?? null) !== null) {
      breaches.push({ conditionId, code: 'CAT-283' });
    }
  }

  return breaches;
}

/**
 * The conditions a condition may merge into under the current map: the other active conditions that merge into nothing,
 * plus its current target (kept selectable so an invalid stored choice still shows and can be changed). A condition that
 * others merge into is offered nothing else (CAT-284).
 */
export function targetChoices(conditions: readonly CompatibleConditionView[], conditionId: string, map: ReadonlyMap<string, string | null>): readonly CompatibleConditionView[] {
  const current = map.get(conditionId) ?? null;
  const isTarget = [...map.values()].includes(conditionId);
  return conditions.filter((candidate) => candidate.id === current || (!isTarget && candidate.id !== conditionId && candidate.isActive && (map.get(candidate.id) ?? null) === null));
}
