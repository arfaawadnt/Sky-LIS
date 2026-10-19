/**
 * Reordering helpers of the service sequence page (CP-42b, change request 2026-10-19): pure functions over the rows the
 * page shows. The rule of the page: the visible rows keep the set of sequence numbers they already occupy and only their
 * order changes, so a reorder inside a filter never collides with the rows outside it (INV-CAT-004).
 */

/** What the helpers need of a sequence row. */
export interface SequencedRow {
  readonly id: string;
  readonly sequence: number;
}

/** A row's new number after a reorder, for `PUT …/services/sequence`. */
export interface SequenceAssignment {
  readonly serviceId: string;
  readonly sequence: number;
}

/** Where a dragged row lands relative to the row under the pointer. */
export type DropPlacement = 'before' | 'after';

/** Moves the item at `from` so that it ends at index `to` (both clamped); the same array when nothing moves. */
export function moveItem<T>(items: readonly T[], from: number, to: number): readonly T[] {
  const last = items.length - 1;
  if (last < 0 || from < 0 || from > last) {
    return items;
  }

  const target = Math.max(0, Math.min(to, last));
  if (target === from) {
    return items;
  }

  const moved = items[from] as T;
  const rest = items.filter((_, index) => index !== from);
  return [...rest.slice(0, target), moved, ...rest.slice(target)];
}

/** The index the dragged item ends at when dropped before or after the item at `target` (indexes in the same list). */
export function dropIndex(from: number, target: number, placement: DropPlacement): number {
  const after = placement === 'after';
  if (from < target) {
    return after ? target : target - 1;
  }

  return after ? target + 1 : target;
}

/** The sequence numbers the rows occupy, ascending (one per row). */
export function occupiedNumbers(rows: readonly SequencedRow[]): readonly number[] {
  return rows.map((row) => row.sequence).sort((a, b) => a - b);
}

/**
 * The position (1-based, among `occupied`) a typed number lands on: the place of that number, or of the last occupied
 * number below it; at least 1, at most the count. A number below every occupied one is the first position, above every
 * one the last.
 */
export function positionOfNumber(occupied: readonly number[], value: number): number {
  const below = occupied.filter((number) => number <= value).length;
  return Math.max(1, Math.min(below, occupied.length));
}

/** A typed position: a whole number above zero, else `null`. */
export function parseTypedNumber(text: string): number | null {
  const trimmed = text.trim();
  if (!/^\d+$/.test(trimmed)) {
    return null;
  }

  const value = Number(trimmed);
  return Number.isSafeInteger(value) && value > 0 ? value : null;
}

/**
 * The rows in the order of `order` (ids), each given the occupied number of its position. Ids `order` does not know are
 * ignored; rows `order` does not list follow at the end in their stored order, so a stale order never loses a row.
 */
export function applyOrder<T extends SequencedRow>(rows: readonly T[], order: readonly string[]): readonly T[] {
  const byId = new Map(rows.map((row) => [row.id, row]));
  const ordered: T[] = [];
  for (const id of order) {
    const row = byId.get(id);
    if (row) {
      ordered.push(row);
      byId.delete(id);
    }
  }

  ordered.push(...byId.values());
  const numbers = occupiedNumbers(rows);
  return ordered.map((row, index) => (row.sequence === numbers[index] ? row : { ...row, sequence: numbers[index] as number }));
}

/** The rows whose number differs between the stored rows and the reordered ones, as the save request lists them. */
export function assignmentsOf(stored: readonly SequencedRow[], reordered: readonly SequencedRow[]): readonly SequenceAssignment[] {
  const before = new Map(stored.map((row) => [row.id, row.sequence]));
  return reordered.filter((row) => before.get(row.id) !== row.sequence).map((row) => ({ serviceId: row.id, sequence: row.sequence }));
}
