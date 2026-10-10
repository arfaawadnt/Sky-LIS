/**
 * The release being delivered (decided with the business on the R1 prototype builds: prototypes/dist/*-v2.1-Release1.html
 * and *-v3.1-Release1.html). Pages, navigation entries, dashboard tiles and quick actions of a later release stay in the
 * code, ready, but are not shown. Raise to 'R2' to open the Release 2 pages.
 */
export type Release = 'R1' | 'R2';

export const RELEASE_SCOPE: Release = 'R1';

/** True when an item belongs to the delivered scope (R1 shows R1 items; R2 shows R1 and R2). */
export function inReleaseScope(release: Release, scope: Release = RELEASE_SCOPE): boolean {
  return scope === 'R2' || release === 'R1';
}
