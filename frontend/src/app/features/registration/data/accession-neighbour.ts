/**
 * The accession one lower (-1) or higher (+1) than `accession` by its trailing number, keeping its width
 * (`ACC-2026-00042` → `ACC-2026-00041` / `…00043`); null when the accession ends in no number or the count would reach
 * zero. The results page keeps its own copy (features never import each other); a shared home is a candidate for `shared/`.
 */
export function accessionNeighbour(accession: string, direction: -1 | 1): string | null {
  const match = /^(.*?)(\d+)$/.exec(accession.trim());
  if (match === null || match[1] === undefined || match[2] === undefined) {
    return null;
  }

  const next = Number.parseInt(match[2], 10) + direction;
  if (!Number.isSafeInteger(next) || next < 1) {
    return null;
  }

  return `${match[1]}${String(next).padStart(match[2].length, '0')}`;
}
