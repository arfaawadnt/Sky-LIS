import type { Portal } from '../core/portal/portal';
import type { NavEntry, NavGroup } from './nav.registry';

export interface VisibleNavGroup {
  readonly group: NavGroup;
  readonly entries: readonly NavEntry[];
}

/**
 * The sidebar as the signed-in user sees it: the groups of the current portal, in registry order, each with the
 * entries the user may open (`permission` null or held), dropping empty groups. `search` narrows by label text
 * (the caller translates). Pure, so the shell spec can check it without rendering.
 */
export function filterNav(
  groups: readonly NavGroup[],
  entries: readonly NavEntry[],
  portal: Portal,
  can: (permission: string) => boolean,
  label: (entry: NavEntry) => string,
  search = '',
): readonly VisibleNavGroup[] {
  const needle = search.trim().toLowerCase();
  return groups
    .filter((group) => group.portal === portal)
    .map((group) => ({
      group,
      entries: entries.filter(
        (entry) => entry.portal === portal && entry.group === group.i18nKey && (entry.permission === null || can(entry.permission)) && (needle === '' || label(entry).toLowerCase().includes(needle)),
      ),
    }))
    .filter((visible) => visible.entries.length > 0);
}

/** The first visible entry across the groups (Enter in the navigation search opens it). */
export function firstEntry(groups: readonly VisibleNavGroup[]): NavEntry | null {
  return groups[0]?.entries[0] ?? null;
}

/** Whether `path` is the entry's route or below it (`/` matches only itself). */
export function isActivePath(entry: NavEntry, currentPath: string): boolean {
  if (entry.path === '/' || entry.path === '/admin') {
    return currentPath === entry.path;
  }

  return currentPath === entry.path || currentPath.startsWith(`${entry.path}/`);
}
