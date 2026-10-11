import { filterNav, firstEntry, isActivePath } from './nav.filter';
import { NAV_GROUPS, NAV_REGISTRY, type NavEntry, type NavGroup } from './nav.registry';

const groups: readonly NavGroup[] = [
  { portal: 'client', i18nKey: 'shell.group.overview', icon: 'grid' },
  { portal: 'client', i18nKey: 'shell.group.finance', icon: 'coins' },
  { portal: 'client', i18nKey: 'shell.group.setup', icon: 'settings' },
  { portal: 'admin', i18nKey: 'shell.group.overview', icon: 'grid' },
  { portal: 'admin', i18nKey: 'shell.group.administration', icon: 'building' },
];

const entries: readonly NavEntry[] = [
  { portal: 'client', group: 'shell.group.overview', path: '/', permission: null, i18nKey: 'shell.nav.home', release: 'R1' },
  { portal: 'client', group: 'shell.group.finance', path: '/treasury/treasuries', permission: 'treasury.view', i18nKey: 'shell.nav.treasuries', release: 'R1' },
  { portal: 'client', group: 'shell.group.finance', path: '/finance/payers', permission: 'pricing.view', i18nKey: 'shell.nav.payers', release: 'R1' },
  { portal: 'client', group: 'shell.group.setup', path: '/setup/branches', permission: 'organization.view', i18nKey: 'shell.nav.branches', release: 'R2' },
  { portal: 'admin', group: 'shell.group.overview', path: '/admin', permission: null, i18nKey: 'shell.nav.adminHome', release: 'R1' },
  { portal: 'admin', group: 'shell.group.administration', path: '/admin/tenants', permission: 'tenancy.view', i18nKey: 'shell.nav.tenants', release: 'R1' },
];

const labels: Readonly<Record<string, string>> = {
  'shell.nav.home': 'Dashboard',
  'shell.nav.treasuries': 'Treasuries',
  'shell.nav.payers': 'Payers',
  'shell.nav.branches': 'Branches',
  'shell.nav.adminHome': 'Dashboard',
  'shell.nav.tenants': 'Clients',
};

const label = (entry: NavEntry): string => labels[entry.i18nKey] ?? entry.i18nKey;

describe('filterNav (shell)', () => {
  it('shows the client groups with the entries the user may open, dropping empty groups', () => {
    const can = (permission: string): boolean => permission === 'treasury.view';
    const visible = filterNav(groups, entries, 'client', can, label);

    expect(visible.map((group) => group.group.i18nKey)).toEqual(['shell.group.overview', 'shell.group.finance']);
    expect(visible[1]?.entries.map((entry) => entry.path)).toEqual(['/treasury/treasuries']);
  });

  it('shows only the admin navigation for the platform tenant', () => {
    const visible = filterNav(groups, entries, 'admin', () => true, label);

    expect(visible.map((group) => group.group.i18nKey)).toEqual(['shell.group.overview', 'shell.group.administration']);
    expect(visible.flatMap((group) => group.entries.map((entry) => entry.path))).toEqual(['/admin', '/admin/tenants']);
  });

  it('narrows by the translated label and offers the first match', () => {
    const visible = filterNav(groups, entries, 'client', () => true, label, 'pay');

    expect(visible.length).toBe(1);
    expect(firstEntry(visible)?.path).toBe('/finance/payers');
    expect(firstEntry(filterNav(groups, entries, 'client', () => true, label, 'nothing'))).toBeNull();
  });

  it('hides out-of-scope and action-only entries from the menu (R1-01)', () => {
    const withActionOnly: readonly NavEntry[] = [
      ...entries,
      { portal: 'client', group: 'shell.group.finance', path: '/finance/refunds', permission: null, i18nKey: 'shell.nav.refunds', release: 'R1', menu: false },
      { portal: 'client', group: 'shell.group.finance', path: '/finance/later', permission: null, i18nKey: 'shell.nav.later', release: 'R1', menu: 'R2' },
    ];

    const r1 = filterNav(groups, withActionOnly, 'client', () => true, label).flatMap((group) => group.entries.map((entry) => entry.path));
    expect(r1).toEqual(['/', '/treasury/treasuries', '/finance/payers']);

    const r2 = filterNav(groups, withActionOnly, 'client', () => true, label, '', 'R2').flatMap((group) => group.entries.map((entry) => entry.path));
    expect(r2).toEqual(['/', '/treasury/treasuries', '/finance/payers', '/finance/later', '/setup/branches']);
  });

  it('keeps the registry consistent: every entry names a group of its portal, paths are unique', () => {
    const groupKeys = new Set(NAV_GROUPS.map((group) => `${group.portal}:${group.i18nKey}`));
    for (const entry of NAV_REGISTRY) {
      expect(groupKeys.has(`${entry.portal}:${entry.group}`)).toBe(true);
    }

    expect(new Set(NAV_REGISTRY.map((entry) => entry.path)).size).toBe(NAV_REGISTRY.length);
  });

  it('marks the active entry for the current path, with the homes matching exactly', () => {
    const home = entries[0] as NavEntry;
    const treasuries = entries[1] as NavEntry;
    const adminHome = entries[4] as NavEntry;

    expect(isActivePath(home, '/')).toBe(true);
    expect(isActivePath(home, '/treasury/access')).toBe(false);
    expect(isActivePath(treasuries, '/treasury/treasuries')).toBe(true);
    expect(isActivePath(treasuries, '/treasury/treasuries/42')).toBe(true);
    expect(isActivePath(treasuries, '/treasury/access')).toBe(false);
    expect(isActivePath(adminHome, '/admin')).toBe(true);
    expect(isActivePath(adminHome, '/admin/tenants')).toBe(false);
  });
});
