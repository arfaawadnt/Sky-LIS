import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { ServicePricesView } from '../../../core/api/pricing.contracts';
import { mutation } from '../../../core/state/mutation';
import { PricingApi } from '../data/pricing.api';

/**
 * Service prices (CP-33b, CR 2026-10-19 C22): one catalogue service with its line in every base list and its price in
 * every contract tier, and the inline edit of a base price (one-line upsert, then re-read). Provided at the route.
 */
@Injectable()
export class ServicePricesFacade {
  private readonly api = inject(PricingApi);

  readonly serviceId = signal<string | null>(null);

  readonly view = resource({
    params: () => this.serviceId() ?? undefined,
    loader: ({ params, abortSignal }) => this.api.getServicePrices(params, abortSignal),
  });

  readonly current = computed<ServicePricesView | null>(() => (this.view.hasValue() ? this.view.value() : null));

  /** One base line changed from the page (CP-33 L3 through this page); every other list is untouched. */
  readonly setBasePrice = mutation(
    (input: { listId: string; serviceId: string; price: number; isActive: boolean }) =>
      this.api.upsertPriceLines(input.listId, [{ serviceId: input.serviceId, price: input.price, isActive: input.isActive }]),
    { then: () => this.view.reload() },
  );

  /** Opens a service; the same service is re-read, because the facade outlives the page (route provider). */
  load(serviceId: string | null): void {
    this.setBasePrice.reset();
    if (serviceId === null) {
      this.serviceId.set(null);
    } else if (this.serviceId() === serviceId) {
      this.view.reload();
    } else {
      this.serviceId.set(serviceId);
    }
  }

  reload(): void {
    this.view.reload();
  }
}
