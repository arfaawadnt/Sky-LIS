import type { ProblemDetails } from '../../core/api/problem-details';
import type { I18nService } from '../../core/i18n/i18n.service';

/**
 * The message for a problem code, looked up in this order: `<prefix>.error.<CODE>` (the module's own text), then
 * `shared.error.<CODE>` (platform-wide codes), then `shared.error.generic`. Modules translate their own codes in
 * their dictionary (MODULE-PATTERN §4) and never show the server's English title.
 */
export function problemMessage(i18n: I18nService, prefix: string | null, problem: ProblemDetails | { code: string } | null): string {
  if (problem === null) {
    return '';
  }

  const candidates = [prefix ? `${prefix}.error.${problem.code}` : null, `shared.error.${problem.code}`].filter((key): key is string => key !== null);
  for (const key of candidates) {
    if (i18n.has(key)) {
      return i18n.t(key);
    }
  }

  return i18n.t('shared.error.generic');
}
