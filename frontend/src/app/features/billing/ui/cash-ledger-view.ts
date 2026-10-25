import type { CashLedgerRow, CashLedgerRowKind, CashLedgerView } from '../../../core/api/billing.contracts';
import type { I18nService } from '../../../core/i18n/i18n.service';
import type { BadgeTone } from '../../../shared/ui/badge/status-tones';
import { formatMoney } from './billing-format';

// Presentation helpers of the cash ledger shared by the page, the printed "Daily cash sheet" and the Excel export (change
// request LIS-CR-011, C-15).

/** "Own drawer · Cashier One", "Whole branch · all cashiers" or "Whole branch · Cashier Two". */
export function ledgerScopeLabel(i18n: I18nService, view: Pick<CashLedgerView, 'scope' | 'cashierName' | 'cashierUserId'>): string {
  if (view.scope === 'User') {
    return [i18n.t('billing.ledger.scopeUser'), view.cashierName].filter((part): part is string => part !== null && part !== '').join(' · ');
  }

  const who = view.cashierUserId === null ? i18n.t('billing.closing.allCashiers') : (view.cashierName ?? '');
  return [i18n.t('billing.ledger.scopeBranch'), who].filter((part) => part !== '').join(' · ');
}

/**
 * What a line is about: a receipt or refund names its accession and method; a cash entry its category and reason; a closing its
 * expected and counted cash (and its note). Voided lines carry their reason in the text the server sends.
 */
export function ledgerDetail(i18n: I18nService, row: CashLedgerRow): string {
  const lang = i18n.lang();
  switch (row.kind) {
    case 'Receipt':
    case 'Refund':
      return [row.accession, methodOf(i18n, row), row.text].filter((part): part is string => part !== null && part !== '').join(' · ');
    case 'CashIn':
    case 'CashOut':
      return [row.category === null ? null : i18n.t(`billing.ledger.category.${row.category}`), row.text, row.method === 'Cash' ? null : methodOf(i18n, row)]
        .filter((part): part is string => part !== null && part !== '')
        .join(' · ');
    case 'Closing':
      return [
        i18n.tf('billing.ledger.closingDetail', { expected: formatMoney(row.expected, lang), counted: formatMoney(row.counted, lang) }),
        row.text,
      ].filter((part): part is string => part !== null && part !== '').join(' · ');
  }
}

function methodOf(i18n: I18nService, row: CashLedgerRow): string | null {
  if (row.method === null) {
    return null;
  }

  const kind = i18n.t(`billing.method.${row.method}`);
  return row.methodCode === null || row.methodCode === '' || row.methodCode.toUpperCase() === row.method.toUpperCase() ? kind : `${kind} · ${row.methodCode}`;
}

/** Badge tone of a line's kind: money in green, out amber, a closing blue; a voided line is neutral. */
export function ledgerTone(row: Pick<CashLedgerRow, 'kind' | 'status'>): BadgeTone {
  if (row.status === 'Voided') {
    return 'neutral';
  }

  const tones: Readonly<Record<CashLedgerRowKind, BadgeTone>> = { Receipt: 'ok', CashIn: 'ok', Refund: 'warn', CashOut: 'warn', Closing: 'info' };
  return tones[row.kind];
}

/** A posted cash entry can be voided (billing.approve) unless a closing covers it; the server decides that (BIL-061). */
export function canVoidLine(row: Pick<CashLedgerRow, 'kind' | 'status'>): boolean {
  return (row.kind === 'CashIn' || row.kind === 'CashOut') && row.status === 'Posted';
}
