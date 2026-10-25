import type { PermissionCatalogue } from '../../../core/api/organization.contracts';
import { type Release, RELEASE_SCOPE, inReleaseScope } from '../../../core/release';

/**
 * The release of each permission module of the server catalogue (`PermissionCatalog`) whose screens are all of a later
 * release, in both portals (the navigation registry lists every one of their pages as Release 2): payer claims, analysers
 * and the integration library, auto-verification, quality control and support tickets. Every other module (the wallet since
 * LIS-CR-012 brought CP-66 to Release 1) is
 * Release 1. Validation v0.5.0 O-7: the role editor offers only the modules of the delivered release; the server stays
 * permissive and a role keeps the keys of hidden modules it already holds.
 */
export const PERMISSION_MODULE_RELEASE: Readonly<Record<string, Release>> = {
  claims: 'R2',
  integration: 'R2',
  automation: 'R2',
  quality: 'R2',
  support: 'R2',
};

/** The release of a permission key (`module.action`) or of a module key. */
export function permissionRelease(keyOrModule: string): Release {
  return PERMISSION_MODULE_RELEASE[keyOrModule.split('.')[0] ?? ''] ?? 'R1';
}

/** True when the role editor offers the module or key in the delivered release. */
export function permissionInScope(keyOrModule: string, scope: Release = RELEASE_SCOPE): boolean {
  return inReleaseScope(permissionRelease(keyOrModule), scope);
}

/** The catalogue without the modules of a later release. */
export function catalogueInScope(catalogue: PermissionCatalogue, scope: Release = RELEASE_SCOPE): PermissionCatalogue {
  return { ...catalogue, modules: catalogue.modules.filter((module) => permissionInScope(module.key, scope)) };
}

/** The keys a role holds split into those the matrix shows and those of hidden modules, which a save keeps as they are. */
export function splitByScope(keys: Iterable<string>, scope: Release = RELEASE_SCOPE): { readonly shown: ReadonlySet<string>; readonly kept: readonly string[] } {
  const shown = new Set<string>();
  const kept: string[] = [];
  for (const key of keys) {
    if (permissionInScope(key, scope)) {
      shown.add(key);
    } else {
      kept.push(key);
    }
  }

  return { shown, kept };
}
