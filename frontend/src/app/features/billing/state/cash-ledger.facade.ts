import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type {
  CashClosingCreated,
  CashClosingPreview,
  CashEntryCreated,
  CashLedgerRequest,
  CashLedgerView,
  CloseCashDrawerRequest,
  CollectionsReportPrivileges,
  DrawerScopeFilter,
  PostCashEntryRequest,
} from '../../../core/api/billing.contracts';
import { mutation } from '../../../core/state/mutation';
import { BillingApi } from '../data/billing.api';
import { reloadWhenSettled } from './cash-desk.facade';

/** Today's own drawer: the ledger the page opens on. */
export const DEFAULT_LEDGER_REQUEST: CashLedgerRequest = { fromDay: null, toDay: null, scope: 'User', cashierUserId: null };

/**
 * CP-70 Cash ledger (change request LIS-CR-011, point C-15): the day book of the session branch's drawer — own drawer, one
 * cashier or every cashier of the branch — with its running balance; cash entries in and out and their void; the day close
 * from the ledger's close window, through the closing of CP-30 (preview, then close). The branch and the cashier of a
 * movement are the session's; nothing here chooses them.
 */
@Injectable()
export class CashLedgerFacade {
  private readonly api = inject(BillingApi);

  readonly request = signal<CashLedgerRequest>(DEFAULT_LEDGER_REQUEST);

  readonly ledger = resource({
    params: () => this.request(),
    loader: ({ params, abortSignal }) => this.api.cashLedger(params, abortSignal),
  });

  readonly current = computed<CashLedgerView | null>(() => (this.ledger.hasValue() ? this.ledger.value() : null));

  /** The CP-37 rights: the branch scope needs `canViewBranchCollections`, and `reportDaysLimit` bounds the period. */
  readonly privileges = resource({
    loader: ({ abortSignal }) => this.api.collectionsReportPrivileges(abortSignal),
  });

  readonly currentPrivileges = computed<CollectionsReportPrivileges | null>(() => (this.privileges.hasValue() ? this.privileges.value() : null));

  readonly postEntry = mutation((request: PostCashEntryRequest): Promise<CashEntryCreated> => this.api.postCashEntry(request), {
    then: () => this.ledger.reload(),
  });

  readonly voidEntry = mutation((input: { readonly id: string; readonly reason: string }): Promise<void> => this.api.voidCashEntry(input.id, input.reason), {
    then: () => this.ledger.reload(),
  });

  /** Set when the day close dialog opens: the drawer of the ledger's scope over its close window. */
  readonly closeScope = signal<DrawerScopeFilter | null>(null);

  readonly closePreview = resource({
    params: () => this.closeScope() ?? undefined,
    loader: ({ params, abortSignal }) => this.api.previewClosing(params, abortSignal),
  });

  readonly currentClosePreview = computed<CashClosingPreview | null>(() => (this.closePreview.hasValue() ? this.closePreview.value() : null));

  readonly close = mutation((request: CloseCashDrawerRequest): Promise<CashClosingCreated> => this.api.closeDrawer(request), {
    then: () => {
      this.closeScope.set(null);
      this.ledger.reload();
    },
  });

  /** Re-reads the ledger and the rights when the page opens again (the facade outlives the page: UAT D-14, A-01). */
  refresh(): void {
    reloadWhenSettled(this.ledger);
    reloadWhenSettled(this.privileges);
  }

  /** A new period, scope or cashier; a cashier choice only makes sense in the branch scope. */
  setRequest(change: Partial<CashLedgerRequest>): void {
    this.request.update((current) => {
      const next = { ...current, ...change };
      return next.scope === 'User' ? { ...next, cashierUserId: null } : next;
    });
  }

  /** Opens the day close of the current ledger: its scope's drawer (own: the ledger's cashier; branch: the chosen cashier or all) over the close window. */
  openClose(view: CashLedgerView): boolean {
    const window = view.closeWindow;
    if (window === null) {
      return false;
    }

    this.close.reset();
    this.closeScope.set({ cashierUserId: view.cashierUserId, fromUtc: window.fromUtc, toUtc: window.toUtc });
    return true;
  }

  cancelClose(): void {
    this.closeScope.set(null);
    this.close.reset();
  }
}
