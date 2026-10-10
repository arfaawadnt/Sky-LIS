import { Injectable, inject, resource } from '@angular/core';

import type { SampleConditionView } from '../../../core/api/catalogue-a.contracts';
import { CatalogueAApi, OPTIONS_PAGE_SIZE } from '../data-a/catalogue-a.api';

/** One option of an editor select; inactive rows stay listed (disabled) so a kept reference still shows its name (BR-24). */
export interface CatalogueOption {
  readonly value: string;
  readonly code: string | null;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly isActive: boolean;
}

const ALL = { search: '', isActive: null, page: 1, pageSize: OPTIONS_PAGE_SIZE } as const;

/**
 * The reference lists the service and test editors select from (clinical groups, sample types, units, patient
 * conditions), loaded once per editor from this module's own list endpoints, plus the conditions of a sample type.
 * Provided at the editor routes.
 */
@Injectable()
export class CatalogueAOptions {
  private readonly api = inject(CatalogueAApi);
  private readonly conditionCache = new Map<string, Promise<readonly SampleConditionView[]>>();

  readonly groups = resource({
    loader: async ({ abortSignal }): Promise<readonly CatalogueOption[]> =>
      (await this.api.listClinicalGroups(ALL, abortSignal)).items.map((row) => ({ value: row.id, code: row.code, nameEn: row.nameEn, nameAr: row.nameAr, isActive: row.isActive })),
  });

  readonly sampleTypes = resource({
    loader: async ({ abortSignal }): Promise<readonly CatalogueOption[]> =>
      (await this.api.listSampleTypes(ALL, abortSignal)).items.map((row) => ({ value: row.id, code: row.code, nameEn: row.nameEn, nameAr: row.nameAr, isActive: row.isActive })),
  });

  readonly units = resource({
    loader: async ({ abortSignal }): Promise<readonly CatalogueOption[]> =>
      (await this.api.listUnits(ALL, abortSignal)).items.map((row) => ({ value: row.id, code: null, nameEn: row.name, nameAr: row.name, isActive: row.isActive })),
  });

  readonly patientConditions = resource({
    loader: async ({ abortSignal }): Promise<readonly CatalogueOption[]> =>
      (await this.api.listPatientConditions(ALL, abortSignal)).items.map((row) => ({ value: row.id, code: null, nameEn: row.nameEn, nameAr: row.nameAr, isActive: row.isActive })),
  });

  /** The conditions of a sample type (cached per editor). */
  conditionsOf(sampleTypeId: string): Promise<readonly SampleConditionView[]> {
    let pending = this.conditionCache.get(sampleTypeId);
    if (!pending) {
      pending = this.api.getSampleType(sampleTypeId).then(
        (detail) => detail.conditions,
        () => {
          this.conditionCache.delete(sampleTypeId);
          return [];
        },
      );
      this.conditionCache.set(sampleTypeId, pending);
    }

    return pending;
  }

  /** Re-reads the lookups when a page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14). A load in flight is left alone. */
  refresh(): void {
    this.groups.reload();
    this.sampleTypes.reload();
    this.units.reload();
    this.patientConditions.reload();
  }
}

/** Options of a select in the current language: inactive rows are disabled unless they are the current value. */
export function optionLabel(option: CatalogueOption, lang: string): string {
  const name = lang === 'ar' ? option.nameAr || option.nameEn : option.nameEn || option.nameAr;
  return option.code ? `${option.code} · ${name}` : name;
}
