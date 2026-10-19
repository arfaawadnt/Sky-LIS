import { accessionNeighbour } from './accession-neighbour';

describe('accessionNeighbour (CP-10b previous / next)', () => {
  it('steps the trailing number and keeps its width', () => {
    expect(accessionNeighbour('ACC-2026-00042', -1)).toBe('ACC-2026-00041');
    expect(accessionNeighbour('ACC-2026-00042', 1)).toBe('ACC-2026-00043');
    expect(accessionNeighbour('ACC-2026-00099', 1)).toBe('ACC-2026-00100');
  });

  it('refuses an accession without a trailing number and never reaches zero', () => {
    expect(accessionNeighbour('ACC', 1)).toBeNull();
    expect(accessionNeighbour('', -1)).toBeNull();
    expect(accessionNeighbour('ACC-2026-00001', -1)).toBeNull();
  });
});
