/**
 * The login logs (SA-08b) show and filter in UTC, as the prototype labels them: the laboratories of a platform span time
 * zones, so one zone for every row keeps a sign-in in Cairo and one in Riyadh on the same scale.
 */

/** The value of a `datetime-local` input read as UTC (`2026-10-19T09:30` → `2026-10-19T09:30:00.000Z`); null when empty or invalid. */
export function utcInputToIso(value: string | null | undefined): string | null {
  if (value === null || value === undefined || value.trim() === '') {
    return null;
  }

  const match = /^(\d{4}-\d{2}-\d{2})T(\d{2}:\d{2})(?::\d{2})?$/.exec(value.trim());
  if (!match) {
    return null;
  }

  const date = new Date(`${match[1]}T${match[2]}:00Z`);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

/** `dd/MM/yyyy HH:mm` of an instant in UTC (Latin digits in both languages; assembled by hand so no ICU version adds a comma). */
export function formatUtcDateTime(iso: string | null | undefined): string {
  if (!iso) {
    return '—';
  }

  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return '—';
  }

  const two = (value: number): string => String(value).padStart(2, '0');
  return `${two(date.getUTCDate())}/${two(date.getUTCMonth() + 1)}/${date.getUTCFullYear()} ${two(date.getUTCHours())}:${two(date.getUTCMinutes())}`;
}

/** "1h 05m" / "05m" for a duration in minutes. */
export function formatMinutes(minutes: number): string {
  const hours = Math.floor(minutes / 60);
  const rest = String(minutes % 60).padStart(2, '0');
  return hours > 0 ? `${hours}h ${rest}m` : `${rest}m`;
}
