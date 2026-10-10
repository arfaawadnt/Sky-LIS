import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { TierOption } from '../../../core/api/pricing.contracts';
import type { PriceQuoteDocument, PriceQuoteRequest } from '../../../core/api/registration.contracts';
import { RegistrationApi } from '../data/registration.api';
import { utcOffsetMinutes } from '../data/registration-format';

/** A service typed on the price query (the quote prices it on the server). */
export interface QuoteLine {
  readonly serviceId: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
}

export type QuoteGender = 'Male' | 'Female' | null;

/**
 * State of the price query (SRS CP-12, LDM §7.2 "Query"; 01 §6.3): the contract (a tier, or the cash default when none
 * is chosen), the services typed with the same entry as registration, an optional gender for the preparations, and the
 * quote document through `resource()` (`POST /api/v1/registration/quote`, nothing is saved). "Continue to registration"
 * hands the services and the tier to CP-06 through the query string.
 */
@Injectable()
export class PriceQueryFacade {
  private readonly api = inject(RegistrationApi);

  readonly tierId = signal('');
  readonly gender = signal<QuoteGender>(null);
  readonly lines = signal<readonly QuoteLine[]>([]);

  readonly tiers = resource({ loader: ({ abortSignal }) => this.api.tiers(abortSignal) });

  readonly tierOptions = computed<readonly TierOption[]>(() => (this.tiers.hasValue() ? this.tiers.value() : []));

  readonly tier = computed<TierOption | null>(() => this.tierOptions().find((tier) => tier.id === this.tierId()) ?? null);

  readonly request = computed<PriceQuoteRequest | undefined>(() => {
    const lines = this.lines();
    if (lines.length === 0) {
      return undefined;
    }

    const tier = this.tier();
    return {
      payerId: tier?.payerId ?? null,
      contractTierId: tier?.id ?? null,
      serviceIds: lines.map((line) => line.serviceId),
      gender: this.gender(),
      utcOffsetMinutes: utcOffsetMinutes(),
    };
  });

  readonly quote = resource({
    params: () => this.request(),
    loader: ({ params, abortSignal }) => this.api.quote(params, abortSignal),
  });

  /** The quote for the current request (null while nothing is typed or the quote loads). */
  readonly document = computed<PriceQuoteDocument | null>(() => (this.request() !== undefined && this.quote.hasValue() ? this.quote.value() : null));

  /** The default contract: the cash payer's default tier, else the first active tier. */
  defaultTier(): void {
    if (this.tierId() !== '') {
      return;
    }

    const options = this.tierOptions();
    const preferred = options.find((tier) => tier.isDefault && tier.payerType === 'Cash') ?? options.find((tier) => tier.isDefault) ?? options[0];
    if (preferred) {
      this.tierId.set(preferred.id);
    }
  }

  /** Adds typed services that are not on the query yet; returns the codes that were already there. */
  addServices(services: readonly QuoteLine[]): readonly string[] {
    const present = new Set(this.lines().map((line) => line.serviceId));
    const duplicates = services.filter((service) => present.has(service.serviceId)).map((service) => service.code);
    const fresh = services.filter((service, index) => !present.has(service.serviceId) && services.findIndex((other) => other.serviceId === service.serviceId) === index);
    if (fresh.length > 0) {
      this.lines.update((lines) => [...lines, ...fresh]);
    }

    return duplicates;
  }

  removeLine(serviceId: string): void {
    this.lines.update((lines) => lines.filter((line) => line.serviceId !== serviceId));
  }

  clear(): void {
    this.lines.set([]);
    this.gender.set(null);
  }
}
