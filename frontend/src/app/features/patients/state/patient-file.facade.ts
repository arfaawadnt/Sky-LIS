import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { MergePatientsRequest, PatientDocumentItem, PatientListItem, UpdatePatientRequest } from '../../../core/api/patients.contracts';
import { mutation } from '../../../core/state/mutation';
import { PatientLookupApi } from '../data/patient-lookup.api';
import { PatientsApi } from '../data/patients.api';

const MERGE_SEARCH_LIMIT = 10;

/**
 * State of one patient file page (CP-11): the file, its documents (including those of files merged into it), its
 * visits (Registration module, wave 2: an empty state until it exists), the edit drawer, the merge dialog and the
 * document actions. Provided at the route.
 */
@Injectable()
export class PatientFileFacade {
  private readonly api = inject(PatientsApi);
  private readonly lookup = inject(PatientLookupApi);

  readonly patientId = signal<string | null>(null);
  readonly includeInactive = signal(true);
  /** Visits load when their tab is first opened. */
  readonly visitsRequested = signal(false);
  readonly mergeQuery = signal('');

  readonly detail = resource({
    params: () => this.patientId() ?? undefined,
    loader: ({ params, abortSignal }) => this.api.get(params, abortSignal),
  });

  readonly documents = resource({
    params: () => {
      const id = this.patientId();
      return id === null ? undefined : { id, includeInactive: this.includeInactive() };
    },
    loader: ({ params, abortSignal }) => this.api.documents(params.id, params.includeInactive, abortSignal),
  });

  readonly visits = resource({
    params: () => {
      const id = this.patientId();
      return id !== null && this.visitsRequested() ? id : undefined;
    },
    loader: ({ params, abortSignal }) => this.api.visits(params, abortSignal),
  });

  readonly settings = resource({ loader: ({ abortSignal }) => this.lookup.formSettings(abortSignal) });
  readonly countries = resource({ loader: () => this.lookup.countries() });
  readonly governorates = resource({ loader: () => this.lookup.governorates() });
  readonly conditions = resource({ loader: () => this.lookup.conditions() });

  /** Files the merge dialog offers: Active ones other than this file. */
  readonly mergeSearch = resource({
    params: () => (this.mergeQuery().length >= 2 ? this.mergeQuery() : undefined),
    loader: ({ params, abortSignal }) => this.lookup.search(params, MERGE_SEARCH_LIMIT, abortSignal),
  });

  readonly mergeCandidates = computed<readonly PatientListItem[]>(() =>
    (this.mergeSearch.hasValue() ? this.mergeSearch.value().items : []).filter((item) => item.id !== this.patientId() && item.status === 'Active'),
  );

  readonly conditionName = computed(() => {
    const id = this.detail.hasValue() ? this.detail.value().patientConditionId : null;
    return id === null ? null : ((this.conditions.value() ?? []).find((condition) => condition.id === id) ?? null);
  });

  readonly update = mutation((request: UpdatePatientRequest) => this.api.update(this.requireId(), request), { then: () => this.detail.reload() });

  readonly merge = mutation((request: MergePatientsRequest) => this.api.merge(this.requireId(), request), {
    then: () => {
      this.detail.reload();
      this.documents.reload();
      this.mergeQuery.set('');
    },
  });

  readonly upload = mutation((input: { readonly file: File; readonly note: string | null }) => this.api.upload(this.requireId(), input.file, input.note), {
    then: () => this.documents.reload(),
  });

  readonly deactivate = mutation((input: { readonly document: PatientDocumentItem; readonly reason: string }) => this.api.deactivateDocument(input.document.id, input.reason), {
    then: () => this.documents.reload(),
  });

  readonly download = mutation((document: PatientDocumentItem) => this.api.download(document));

  load(id: string): void {
    if (this.patientId() !== id) {
      this.visitsRequested.set(false);
      this.mergeQuery.set('');
      this.patientId.set(id);
    }
  }

  private requireId(): string {
    const id = this.patientId();
    if (id === null) {
      throw new Error('No patient file is loaded.');
    }

    return id;
  }
}
