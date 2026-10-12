import type { AuthProfile } from '../api/auth.contracts';

/**
 * The role of the session in a language (validation v0.5.0 B-09): the Arabic name in Arabic when the server sent one,
 * else the English name; `null` without a profile or a role. The shell's sidebar and top bar show it.
 */
export function roleNameFor(profile: Pick<AuthProfile, 'roleName' | 'roleNameAr'> | null | undefined, lang: string): string | null {
  if (!profile) {
    return null;
  }

  const arabic = profile.roleNameAr?.trim();
  return lang === 'ar' && arabic ? arabic : profile.roleName;
}
