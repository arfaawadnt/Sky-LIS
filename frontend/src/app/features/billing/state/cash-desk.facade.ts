import { Injectable, computed, inject, linkedSignal, resource } from '@angular/core';

import type { BillingTreasuryItem } from '../../../core/api/billing.contracts';
import { DeviceIdentityService } from '../../../core/device/device-identity.service';
import { BillingApi } from '../data/billing.api';

/**
 * The cash desk of this browser (BR-27), shared by the pages of the module (provided at the billing route): the device
 * id, the treasuries of the session branch approved for the user on this device, the treasury in use, and whether a
 * receipt prints after a payment (`autoPrintReceipt`, CP-57).
 */
@Injectable()
export class CashDeskFacade {
  private readonly api = inject(BillingApi);
  private readonly device = inject(DeviceIdentityService);

  readonly deviceId = computed(() => this.device.deviceId());

  readonly treasuries = resource({
    params: () => ({ deviceId: this.deviceId() }),
    loader: ({ params, abortSignal }) => this.api.myTreasuries(params.deviceId, abortSignal),
  });

  readonly autoPrintReceipt = resource({
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

  shouldAutoPrint(): boolean {
    return this.autoPrintReceipt.hasValue() ? this.autoPrintReceipt.value() : true;
  }
}
