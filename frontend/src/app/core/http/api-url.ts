import { AUTH_API } from '../api/auth.contracts';
import { pathOf } from '../api/credential-fields';

/** Same-origin API (01 §1): relative `/api/...` URLs, or absolute ones whose path starts with `/api/`. */
export function isApiRequest(url: string): boolean {
  return pathOf(url).startsWith('/api/');
}

/** The authentication surface never triggers a refresh on 401 (01 §6.4). */
export function isAuthEndpoint(url: string): boolean {
  return pathOf(url).startsWith(AUTH_API.base + '/');
}
