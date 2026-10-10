import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { RefundCreated, RefundRegisterFilter, VisitAccountView } from '../../../core/api/billing.contracts';
import type { PageRequest } from '../../../core/api/paging';
import { AuthStore } from '../../../core/auth/auth.store';
import { mutation } from '../../../core/state/mutation';
import { BillingApi, type DownloadedFile } from '../data/billing.api';
import type { MovementInput } from '../ui/movement-form.component';
import { CashDeskFacade, reloadWhenSettled } from './cash-desk.facade';
import type { VisitLookup } from './payments.facade';

export const DEFAULT_REFUND_FILTER: RefundRegisterFilter = { accession: null, fromUtc: null, toUtc: null, treasuryId: null, method: null, page: 1, pageSize: 50 };

/** CP-24 Refunds: a visit with a negative balance, the refund mutation (BR-14), the catalogue reasons and the refunds register. */
@Injectable()
export class RefundsFacade {
  private readonly api = inject(BillingApi);
  private readonly desk = inject(CashDeskFacade);
  private readonly auth = inject(AuthStore);

  readonly lookup = signal<VisitLookup | null>(null);

  readonly account = resource({
    params: () => this.lookup() ?? undefined,
    loader: ({ params, abortSignal }) => (params.by === 'visit' ? this.api.accountByVisit(params.value, abortSignal) : this.api.accountByAccession(params.value, abortSignal)),
  });

  readonly current = computed<VisitAccountView | null>(() => (this.account.hasValue() ? this.account.value() : null));

  /** Keyed by the signed-in user: another cashier on the same browser never inherits a list (or a refusal) read for someone else. */
  readonly reasons = resource({
    params: () => ({ userId: this.auth.currentUser()?.id ?? '' }),
    loader: ({ abortSignal }) => this.api.refundReasons(abortSignal),
  });

  /** Called when the Refunds page opens: a reason list that settled on an earlier visit (UAT D-08) is read again. */
  refreshReasons(): void {
    reloadWhenSettled(this.reasons);
  }

  readonly refund = mutation(
    (input: MovementInput & { readonly visitId: string; readonly reasonId: string }): Promise<RefundCreated> =>
      this.api.recordRefund({
        visitId: input.visitId,
        amount: input.amount,
        method: input.method,
        reference: input.reference,
        reasonId: input.reasonId,
        note: input.note,
        treasuryId: input.treasuryId,
        deviceId: this.desk.deviceId(),
      }),
    { then: () => this.refresh() },
  );

  readonly filter = signal<RefundRegisterFilter>(DEFAULT_REFUND_FILTER);

  readonly register = resource({
    params: () => this.filter(),
    loader: ({ params, abortSignal }) => this.api.listRefunds(params, abortSignal),
  });

  readonly exportCsv = mutation<void, DownloadedFile>(() => this.api.exportRefunds(this.filter()));

  find(accession: string): void {
    const value = accession.trim();
    if (value !== '') {
      this.lookup.set({ by: 'accession', value });
    }
  }

  open(visitId: string): void {
    this.lookup.set({ by: 'visit', value: visitId });
  }

  clear(): void {
    this.lookup.set(null);
    this.refund.reset();
  }

  setFilter(change: Partial<Omit<RefundRegisterFilter, 'page' | 'pageSize'>>): void {
    this.filter.update((current) => ({ ...current, ...change, page: 1 }));
  }

  setPage(page: PageRequest): void {
    this.filter.update((current) => ({ ...current, page: page.page, pageSize: page.pageSize }));
  }

  private refresh(): void {
    if (this.lookup() !== null) {
      this.account.reload();
    }

    this.register.reload();
  }
}
