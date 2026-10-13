import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { CollectionsReportDocument, CollectionsReportRequest, PaymentCreated, PaymentRegisterFilter, VisitAccountView } from '../../../core/api/billing.contracts';
import type { PageRequest } from '../../../core/api/paging';
import { mutation } from '../../../core/state/mutation';
import { BillingApi, type DownloadedFile } from '../data/billing.api';
import type { MovementInput } from '../ui/movement-form.component';
import { CashDeskFacade } from './cash-desk.facade';

/** How a page asks for a visit account: by accession (the search box) or by id (a link from Registration, BR-26). */
export interface VisitLookup {
  readonly by: 'accession' | 'visit';
  readonly value: string;
}

export const DEFAULT_PAYMENT_FILTER: PaymentRegisterFilter = { accession: null, fromUtc: null, toUtc: null, treasuryId: null, method: null, status: null, page: 1, pageSize: 50 };

/** CP-23 Payments (01 §6.3): the account of one visit, the pay and void mutations, and the payments register. Provided at the route. */
@Injectable()
export class PaymentsFacade {
  private readonly api = inject(BillingApi);
  private readonly desk = inject(CashDeskFacade);

  readonly lookup = signal<VisitLookup | null>(null);

  readonly account = resource({
    params: () => this.lookup() ?? undefined,
    loader: ({ params, abortSignal }) => (params.by === 'visit' ? this.api.accountByVisit(params.value, abortSignal) : this.api.accountByAccession(params.value, abortSignal)),
  });

  readonly current = computed<VisitAccountView | null>(() => (this.account.hasValue() ? this.account.value() : null));

  readonly pay = mutation(
    (input: MovementInput & { readonly visitId: string }): Promise<PaymentCreated> =>
      this.api.recordPayment({ visitId: input.visitId, amount: input.amount, method: input.method, reference: input.reference, treasuryId: input.treasuryId, deviceId: this.desk.deviceId() }),
    { then: () => this.refresh() },
  );

  readonly voidPayment = mutation((input: { readonly id: string; readonly reason: string }) => this.api.voidPayment(input.id, input.reason), { then: () => this.refresh() });

  /** The register loads only while its tab is open. */
  readonly registerOpen = signal(false);

  readonly filter = signal<PaymentRegisterFilter>(DEFAULT_PAYMENT_FILTER);

  readonly register = resource({
    params: () => (this.registerOpen() ? this.filter() : undefined),
    loader: ({ params, abortSignal }) => this.api.listPayments(params, abortSignal),
  });

  readonly branchTreasuries = resource({
    params: () => (this.registerOpen() ? true : undefined),
    loader: ({ abortSignal }) => this.api.branchTreasuries(abortSignal),
  });

  readonly exportCsv = mutation<void, DownloadedFile>(() => this.api.exportPayments(this.filter()));

  // ---- collections (treasury) report, change request 2026-10-13 item 2 ------------------------------------------

  /** The report dialog is open: its rights are read when it opens (a role change since the last visit shows at once). */
  readonly collectionsOpen = signal(false);

  readonly reportPrivileges = resource({
    params: () => (this.collectionsOpen() ? true : undefined),
    loader: ({ abortSignal }) => this.api.collectionsReportPrivileges(abortSignal),
  });

  /** The report last loaded in the dialog (shown in its table, printed on demand); cleared when the dialog closes. */
  readonly collectionsDocument = signal<CollectionsReportDocument | null>(null);

  readonly collectionsReport = mutation((request: CollectionsReportRequest) => this.api.collectionsReport(request), { then: (doc) => this.collectionsDocument.set(doc) });

  openCollections(): void {
    this.collectionsReport.reset();
    this.collectionsDocument.set(null);
    this.collectionsOpen.set(true);
  }

  closeCollections(): void {
    this.collectionsOpen.set(false);
    this.collectionsDocument.set(null);
    this.collectionsReport.reset();
  }

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
    this.pay.reset();
    this.voidPayment.reset();
  }

  setFilter(change: Partial<Omit<PaymentRegisterFilter, 'page' | 'pageSize'>>): void {
    this.filter.update((current) => ({ ...current, ...change, page: 1 }));
  }

  setPage(page: PageRequest): void {
    this.filter.update((current) => ({ ...current, page: page.page, pageSize: page.pageSize }));
  }

  /** Re-reads the visit account and the open register (after a write, and when the page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14, A-01). A load in flight is left alone; an idle read (nothing looked up) stays idle.) */
  refresh(): void {
    this.account.reload();
    this.register.reload();
    this.branchTreasuries.reload();
  }
}
