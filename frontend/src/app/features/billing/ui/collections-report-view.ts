import type { CollectionsReportDocument, CollectionsReportGrouping, CollectionsReportRow } from '../../../core/api/billing.contracts';
import type { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';

// Presentation helpers of the collections report shared by the dialog table and the printed document (change request
// 2026-10-13, item 2).

/** The caption of a row: the method translated here, a branch in the reader's language, a cashier by display name; an unnamed key falls back to the key. */
export function collectionsRowLabel(i18n: I18nService, row: CollectionsReportRow, groupBy: CollectionsReportGrouping): string {
  if (groupBy === 'Method') {
    return i18n.t(`billing.method.${row.key}`);
  }

  const picked = localizedPick({ en: row.labelEn ?? '', ar: row.labelAr ?? '' }, i18n.lang());
  return picked === '' ? row.key : picked;
}

/** "Own payments · Cashier One · 2026-10-13" (the scope, the cashier in user scope, the period). */
export function collectionsSubtitle(i18n: I18nService, doc: Pick<CollectionsReportDocument, 'scope' | 'cashierName' | 'fromDay' | 'toDay'>): string {
  const period = doc.fromDay === doc.toDay ? doc.fromDay : `${doc.fromDay} – ${doc.toDay}`;
  return [i18n.t(`billing.collections.scope${doc.scope}`), doc.cashierName, period].filter((part): part is string => part !== null && part !== '').join(' · ');
}

/** A `yyyy-MM-dd` value of a local date (the dialog's default period: today in the browser; the server reads its own laboratory day). */
export function dayInputValue(date: Date): string {
  const pad = (value: number): string => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** The earliest day the member may report (`reportDaysLimit` days before today), as a `yyyy-MM-dd` value; null = no limit. */
export function earliestReportDay(reportDaysLimit: number | null, today: Date = new Date()): string | null {
  if (reportDaysLimit === null) {
    return null;
  }

  const earliest = new Date(today.getFullYear(), today.getMonth(), today.getDate() - reportDaysLimit);
  return dayInputValue(earliest);
}
