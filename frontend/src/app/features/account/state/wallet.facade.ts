import { Injectable, inject, resource, signal } from '@angular/core';

import type { PageRequest } from '../../../core/api/paging';
import type { CreateTopUpRequest, WalletTransactionKind } from '../../../core/api/platform.contracts';
import { mutation } from '../../../core/state/mutation';
import { AccountApi } from '../data/account.api';

export interface WalletTransactionFilter {
  readonly kind: WalletTransactionKind | null;
  readonly page: number;
  readonly pageSize: number;
}

/** CP-66 state over the platform module's tenant wallet API: the wallet, its movements (paged, by kind) and the top-up request. */
@Injectable()
export class WalletFacade {
  private readonly api = inject(AccountApi);

  readonly filter = signal<WalletTransactionFilter>({ kind: null, page: 1, pageSize: 20 });

  readonly wallet = resource({ loader: () => this.api.wallet() });

  readonly transactions = resource({
    params: () => this.filter(),
    loader: ({ params }) => this.api.walletTransactions(params.kind, params.page, params.pageSize),
  });

  readonly requestTopUp = mutation((request: CreateTopUpRequest) => this.api.requestTopUp(request), { then: () => this.wallet.reload() });

  setKind(kind: WalletTransactionKind | null): void {
    this.filter.update((current) => ({ ...current, kind, page: 1 }));
  }

  setPage(page: PageRequest): void {
    this.filter.update((current) => ({ ...current, page: page.page, pageSize: page.pageSize }));
  }

  refresh(): void {
    this.wallet.reload();
    this.transactions.reload();
  }
}
