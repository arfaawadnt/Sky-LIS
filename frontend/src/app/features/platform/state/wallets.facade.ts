import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { PageRequest } from '../../../core/api/paging';
import type { PlanListItem, PlanRequest, TopUpWalletRequest, WalletChargeRunSummary, WalletTransactionItem, WalletTransactionKind } from '../../../core/api/platform.contracts';
import { mutation } from '../../../core/state/mutation';
import { PlatformApi } from '../data/platform.api';

/** SA-05 state: the plans register and the wallet of the selected client (balance, pending requests, transactions). */
@Injectable()
export class WalletsFacade {
  private readonly api = inject(PlatformApi);

  // plans
  readonly planActive = signal<boolean | null>(null);

  readonly plans = resource({
    params: () => this.planActive(),
    loader: ({ params, abortSignal }) => this.api.listPlans(params, { page: 1, pageSize: 200 }, abortSignal),
  });

  readonly planRows = computed<readonly PlanListItem[]>(() => (this.plans.hasValue() ? this.plans.value().items : []));

  readonly savePlan = mutation((input: { id: string | null; request: PlanRequest }): Promise<unknown> => (input.id === null ? this.api.createPlan(input.request) : this.api.updatePlan(input.id, input.request)), {
    then: () => this.plans.reload(),
  });

  readonly setPlanActive = mutation((input: { id: string; active: boolean }) => this.api.setPlanActive(input.id, input.active), { then: () => this.plans.reload() });

  // client wallet
  readonly clientOptions = resource({
    loader: ({ abortSignal }) => this.api.listClients({ search: null, source: null, planId: null, status: null, page: 1, pageSize: 200 }, abortSignal),
  });

  readonly clientId = signal<string | null>(null);

  readonly wallet = resource({
    params: () => this.clientId(),
    loader: ({ params, abortSignal }) => (params === null ? Promise.resolve(null) : this.api.getWallet(params, abortSignal)),
  });

  readonly transactionFilter = signal<{ kind: WalletTransactionKind | null; page: number; pageSize: number }>({ kind: null, page: 1, pageSize: 20 });

  readonly transactions = resource({
    params: () => ({ clientId: this.clientId(), ...this.transactionFilter() }),
    loader: ({ params, abortSignal }) =>
      params.clientId === null ? Promise.resolve(null) : this.api.listWalletTransactions(params.clientId, params.kind, { page: params.page, pageSize: params.pageSize }, abortSignal),
  });

  readonly transactionRows = computed<readonly WalletTransactionItem[]>(() => this.transactions.value()?.items ?? []);

  readonly topUp = mutation((request: TopUpWalletRequest) => this.api.topUpWallet(this.requireClient(), request), { then: () => this.reloadWallet() });

  readonly decline = mutation((requestId: string) => this.api.declineTopUpRequest(this.requireClient(), requestId), { then: () => this.reloadWallet() });

  /** SA-05 "Run charges now" (W2): every due wallet, then the selected wallet and the client list are re-read. */
  readonly runCharges = mutation<void, WalletChargeRunSummary>(() => this.api.runWalletCharges(), { then: () => this.reloadWallet() });

  selectClient(clientId: string | null): void {
    this.clientId.set(clientId === '' ? null : clientId);
    this.transactionFilter.update((current) => ({ ...current, page: 1 }));
  }

  setKind(kind: WalletTransactionKind | null): void {
    this.transactionFilter.update((current) => ({ ...current, kind, page: 1 }));
  }

  setTransactionPage(page: PageRequest): void {
    this.transactionFilter.update((current) => ({ ...current, page: page.page, pageSize: page.pageSize }));
  }

  /** Every transaction of the selected wallet with the current kind, for the CSV export. */
  exportTransactions(): Promise<readonly WalletTransactionItem[]> {
    const clientId = this.requireClient();
    const kind = this.transactionFilter().kind;
    return this.api.all((page) => this.api.listWalletTransactions(clientId, kind, page));
  }

  /** Re-reads the plans, the client list and the selected wallet when a page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14). A load in flight is left alone. */
  refresh(): void {
    this.plans.reload();
    this.clientOptions.reload();
    this.wallet.reload();
    this.transactions.reload();
  }

  private reloadWallet(): void {
    this.wallet.reload();
    this.transactions.reload();
    this.clientOptions.reload();
  }

  private requireClient(): string {
    const clientId = this.clientId();
    if (clientId === null) {
      throw new Error('No client is selected.');
    }

    return clientId;
  }
}
