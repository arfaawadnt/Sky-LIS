import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { SubscriptionClientItem, SubscriptionFilter } from '../../../core/api/platform.contracts';
import { SubscriptionClientsApi } from '../data/subscription-clients.api';

/**
 * LIS-CR-012 (C-16): the clients of the SA-04 "Subscription" filter — expiring within a week, in grace, expired. One small
 * list, never paged; the server orders it soonest end first.
 */
@Injectable()
export class SubscriptionClientsFacade {
  private readonly api = inject(SubscriptionClientsApi);

  /** The filter; nothing loads until the owner sets it (the table sets its input before the first read). */
  readonly state = signal<SubscriptionFilter | null>(null);

  readonly list = resource({
    params: () => this.state() ?? undefined,
    loader: ({ params, abortSignal }) => this.api.list(params, null, abortSignal),
  });

  readonly rows = computed<readonly SubscriptionClientItem[]>(() => (this.list.hasValue() ? this.list.value().items : []));

  /** The platform's grace days the list was computed with. */
  readonly graceDays = computed(() => (this.list.hasValue() ? this.list.value().graceDays : null));

  setState(state: SubscriptionFilter): void {
    this.state.set(state);
  }

  refresh(): void {
    this.list.reload();
  }
}
