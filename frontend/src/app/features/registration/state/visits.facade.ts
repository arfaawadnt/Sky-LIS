import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { PageRequest } from '../../../core/api/paging';
import type { UpdatePatientRequest } from '../../../core/api/patients.contracts';
import type { CancelLinesRequest, VisitSearchFilter, VisitStatus } from '../../../core/api/registration.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { mutation } from '../../../core/state/mutation';
import { PatientLookupApi } from '../../../shared/patient/patient-lookup.api';
import { RegistrationApi } from '../data/registration.api';
import { classifySearch, isoDay, localDayEndUtc, localDayStartUtc } from '../data/registration-format';

/** The local-day range of the register (inclusive days, `yyyy-MM-dd`). */
export interface DayRange {
  readonly from: string;
  readonly to: string;
}

/** The patient file opened from a row: the file and the visit the edit is judged on (PAT-032). */
export interface PatientEditTarget {
  readonly patientId: string;
  readonly visitId: string;
}

/**
 * State of Search registrations (CP-10; 01 §6.3): one search box classified as accession, patient number, mobile or
 * name, the day range of the desk's local time, payer, status and "this branch only"; the paged register through
 * `resource()`, the cancellation through `mutation()` (reloads the page after it), and the patient drawer (change
 * request 2026-10-13 item 1): the file, its demographics lock for the row's visit, the form's lookups and the update
 * that reloads the register so the name and mobile columns follow.
 */
@Injectable()
export class VisitsFacade {
  private readonly api = inject(RegistrationApi);
  private readonly patients = inject(PatientLookupApi);
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

  /** The row whose patient file is open in the drawer; null closes it and stops its reads. */
  readonly patientEdit = signal<PatientEditTarget | null>(null);

  readonly patientDetail = resource({
    params: () => this.patientEdit() ?? undefined,
    loader: ({ params, abortSignal }) => this.patients.get(params.patientId, abortSignal),
  });

  /** Read before the form shows: what the server will decide on an identity change from this visit. */
  readonly patientLock = resource({
    params: () => this.patientEdit() ?? undefined,
    loader: ({ params, abortSignal }) => this.patients.demographicsLock(params.patientId, params.visitId, abortSignal),
  });

  readonly patientSettings = resource({
    params: () => (this.patientEdit() === null ? undefined : true),
    loader: ({ abortSignal }) => this.patients.formSettings(abortSignal),
  });

  readonly countries = resource({ params: () => (this.patientEdit() === null ? undefined : true), loader: () => this.patients.countries() });
  readonly governorates = resource({ params: () => (this.patientEdit() === null ? undefined : true), loader: () => this.patients.governorates() });
  readonly conditions = resource({ params: () => (this.patientEdit() === null ? undefined : true), loader: () => this.patients.conditions() });

  readonly updatePatient = mutation(
    (input: { patientId: string; request: UpdatePatientRequest }) => this.patients.update(input.patientId, input.request),
    { then: () => this.visits.reload() },
  );

  openPatient(target: PatientEditTarget): void {
    this.updatePatient.reset();
    this.patientEdit.set(target);
  }

  closePatient(): void {
    this.patientEdit.set(null);
  }

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
    this.updatePatient.reset();
    this.patientEdit.set(null);
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
