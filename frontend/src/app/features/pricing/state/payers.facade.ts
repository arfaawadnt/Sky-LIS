import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { PageRequest } from '../../../core/api/paging';
import type { ContractTierRequest, DefaultPayerIds, Paged, PayerListItem, PayerRequest } from '../../../core/api/pricing.contracts';
import { mutation } from '../../../core/state/mutation';
import { sortParams } from '../../../shared/forms/paging';
import type { MasterDataFilter } from '../../../shared/ui/master-data-page/master-data.types';
import { PricingApi } from '../data/pricing.api';

/**
 * Payers & contracts (CP-32). The register itself is the shared master-data page (its adapter calls `list`, `create`,
 * `update`, `setActive` here); this facade adds the tenant's cash payer provisioning (intake Q1, once per page) and the
 * tiers drawer of one payer. Provided at the route.
 */
@Injectable()
export class PayersFacade {
  private readonly api = inject(PricingApi);
  private ensured: Promise<DefaultPayerIds | null> | null = null;

  /** The payer whose tiers drawer is open. */
  readonly tiersPayerId = signal<string | null>(null);

  readonly tiersPayer = resource({
    params: () => this.tiersPayerId() ?? undefined,
    loader: ({ params, abortSignal }) => this.api.getPayer(params, abortSignal),
  });

  readonly tiers = computed(() => (this.tiersPayer.hasValue() ? this.tiersPayer.value().tiers : []));

  /** Bumped after a tier change so the page reloads the register (tier names are a column). */
  readonly changes = signal(0);

  readonly addTier = mutation((input: { payerId: string; request: ContractTierRequest }) => this.api.addTier(input.payerId, input.request), { then: () => this.afterTierChange() });

  readonly renameTier = mutation((input: { payerId: string; tierId: string; request: ContractTierRequest }) => this.api.renameTier(input.payerId, input.tierId, input.request), {
    then: () => this.afterTierChange(),
  });

  readonly setTierActive = mutation((input: { payerId: string; tierId: string; active: boolean }) => this.api.setTierActive(input.payerId, input.tierId, input.active), {
    then: () => this.afterTierChange(),
  });

  /** The master-data adapter's `list`: provisions the cash payer once (a refusal is ignored), then pages the register in the server order of the table (B-03). */
  async list(filter: MasterDataFilter, page: PageRequest, signal?: AbortSignal): Promise<Paged<PayerListItem>> {
    await this.ensureDefault();
    return this.api.listPayers({ ...sortParams(filter.sort), search: filter.search, type: null, isActive: filter.active, page: page.page, pageSize: page.pageSize }, signal);
  }

  create(request: PayerRequest): Promise<unknown> {
    return this.api.createPayer(request);
  }

  update(id: string, request: PayerRequest): Promise<unknown> {
    return this.api.updatePayer(id, request);
  }

  setActive(id: string, active: boolean): Promise<unknown> {
    return this.api.setPayerActive(id, active);
  }

  /** Idempotent on the server; called at most once per page instance. */
  ensureDefault(): Promise<DefaultPayerIds | null> {
    this.ensured ??= this.api.ensureDefaultPayer().catch((): null => null);
    return this.ensured;
  }

  /** A new visit of the page: provisioning is checked again and a drawer left open by the previous visit is closed (UAT D-14). */
  refresh(): void {
    this.ensured = null;
    this.closeTiers();
  }

  openTiers(payerId: string): void {
    this.addTier.reset();
    this.renameTier.reset();
    this.setTierActive.reset();
    this.tiersPayerId.set(payerId);
  }

  closeTiers(): void {
    this.tiersPayerId.set(null);
  }

  private afterTierChange(): void {
    this.tiersPayer.reload();
    this.changes.update((value) => value + 1);
  }
}
