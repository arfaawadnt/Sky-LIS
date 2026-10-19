import { applyOrder, assignmentsOf, dropIndex, moveItem, occupiedNumbers, parseTypedNumber, positionOfNumber } from './sequence-order';

const rows = [
  { id: 'a', sequence: 10 },
  { id: 'b', sequence: 20 },
  { id: 'c', sequence: 30 },
  { id: 'd', sequence: 45 },
] as const;

describe('sequence-order helpers (CP-42b)', () => {
  it('moveItem moves an item to its final index, clamps the ends and keeps the array when nothing moves', () => {
    expect(moveItem(['a', 'b', 'c', 'd'], 0, 2)).toEqual(['b', 'c', 'a', 'd']);
    expect(moveItem(['a', 'b', 'c', 'd'], 3, 1)).toEqual(['a', 'd', 'b', 'c']);
    expect(moveItem(['a', 'b', 'c', 'd'], 1, 99)).toEqual(['a', 'c', 'd', 'b']);
    expect(moveItem(['a', 'b', 'c', 'd'], 2, -5)).toEqual(['c', 'a', 'b', 'd']);
    const same = ['a', 'b'];
    expect(moveItem(same, 1, 1)).toBe(same);
    expect(moveItem(same, 7, 0)).toBe(same);
    expect(moveItem([], 0, 0)).toEqual([]);
  });

  it('dropIndex lands the dragged row before or after the row under the pointer in both directions', () => {
    // dragging downwards: the target shifts up by one once the dragged row leaves its place
    expect(dropIndex(0, 2, 'before')).toBe(1);
    expect(dropIndex(0, 2, 'after')).toBe(2);
    // dragging upwards
    expect(dropIndex(3, 1, 'before')).toBe(1);
    expect(dropIndex(3, 1, 'after')).toBe(2);
    // the row's own neighbours: a no-op lands on the same index
    expect(moveItem(['a', 'b', 'c'], 1, dropIndex(1, 0, 'after'))).toEqual(['a', 'b', 'c']);
    expect(moveItem(['a', 'b', 'c'], 1, dropIndex(1, 2, 'before'))).toEqual(['a', 'b', 'c']);
  });

  it('occupiedNumbers lists the numbers ascending, one per row', () => {
    expect(occupiedNumbers([{ id: 'x', sequence: 7 }, { id: 'y', sequence: 2 }, { id: 'z', sequence: 7 }])).toEqual([2, 7, 7]);
  });

  it('positionOfNumber maps a typed number to the position of that number or of the last one below it', () => {
    const occupied = occupiedNumbers(rows);
    expect(positionOfNumber(occupied, 10)).toBe(1);
    expect(positionOfNumber(occupied, 20)).toBe(2);
    expect(positionOfNumber(occupied, 45)).toBe(4);
    expect(positionOfNumber(occupied, 25)).toBe(2);
    expect(positionOfNumber(occupied, 1)).toBe(1);
    expect(positionOfNumber(occupied, 999)).toBe(4);
    expect(positionOfNumber([], 3)).toBe(1);
  });

  it('parseTypedNumber accepts whole numbers above zero only', () => {
    expect(parseTypedNumber(' 12 ')).toBe(12);
    expect(parseTypedNumber('0')).toBeNull();
    expect(parseTypedNumber('-3')).toBeNull();
    expect(parseTypedNumber('2.5')).toBeNull();
    expect(parseTypedNumber('')).toBeNull();
    expect(parseTypedNumber('abc')).toBeNull();
  });

  it('applyOrder renumbers the rows within the numbers they already occupy, so a filtered subset keeps its holes', () => {
    const reordered = applyOrder(rows, ['c', 'a', 'd', 'b']);
    expect(reordered.map((row) => [row.id, row.sequence])).toEqual([
      ['c', 10],
      ['a', 20],
      ['d', 30],
      ['b', 45],
    ]);
    // untouched rows keep their identity (no change to save)
    expect(applyOrder(rows, ['a', 'b', 'c', 'd'])[0]).toBe(rows[0]);
  });

  it('applyOrder ignores unknown ids and appends rows the order does not list', () => {
    const reordered = applyOrder(rows, ['zz', 'd', 'a']);
    expect(reordered.map((row) => row.id)).toEqual(['d', 'a', 'b', 'c']);
    expect(reordered.map((row) => row.sequence)).toEqual([10, 20, 30, 45]);
  });

  it('assignmentsOf lists only the rows whose number changed, in the new order', () => {
    const reordered = applyOrder(rows, ['a', 'c', 'b', 'd']);
    expect(assignmentsOf(rows, reordered)).toEqual([
      { serviceId: 'c', sequence: 20 },
      { serviceId: 'b', sequence: 30 },
    ]);
    expect(assignmentsOf(rows, applyOrder(rows, ['a', 'b', 'c', 'd']))).toEqual([]);
  });

  it('a move to a typed number composes the helpers: the row takes the place of that number, the rest shift', () => {
    const order = rows.map((row) => row.id);
    const occupied = occupiedNumbers(rows);
    // "d" typed 10: first
    const toFirst = applyOrder(rows, moveItem(order, 3, positionOfNumber(occupied, 10) - 1));
    expect(toFirst.map((row) => `${row.id}${row.sequence}`)).toEqual(['d10', 'a20', 'b30', 'c45']);
    // "a" typed 30: third, the rows in between move up
    const toThird = applyOrder(rows, moveItem(order, 0, positionOfNumber(occupied, 30) - 1));
    expect(toThird.map((row) => `${row.id}${row.sequence}`)).toEqual(['b10', 'c20', 'a30', 'd45']);
    // "a" typed 25 (a hole): between b and c, that is c's old place
    const toHole = applyOrder(rows, moveItem(order, 0, positionOfNumber(occupied, 25) - 1));
    expect(toHole.map((row) => row.id)).toEqual(['b', 'a', 'c', 'd']);
  });
});
