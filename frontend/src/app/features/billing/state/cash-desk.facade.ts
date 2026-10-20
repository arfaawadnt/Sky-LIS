import { Injectable, computed, inject, resource, type ResourceRef } from '@angular/core';

import type { PaymentMethodOption } from '../../../core/api/billing.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { displayNameFor } from '../../../core/auth/display-name';
import { I18nService } from '../../../core/i18n/i18n.service';
import { BillingApi } from '../data/billing.api';

/**
 * Re-reads a resource that already settled (resolved or failed) and leaves a load in flight alone. The billing facades
 * live as long as the module's route, so a page that opens again must not keep the answer of an earlier visit
 * (UAT D-08: a 403 of a role that has changed since, or a parameter changed since, stayed on screen).
 */
export function reloadWhenSettled<T>(target: ResourceRef<T>): void {
  const status = target.status();
  if (status === 'resolved' || status === 'error') {
    target.reload();
  }
}

/**
 * The cash desk shared by the pages of the module (provided at the billing route): whether a receipt prints after a
 * payment or a refund (`autoPrintReceipt`, CP-57, read through billing: UAT A-05) and the laboratory's payment methods.
 * A movement is posted by the signed-in cashier on the session branch (LIS-CR-004: no treasury, no device); the server
 * takes both from the session, so nothing here chooses them.
 */
@Injectable()
export class CashDeskFacade {
  private readonly api = inject(BillingApi);
  private readonly auth = inject(AuthStore);
  private readonly i18n = inject(I18nService);

  /** The signed-in cashier, in the page language: the money forms say who the movement is posted for. */
  readonly cashierName = computed(() => displayNameFor(this.auth.profile(), this.i18n.lang()));

  /** "Cashier: …" under the title of a money form; null without a profile. */
  readonly cashierLine = computed<string | null>(() => {
    const name = this.cashierName();
    return name === '' ? null : this.i18n.tf('billing.pay.cashier', { name });
  });

  readonly autoPrintReceipt = resource({
    params: () => ({ userId: this.auth.currentUser()?.id ?? '' }),
    loader: ({ abortSignal }) => this.api.autoPrintReceipt(abortSignal),
  });

  /**
   * The laboratory's active payment methods in pick-list order (CP-30b, change request 2026-10-19): the movement form
   * offers them and asks for a reference only when the chosen one requires it. Keyed by the signed-in user so a sign-in as
   * someone else re-reads them. An empty answer (no definitions yet, or a refused read) leaves the form on the four kinds.
   */
  readonly methods = resource({
    params: () => ({ userId: this.auth.currentUser()?.id ?? '' }),
    loader: ({ abortSignal }) => this.api.paymentMethodOptions(abortSignal),
  });

  readonly methodOptions = computed<readonly PaymentMethodOption[]>(() => (this.methods.hasValue() ? this.methods.value() : []));

  /** Called when a page of the module opens: a print parameter or a payment method changed since shows without a manual Refresh. */
  refresh(): void {
    reloadWhenSettled(this.autoPrintReceipt);
    reloadWhenSettled(this.methods);
  }

  /** Prints only when the laboratory asked for it; an unknown answer (still loading, refused, failed) does not print (UAT A-05). */
  shouldAutoPrint(): boolean {
    return this.autoPrintReceipt.hasValue() && this.autoPrintReceipt.value();
  }
}
