import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { CompatibleSampleTypeView, SampleConditionPairItem } from '../../../core/api/compatible-conditions.contracts';
import { mutation } from '../../../core/state/mutation';
import { CompatibleConditionsApi } from '../data-a/compatible-conditions.api';

/** One line of the page: a condition of a sample type with the condition it merges into (a type without conditions has one line, `conditionId` null). */
export interface CompatibleRow {
  readonly id: string;
  readonly sampleTypeId: string;
  readonly typeCode: string;
  readonly typeNameEn: string;
  readonly typeNameAr: string;
  readonly typeActive: boolean;
  readonly conditionId: string | null;
  readonly conditionCode: string | null;
  readonly conditionNameEn: string | null;
  readonly conditionNameAr: string | null;
  readonly conditionActive: boolean;
  readonly targetCode: string | null;
  readonly targetNameEn: string | null;
  readonly targetNameAr: string | null;
}

/** Compatible sample conditions (LIS-CR-015 M4): every sample type with its map, the search over the loaded list and the save of one type. Provided at the route. */
@Injectable()
export class CompatibleConditionsFacade {
  private readonly api = inject(CompatibleConditionsApi);

  readonly search = signal('');

  readonly list = resource({
    loader: ({ abortSignal }) => this.api.list(abortSignal),
  });

  readonly types = computed<readonly CompatibleSampleTypeView[]>(() => (this.list.hasValue() ? this.list.value() : []));

  /** The lines matching the search (codes and both names of the type, the condition and its target). */
  readonly rows = computed<readonly CompatibleRow[]>(() => {
    const term = this.search().trim().toLocaleLowerCase();
    const rows = this.types().flatMap(rowsOf);
    return term === '' ? rows : rows.filter((row) => searchText(row).includes(term));
  });

  readonly save = mutation((input: { readonly sampleTypeId: string; readonly pairs: readonly SampleConditionPairItem[] }) => this.api.save(input.sampleTypeId, input.pairs), {
    then: () => this.list.reload(),
  });

  setSearch(search: string): void {
    this.search.set(search);
  }

  typeOf(sampleTypeId: string): CompatibleSampleTypeView | null {
    return this.types().find((type) => type.id === sampleTypeId) ?? null;
  }

  /** Re-reads the list when the page opens again: the facade outlives the page (route provider). */
  refresh(): void {
    this.list.reload();
  }
}

function rowsOf(type: CompatibleSampleTypeView): readonly CompatibleRow[] {
  const base = { sampleTypeId: type.id, typeCode: type.code, typeNameEn: type.nameEn, typeNameAr: type.nameAr, typeActive: type.isActive };
  if (type.conditions.length === 0) {
    return [{ ...base, id: type.id, conditionId: null, conditionCode: null, conditionNameEn: null, conditionNameAr: null, conditionActive: false, targetCode: null, targetNameEn: null, targetNameAr: null }];
  }

  return type.conditions.map((condition) => {
    const target = condition.mergesIntoConditionId === null ? null : (type.conditions.find((candidate) => candidate.id === condition.mergesIntoConditionId) ?? null);
    return {
      ...base,
      id: condition.id,
      conditionId: condition.id,
      conditionCode: condition.code,
      conditionNameEn: condition.nameEn,
      conditionNameAr: condition.nameAr,
      conditionActive: condition.isActive,
      targetCode: target?.code ?? null,
      targetNameEn: target?.nameEn ?? null,
      targetNameAr: target?.nameAr ?? null,
    };
  });
}

function searchText(row: CompatibleRow): string {
  return [row.typeCode, row.typeNameEn, row.typeNameAr, row.conditionCode, row.conditionNameEn, row.conditionNameAr, row.targetCode, row.targetNameEn, row.targetNameAr]
    .filter((part): part is string => part !== null)
    .join(' ')
    .toLocaleLowerCase();
}
