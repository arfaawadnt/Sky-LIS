import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { PageRequest } from '../../../core/api/paging';
import type { TreasuryAccessRequestFilter, TreasuryAccessRequestListItem, TreasuryAccessStatus, TreasuryListItem } from '../../../core/api/treasury.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { DeviceIdentityService } from '../../../core/device/device-identity.service';
import { mutation } from '../../../core/state/mutation';
import { allPages } from '../data/csv-export';
import { TreasuryApi } from '../data/treasury.api';

/** Facade of 01 §6.3: reads through `resource()`, writes through the shared `mutation()` helper, state in signals. Provided at the feature route. */
@Injectable()
export class TreasuryAccessFacade {
  private readonly api = inject(TreasuryApi);
  private readonly auth = inject(AuthStore);
  private readonly device = inject(DeviceIdentityService);

  readonly filter = signal<TreasuryAccessRequestFilter>({ status: null, mine: false, page: 1, pageSize: 50 });

  readonly requests = resource({
    params: () => this.filter(),
    loader: ({ params, abortSignal }) => this.api.listRequests(params, abortSignal),
  });

  readonly treasuries = resource({
    loader: ({ abortSignal }) => this.api.listTreasuries(true, abortSignal),
  });

  /**
   * The picker of the request form: the active treasuries of the branches the user may work in (intake Q3, validation
   * v0.5.0 O-4); a membership without assigned branches works in every branch and sees all. The server refuses the
   * others with TRS-015.
   */
  readonly requestableTreasuries = computed<readonly TreasuryListItem[]>(() => {
    const items = this.treasuries.hasValue() ? this.treasuries.value().items : [];
    const branches = this.auth.branchIds();
    return branches.length === 0 ? items : items.filter((treasury) => branches.includes(treasury.branchId));
  });

  /** This user's id on this browser (O-10: one per user and browser profile, see `DeviceIdentityService`). */
  readonly deviceId = computed(() => this.device.deviceId());

  readonly currentUserId = computed(() => this.auth.currentUser()?.id ?? null);

  readonly request = mutation(
    (input: { treasuryId: string; deviceLabel: string | null }) => this.api.requestAccess({ treasuryId: input.treasuryId, deviceId: this.deviceId(), deviceLabel: input.deviceLabel }),
    { then: () => this.requests.reload() },
  );

  readonly approve = mutation((input: { id: string; comment: string | null }) => this.api.approve(input.id, input.comment), { then: () => this.requests.reload() });

  readonly reject = mutation((input: { id: string; reason: string }) => this.api.reject(input.id, input.reason), { then: () => this.requests.reload() });

  readonly revoke = mutation((input: { id: string; reason: string }) => this.api.revoke(input.id, input.reason), { then: () => this.requests.reload() });

  readonly cancel = mutation((input: { id: string; reason: string | null }) => this.api.cancel(input.id, input.reason), { then: () => this.requests.reload() });

  /**
   * Re-reads the requests and the treasury picker when the page opens again: the facade outlives the page (route
   * provider), so a treasury reactivated since the last visit must be offered (UAT B-07, D-14). A load in flight is left alone.
   */
  refresh(): void {
    this.requests.reload();
    this.treasuries.reload();
  }

  /** Every request of the current filter (status, mine), page after page, for the CSV export (D-05: with comments and reasons). */
  allRequests(): Promise<readonly TreasuryAccessRequestListItem[]> {
    const filter = this.filter();
    return allPages((page, pageSize) => this.api.listRequests({ ...filter, page, pageSize }));
  }

  setStatus(status: TreasuryAccessStatus | null): void {
    this.filter.update((current) => ({ ...current, status, page: 1 }));
  }

  setMine(mine: boolean): void {
    this.filter.update((current) => ({ ...current, mine, page: 1 }));
  }

  setPage(page: PageRequest): void {
    this.filter.update((current) => ({ ...current, page: page.page, pageSize: page.pageSize }));
  }

  /** The requester never approves or rejects their own request (SOD-TRS-001); the screen hides what the server would refuse. */
  isOwn(item: TreasuryAccessRequestListItem): boolean {
    return item.requestedByUserId === this.currentUserId();
  }

  /** The laboratory's primary administrator may approve (not reject) their own request: the named exception of SOD-TRS-001 (2026-10-14). */
  readonly isPrimaryAdministrator = computed(() => this.auth.currentUser()?.isPrimaryAdministrator === true);
}
