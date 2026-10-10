import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { BatchAccountView, BatchPaymentResult, PaymentCreated } from '../../../core/api/billing.contracts';
import { mutation } from '../../../core/state/mutation';
import { BillingApi } from '../data/billing.api';
import type { MovementInput } from '../ui/movement-form.component';
import { CashDeskFacade } from './cash-desk.facade';

/** CP-27 Batch payment: the account of a batch by number, pay one member (T1) or pay all (T4). */
@Injectable()
export class BatchPaymentFacade {
  private readonly api = inject(BillingApi);
  private readonly desk = inject(CashDeskFacade);

  readonly number = signal<string | null>(null);

  readonly batch = resource({
    params: () => this.number() ?? undefined,
    loader: ({ params, abortSignal }) => this.api.batchByNumber(params, abortSignal),
  });

  readonly current = computed<BatchAccountView | null>(() => (this.batch.hasValue() ? this.batch.value() : null));

  readonly payMember = mutation(
    (input: MovementInput & { readonly visitId: string }): Promise<PaymentCreated> =>
      this.api.recordPayment({ visitId: input.visitId, amount: input.amount, method: input.method, reference: input.reference, treasuryId: input.treasuryId, deviceId: this.desk.deviceId() }),
    { then: () => this.batch.reload() },
  );

  readonly payAll = mutation(
    (input: MovementInput & { readonly batchId: string }): Promise<BatchPaymentResult> =>
      this.api.payBatch(input.batchId, { amount: input.amount, method: input.method, reference: input.reference, treasuryId: input.treasuryId, deviceId: this.desk.deviceId() }),
    { then: () => this.batch.reload() },
  );

  find(number: string): void {
    const value = number.trim();
    if (value !== '') {
      this.number.set(value);
    }
  }

  clear(): void {
    this.number.set(null);
    this.payMember.reset();
    this.payAll.reset();
  }
}
