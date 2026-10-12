import type { AuthProfile } from '../api/auth.contracts';

/**
 * The user's own name in a language (validation v0.5.0 R-12): the member's Arabic full name in Arabic, the English one
 * in English, when the server sent them; else `displayName` (the name in the preferred language, or the account's name
 * outside a laboratory). `''` without a profile. The shell's top bar shows it.
 */
export function displayNameFor(profile: Pick<AuthProfile, 'displayName' | 'displayNameEn' | 'displayNameAr'> | null | undefined, lang: string): string {
  if (!profile) {
    return '';
  }

  const localized = (lang === 'ar' ? profile.displayNameAr : profile.displayNameEn)?.trim();
  return localized || profile.displayName;
}
