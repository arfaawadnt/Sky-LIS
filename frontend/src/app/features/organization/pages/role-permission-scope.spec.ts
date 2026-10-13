import type { PermissionCatalogue } from '../../../core/api/organization.contracts';
import { NAV_REGISTRY } from '../../../shell/nav.registry';
import { PERMISSION_MODULE_RELEASE, catalogueInScope, permissionInScope, permissionRelease, splitByScope } from './role-permission-scope';

const moduleOf = (key: string): PermissionCatalogue['modules'][number] => ({ key, displayName: key, actions: ['view'], platformOnly: false });

/** Validation v0.5.0 O-7: the privilege matrix of the role editor offers the modules of the delivered release only. */
describe('Role permission scope (O-7)', () => {
  it('maps the six Release 2 modules of the report, and every other module to Release 1', () => {
    expect(Object.keys(PERMISSION_MODULE_RELEASE).sort()).toEqual(['automation', 'claims', 'integration', 'quality', 'support', 'wallet']);
    expect([permissionRelease('claims.view'), permissionRelease('wallet'), permissionRelease('registration.create'), permissionRelease('samples.view')]).toEqual(['R2', 'R2', 'R1', 'R1']);
    expect([permissionInScope('quality.manage', 'R1'), permissionInScope('quality.manage', 'R2'), permissionInScope('catalogue.edit', 'R1')]).toEqual([false, true, true]);
  });

  it('agrees with the navigation: no Release 1 page of either portal is gated by a key of a Release 2 module', () => {
    const conflicts = NAV_REGISTRY.filter((entry) => entry.release === 'R1' && entry.permission !== null && permissionRelease(entry.permission) === 'R2').map((entry) => entry.path);
    expect(conflicts).toEqual([]);
    // and each Release 2 module has pages, all of them Release 2 (the mapping is not stale)
    for (const key of ['integration', 'support', 'wallet']) {
      const entries = NAV_REGISTRY.filter((entry) => entry.permission?.startsWith(`${key}.`));
      expect([key, entries.length > 0 && entries.every((entry) => entry.release === 'R2')]).toEqual([key, true]);
    }
  });

  it('filters the catalogue and splits a role into shown and kept keys', () => {
    const catalogue: PermissionCatalogue = { modules: [moduleOf('registration'), moduleOf('claims'), moduleOf('integration'), moduleOf('results')], actions: ['view'] };
    expect(catalogueInScope(catalogue, 'R1').modules.map((entry) => entry.key)).toEqual(['registration', 'results']);
    expect(catalogueInScope(catalogue, 'R2').modules).toHaveLength(4);

    const split = splitByScope(['claims.view', 'registration.view', 'wallet.manage'], 'R1');
    expect([[...split.shown], split.kept]).toEqual([['registration.view'], ['claims.view', 'wallet.manage']]);
  });
});
