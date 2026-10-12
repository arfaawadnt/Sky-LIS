import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { PageRequest } from '../../../core/api/paging';
import type { PatientGender, PatientListFilter, PatientStatus, RegisterPatientRequest, SimilarPatient, UpdatePatientRequest } from '../../../core/api/patients.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { mutation } from '../../../core/state/mutation';
import { sortParams } from '../../../shared/forms/paging';
import { type SortKeyMap, type TableSort, serverSortOf } from '../../../shared/ui/data-table/data-table.types';
import { PatientLookupApi } from '../../../shared/patient/patient-lookup.api';
import { PatientsApi } from '../data/patients.api';
import type { TypedIdentity } from '../../../shared/patient/patient-form.component';

/** Minimum typed identity before the similar lookup runs: two name characters or a whole mobile. */
const MIN_NAME = 2;
const MIN_MOBILE = 8;

/** The server keys of the sortable columns of the register (B-03): the file number and the full name. */
export const PATIENT_SORT_KEYS: SortKeyMap = { number: 'number', name: 'name' };

/**
 * State of the patients register (CP-11) and its create/edit drawer (01 §6.3): reads through `resource()`, writes
 * through `mutation()`. Provided at the feature route.
 */
@Injectable()
export class PatientsFacade {
  private readonly api = inject(PatientsApi);
  private readonly lookup = inject(PatientLookupApi);
  private readonly i18n = inject(I18nService);

  /** Active files by default (a Merged file is history; its survivor is the file in use). */
  readonly filter = signal<PatientListFilter>({ q: '', gender: null, vip: null, status: 'Active', page: 1, pageSize: 50 });

  /** The column order chosen in the table header; the server sorts the whole list before paging (validation v0.5.0 B-03). */
  readonly sort = signal<TableSort | null>(null);

  private readonly query = computed<PatientListFilter>(() => ({ ...this.filter(), ...sortParams(serverSortOf(this.sort(), PATIENT_SORT_KEYS, () => this.i18n.lang())) }));

  readonly list = resource({
    params: () => this.query(),
    loader: ({ params, abortSignal }) => this.api.list(params, abortSignal),
  });

  readonly settings = resource({ loader: ({ abortSignal }) => this.lookup.formSettings(abortSignal) });
  readonly countries = resource({ loader: () => this.lookup.countries() });
  readonly governorates = resource({ loader: () => this.lookup.governorates() });
  readonly conditions = resource({ loader: () => this.lookup.conditions() });

  /** The file open in the edit drawer (`null` while creating or closed). */
  readonly editingId = signal<string | null>(null);

  readonly editing = resource({
    params: () => this.editingId() ?? undefined,
    loader: ({ params, abortSignal }) => this.api.get(params, abortSignal),
  });

  private readonly typed = signal<TypedIdentity | null>(null);

  readonly similar = resource({
    params: () => {
      const typed = this.typed();
      return typed !== null && (typed.name.length >= MIN_NAME || typed.mobile.length >= MIN_MOBILE) ? typed : undefined;
    },
    loader: ({ params, abortSignal }) => this.lookup.similar(params.name, params.mobile, abortSignal),
  });

  /** The candidates to warn about: empty when the tenant switched the warning off (`similarNameWarning`). */
  readonly candidates = computed<readonly SimilarPatient[]>(() => {
    const response = this.similar.hasValue() ? this.similar.value() : null;
    return response?.warningEnabled ? response.candidates : [];
  });

  readonly register = mutation((request: RegisterPatientRequest) => this.lookup.register(request), { then: () => this.list.reload() });

  readonly update = mutation((input: { readonly id: string; readonly request: UpdatePatientRequest }) => this.api.update(input.id, input.request), {
    then: () => {
      this.list.reload();
      this.editing.reload();
    },
  });

  readonly exportCsv = mutation(() => this.api.export(this.filter()));

  setSearch(q: string): void {
    this.filter.update((current) => ({ ...current, q, page: 1 }));
  }

  /** A header click: the new order starts at page 1. */
  setSort(sort: TableSort | null): void {
    this.sort.set(sort);
    this.filter.update((current) => ({ ...current, page: 1 }));
  }

  setGender(gender: PatientGender | null): void {
    this.filter.update((current) => ({ ...current, gender, page: 1 }));
  }

  setStatus(status: PatientStatus | null): void {
    this.filter.update((current) => ({ ...current, status, page: 1 }));
  }

  /** Only offered to holders of `patients.viewConfidential` (the server refuses the filter otherwise, PAT-031). */
  setVip(vip: boolean | null): void {
    this.filter.update((current) => ({ ...current, vip, page: 1 }));
  }

  setPage(page: PageRequest): void {
    this.filter.update((current) => ({ ...current, page: page.page, pageSize: page.pageSize }));
  }

  /** Opens the drawer for a new file (`null`) or an existing one; clears the previous typing and problems. */
  startEditing(id: string | null): void {
    this.typed.set(null);
    this.register.reset();
    this.update.reset();
    this.editingId.set(id);
  }

  onTyped(identity: TypedIdentity): void {
    this.typed.set(identity);
  }

  /** Re-reads the register and the form's lookups when the page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14, A-01). A load in flight is left alone. */
  refresh(): void {
    this.exportCsv.reset();
    this.list.reload();
    this.settings.reload();
    this.countries.reload();
    this.governorates.reload();
    this.conditions.reload();
    this.editing.reload();
  }
}
