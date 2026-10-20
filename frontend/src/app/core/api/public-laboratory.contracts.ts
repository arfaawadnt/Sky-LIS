/**
 * Public laboratory card (LIS-CR-005), mirrored from the API: what the sign-in page of a laboratory address
 * (`/lab/<slug>`) shows before anyone signs in. Anonymous; an active client laboratory only, `404` for anything else
 * (inactive, the platform, unknown or malformed). Nothing else about a tenant is public.
 */
export interface PublicLaboratory {
  readonly slug: string;
  readonly nameEn: string;
  readonly nameAr: string;
  /** Bumped on every logo change; `null` when the laboratory has no logo. */
  readonly logoVersion: number | null;
}

/** A laboratory address: 3–40 of `a-z 0-9 -` (INV-PLT-001, the tenant's `PortalSlug`). */
export const LABORATORY_SLUG_PATTERN = /^[a-z0-9-]{3,40}$/;

export const PUBLIC_LABORATORY_API = {
  card: (slug: string): string => `/api/v1/public/laboratories/${encodeURIComponent(slug)}`,
  /** The logo bytes (`404` when none); the version only busts the browser cache, the server answers the current logo. */
  logo: (slug: string, version: number): string => `/api/v1/public/laboratories/${encodeURIComponent(slug)}/logo?v=${version}`,
} as const;
