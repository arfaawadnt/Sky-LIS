// @vitest-environment node
import { NAV_REGISTRY, RELEASE_SCOPE, inReleaseScope } from './nav.registry';

describe('release scope (R1 prototype builds)', () => {
  it('delivers release 1 and shows only its entries', () => {
    expect(RELEASE_SCOPE).toBe('R1');
    expect(inReleaseScope('R1')).toBe(true);
    expect(inReleaseScope('R2')).toBe(false);
    expect(inReleaseScope('R2', 'R2')).toBe(true);
  });

  it('keeps the release 1 client menu of the prototype and hides the deferred modules', () => {
    const r1Client = NAV_REGISTRY.filter((entry) => entry.portal === 'client' && inReleaseScope(entry.release)).map((entry) => entry.path);
    for (const path of ['/account/dashboard', '/patients', '/billing/payments', '/pricing/payers', '/pricing/base-price-lists', '/organization/roles', '/organization/users', '/organization/branches', '/catalogue/services', '/catalogue/tests', '/account/profile']) {
      expect(r1Client).toContain(path);
    }

    for (const hidden of ['/integration/analysers', '/samples/collection', '/billing/cash-closing', '/pricing/contract-pricing', '/account/audit']) {
      expect(r1Client).not.toContain(hidden);
    }
  });

  it('keeps the admin portal without the integration library', () => {
    const r1Admin = NAV_REGISTRY.filter((entry) => entry.portal === 'admin' && inReleaseScope(entry.release));
    expect(r1Admin.some((entry) => entry.group === 'shell.group.integrationLibrary')).toBe(false);
    expect(r1Admin.map((entry) => entry.path)).toContain('/admin/clients');
  });
});
