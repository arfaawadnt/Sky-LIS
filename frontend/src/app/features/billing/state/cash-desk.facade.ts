import { Injectable, computed, inject, linkedSignal, resource, type ResourceRef } from '@angular/core';

import type { BillingTreasuryItem } from '../../../core/api/billing.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { DeviceIdentityService } from '../../../core/device/device-identity.service';
import { BillingApi } from '../data/billing.api';

/**
 * Re-reads a resource that already settled (resolved or failed) and leaves a load in flight alone. The billing facades
 * live as long as the module's route, so a page that opens again must not keep the answer of an earlier visit
 * (UAT D-08: an approval granted since the last read, or a 403 of a role that has changed since, stayed on screen).
 */
export function reloadWhenSettled<T>(target: ResourceRef<T>): void {
  const status = target.status();
  if (status === 'resolved' || status === 'error') {
    target.reload();
  }
}

/**
 * The cash desk of this browser (BR-27), shared by the pages of the module (provided at the billing route): the device
 * id, the treasuries of the session branch approved for the user on this device, the treasury in use, and whether a
 * receipt prints after a payment or a refund (`autoPrintReceipt`, CP-57, read through billing: UAT A-05). The approved
 * list is keyed by the signed-in user and the session branch, so a sign-in as someone else or a branch change on the
 * same browser never shows the previous list.
 */
@Injectable()
export class CashDeskFacade {
  private readonly api = inject(BillingApi);
  private readonly device = inject(DeviceIdentityService);
  private readonly auth = inject(AuthStore);

  readonly deviceId = computed(() => this.device.deviceId());

  readonly treasuries = resource({
    params: () => ({ deviceId: this.deviceId(), userId: this.auth.currentUser()?.id ?? '', branchId: this.auth.currentBranch() ?? '' }),
    loader: ({ params, abortSignal }) => this.api.myTreasuries(params.deviceId, abortSignal),
  });

  readonly autoPrintReceipt = resource({
    params: () => ({ userId: this.auth.currentUser()?.id ?? '' }),
    loader: ({ abortSignal }) => this.api.autoPrintReceipt(abortSignal),
  });

  readonly approved = computed<readonly BillingTreasuryItem[]>(() => (this.treasuries.hasValue() ? this.treasuries.value() : []));

  /** No approved treasury on this device: the pages point the cashier to the treasury-access request (CP-29). */
  readonly hasStation = computed(() => this.approved().length > 0);

  /** The treasury in use: kept while it stays approved, otherwise the first approved one. */
  readonly treasuryId = linkedSignal<readonly BillingTreasuryItem[], string>({
    source: this.approved,
    computation: (items, previous) => (previous !== undefined && items.some((item) => item.id === previous.value) ? previous.value : (items[0]?.id ?? '')),
  });

  /**
   * Called when a page of the module opens: an approval granted since the last read (CP-29) and a print parameter changed
   * since show without a manual Refresh.
   */
  refresh(): void {
    reloadWhenSettled(this.treasuries);
    reloadWhenSettled(this.autoPrintReceipt);
  }

  /** Prints only when the laboratory asked for it; an unknown answer (still loading, refused, failed) does not print (UAT A-05). */
  shouldAutoPrint(): boolean {
    return this.autoPrintReceipt.hasValue() && this.autoPrintReceipt.value();
  }
}
