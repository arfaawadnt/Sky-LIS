import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { PageRequest } from '../../../core/api/paging';
import type { CancelLinesRequest, VisitSearchFilter, VisitStatus } from '../../../core/api/registration.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { mutation } from '../../../core/state/mutation';
import { RegistrationApi } from '../data/registration.api';
import { classifySearch, isoDay, localDayEndUtc, localDayStartUtc } from '../data/registration-format';

/** The local-day range of the register (inclusive days, `yyyy-MM-dd`). */
export interface DayRange {
  readonly from: string;
  readonly to: string;
}

/**
 * State of Search registrations (CP-10; 01 §6.3): one search box classified as accession, patient number, mobile or
 * name, the day range of the desk's local time, payer, status and "this branch only"; the paged register through
 * `resource()`, the cancellation through `mutation()` (reloads the page after it).
 */
@Injectable()
export class VisitsFacade {
  private readonly api = inject(RegistrationApi);
  private readonly auth = inject(AuthStore);

  readonly text = signal('');
  readonly days = signal<DayRange>({ from: isoDay(new Date()), to: isoDay(new Date()) });
  readonly payerId = signal<string | null>(null);
  readonly status = signal<VisitStatus | null>(null);
  readonly branchOnly = signal(true);
  readonly page = signal<PageRequest>({ page: 1, pageSize: 50 });

  readonly filter = computed<VisitSearchFilter>(() => {
    const search = classifySearch(this.text());
    const days = this.days();
    return {
      accession: search?.target === 'accession' ? search.value : null,
      patientNo: search?.target === 'patientNo' ? search.value : null,
      name: search?.target === 'name' ? search.value : null,
      mobile: search?.target === 'mobile' ? search.value : null,
      from: localDayStartUtc(days.from),
      to: localDayEndUtc(days.to),
      branchId: this.branchOnly() ? this.auth.currentBranch() : null,
      payerId: this.payerId(),
      status: this.status(),
      patientId: null,
      page: this.page().page,
      pageSize: this.page().pageSize,
    };
  });

  readonly visits = resource({
    params: () => this.filter(),
    loader: ({ params, abortSignal }) => this.api.search(params, abortSignal),
  });

  readonly tiers = resource({ loader: ({ abortSignal }) => this.api.tiers(abortSignal) });

  /** The payers of the active tiers, once each. */
  readonly payers = computed(() => {
    const seen = new Map<string, { id: string; nameEn: string; nameAr: string }>();
    for (const tier of this.tiers.value() ?? []) {
      if (!seen.has(tier.payerId)) {
        seen.set(tier.payerId, { id: tier.payerId, nameEn: tier.payerNameEn, nameAr: tier.payerNameAr });
      }
    }

    return [...seen.values()];
  });

  readonly cancel = mutation((input: { visitId: string; request: CancelLinesRequest }) => this.api.cancel(input.visitId, input.request), { then: () => this.visits.reload() });

  setText(text: string): void {
    this.text.set(text);
    this.firstPage();
  }

  setDays(change: Partial<DayRange>): void {
    this.days.update((days) => ({ ...days, ...change }));
    this.firstPage();
  }

  /** Clears the day range (a search by accession or patient finds older visits too). */
  allDays(): void {
    this.days.set({ from: '', to: '' });
    this.firstPage();
  }

  setPayer(payerId: string | null): void {
    this.payerId.set(payerId);
    this.firstPage();
  }

  setStatus(status: VisitStatus | null): void {
    this.status.set(status);
    this.firstPage();
  }

  setBranchOnly(value: boolean): void {
    this.branchOnly.set(value);
    this.firstPage();
  }

  setPage(page: PageRequest): void {
    this.page.set(page);
  }

  /** Re-reads the register and the tiers when the page opens again (the filter is kept): the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14, A-01). A load in flight is left alone. */
  refresh(): void {
    this.cancel.reset();
    this.visits.reload();
    this.tiers.reload();
  }

  exportRows(): ReturnType<RegistrationApi['export']> {
    return this.api.export(this.filter());
  }

  private firstPage(): void {
    this.page.update((page) => ({ ...page, page: 1 }));
  }
}
