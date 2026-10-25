/**
 * The schedule fields of SA-22 (LIS-CR-013): the browser's `datetime-local` value (local wall time, minutes) ↔ the API's UTC
 * instant. An empty or unreadable value is `null`.
 */

const pad = (value: number): string => String(value).padStart(2, '0');

/** `2026-10-24T09:00:00Z` → `2026-10-24T11:00` in a UTC+2 browser; `null` → ''. */
export function toLocalInput(iso: string | null | undefined): string {
  if (!iso) {
    return '';
  }

  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return '';
  }

  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/** `2026-10-24T11:00` (local) → the ISO UTC instant; '' or malformed → `null`. */
export function fromLocalInput(value: string | null | undefined): string | null {
  if (!value || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/.test(value)) {
    return null;
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

/** Where an announcement stands now: shown, waiting for its start, past its end, or switched off. */
export type AnnouncementState = 'Live' | 'Scheduled' | 'Ended' | 'Inactive';

export function announcementState(row: { readonly isActive: boolean; readonly fromUtc: string; readonly toUtc: string | null }, now: Date = new Date()): AnnouncementState {
  if (!row.isActive) {
    return 'Inactive';
  }

  if (new Date(row.fromUtc).getTime() > now.getTime()) {
    return 'Scheduled';
  }

  return row.toUtc !== null && new Date(row.toUtc).getTime() <= now.getTime() ? 'Ended' : 'Live';
}
