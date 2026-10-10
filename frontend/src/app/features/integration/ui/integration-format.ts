import type { I18nService } from '../../../core/i18n/i18n.service';
import type { CredentialStatus, IntakeStatus, LinkStatus, SiteAgentStatus } from '../../../core/api/integration.contracts';
import type { BadgeTone } from '../../../shared/ui/badge/status-tones';

/** Tones of the module's statuses (the shared map has no Online/Connected; the badge takes an explicit tone instead). */
export function integrationTone(status: SiteAgentStatus | LinkStatus | IntakeStatus | CredentialStatus | 'Active' | 'Inactive' | null): BadgeTone {
  switch (status) {
    case 'Online':
    case 'Connected':
    case 'Accepted':
    case 'Live':
    case 'Active':
      return 'ok';
    case 'Error':
    case 'Refused':
    case 'Revoked':
      return 'bad';
    case 'Disconnected':
    case 'AutoSendOff':
    case 'Unmapped':
    case 'UnknownSample':
    case 'Expired':
      return 'warn';
    default:
      return 'neutral';
  }
}

/** "just now", "4 min ago", "3 h ago", "2 d ago" — or "never" for a missing instant. */
export function ageText(i18n: I18nService, isoInstant: string | null, nowMs: number): string {
  if (isoInstant === null) {
    return i18n.t('integration.age.never');
  }

  const seconds = Math.max(0, Math.round((nowMs - Date.parse(isoInstant)) / 1000));
  if (seconds < 60) {
    return i18n.t('integration.age.now');
  }

  const minutes = Math.round(seconds / 60);
  if (minutes < 60) {
    return i18n.tf('integration.age.minutes', { n: minutes });
  }

  const hours = Math.round(minutes / 60);
  return hours < 48 ? i18n.tf('integration.age.hours', { n: hours }) : i18n.tf('integration.age.days', { n: Math.round(hours / 24) });
}

/** A local date-time for tables (the user's locale, numerals as the language shows them). */
export function dateTimeText(i18n: I18nService, isoInstant: string | null): string {
  if (isoInstant === null) {
    return '—';
  }

  return new Date(isoInstant).toLocaleString(i18n.lang() === 'ar' ? 'ar-EG' : 'en-GB', { dateStyle: 'short', timeStyle: 'medium' });
}

/** `yyyy-MM-ddTHH:mm` of a datetime-local input → ISO instant, or null. */
export function localInputToIso(value: string): string | null {
  if (value.trim() === '') {
    return null;
  }

  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? null : parsed.toISOString();
}

/** A number typed in a text box, or null when blank or not a number. */
export function parseNumber(value: string | number | null): number | null {
  if (value === null) {
    return null;
  }

  if (typeof value === 'number') {
    return Number.isFinite(value) ? value : null;
  }

  const trimmed = value.trim();
  if (trimmed === '') {
    return null;
  }

  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
}
