import { Injectable, type WritableSignal, computed, inject, resource, signal } from '@angular/core';

import type { GovernorateListItem, LocationLevel, LocationListItem, LocationRequest } from '../../../core/api/platform.contracts';
import { mutation } from '../../../core/state/mutation';
import { PlatformApi } from '../data/platform.api';

/** What a column asks the server with: the typed search and whether inactive rows are shown. */
export interface LocationColumnFilter {
  readonly search: string;
  readonly showInactive: boolean;
}

/** A create (`id` null) or a rename of one row of a level; `parentId` is the governorate, the city or the district. */
export interface LocationSave {
  readonly level: LocationLevel;
  readonly parentId: string;
  readonly id: string | null;
  readonly request: LocationRequest;
}

export interface LocationToggle {
  readonly level: LocationLevel;
  readonly id: string;
  readonly active: boolean;
}

const EMPTY_FILTER: LocationColumnFilter = { search: '', showInactive: false };

/**
 * SA-11b Locations (LIS-CR-003 §3): the three linked columns under one governorate — its cities, the districts of the
 * selected city, the areas of the selected district — each searched and filtered on the server, with one save
 * (create or rename) and one activate / deactivate mutation; the level a mutation touched is reloaded afterwards.
 * Provided by the drawer: `open(governorate)` resets everything.
 */
@Injectable()
export class LocationsFacade {
  private readonly api = inject(PlatformApi);

  readonly governorate = signal<GovernorateListItem | null>(null);
  readonly cityId = signal<string | null>(null);
  readonly districtId = signal<string | null>(null);

  readonly filters: Readonly<Record<LocationLevel, WritableSignal<LocationColumnFilter>>> = {
    cities: signal(EMPTY_FILTER),
    districts: signal(EMPTY_FILTER),
    areas: signal(EMPTY_FILTER),
  };

  readonly cities = resource({
    params: () => this.paramsOf('cities', this.governorate()?.id ?? null),
    loader: ({ params, abortSignal }) => this.api.listLocations('cities', params.parentId, params.filter, abortSignal),
  });

  readonly districts = resource({
    params: () => this.paramsOf('districts', this.cityId()),
    loader: ({ params, abortSignal }) => this.api.listLocations('districts', params.parentId, params.filter, abortSignal),
  });

  readonly areas = resource({
    params: () => this.paramsOf('areas', this.districtId()),
    loader: ({ params, abortSignal }) => this.api.listLocations('areas', params.parentId, params.filter, abortSignal),
  });

  /** The level the last save or toggle addressed: its column shows the refusal. */
  readonly activeLevel = signal<LocationLevel | null>(null);

  readonly save = mutation<LocationSave, void>(
    async (input) => {
      this.activeLevel.set(input.level);
      if (input.id === null) {
        await this.api.createLocation(input.level, input.parentId, input.request);
      } else {
        await this.api.renameLocation(input.level, input.id, input.request);
      }

      this.reload(input.level);
    },
  );

  readonly toggle = mutation<LocationToggle, void>(async (input) => {
    this.activeLevel.set(input.level);
    await this.api.setLocationActive(input.level, input.id, input.active);
    this.reload(input.level);
  });

  /** The selected city row (null once it left the list, e.g. hidden by the search). */
  readonly selectedCity = computed(() => this.rowOf(this.cities.hasValue() ? this.cities.value() : [], this.cityId()));
  readonly selectedDistrict = computed(() => this.rowOf(this.districts.hasValue() ? this.districts.value() : [], this.districtId()));

  open(governorate: GovernorateListItem): void {
    this.save.reset();
    this.toggle.reset();
    this.activeLevel.set(null);
    this.cityId.set(null);
    this.districtId.set(null);
    for (const level of ['cities', 'districts', 'areas'] as const) {
      this.filters[level].set(EMPTY_FILTER);
    }

    this.governorate.set(governorate);
  }

  close(): void {
    this.governorate.set(null);
    this.cityId.set(null);
    this.districtId.set(null);
  }

  /** Selecting a city shows its districts and forgets the district; selecting a district shows its areas. */
  select(level: LocationLevel, id: string | null): void {
    if (level === 'cities') {
      this.cityId.set(id);
      this.districtId.set(null);
    } else if (level === 'districts') {
      this.districtId.set(id);
    }
  }

  setFilter(level: LocationLevel, filter: Partial<LocationColumnFilter>): void {
    this.filters[level].update((current) => ({ ...current, ...filter }));
  }

  /** The parent id a new row of the level is created under (null while no parent is selected). */
  parentOf(level: LocationLevel): string | null {
    return level === 'cities' ? (this.governorate()?.id ?? null) : level === 'districts' ? this.cityId() : this.districtId();
  }

  private paramsOf(level: LocationLevel, parentId: string | null): { parentId: string; filter: { search: string | null; isActive: boolean | null } } | undefined {
    if (parentId === null) {
      return undefined;
    }

    const filter = this.filters[level]();
    return { parentId, filter: { search: filter.search.trim() === '' ? null : filter.search.trim(), isActive: filter.showInactive ? null : true } };
  }

  private reload(level: LocationLevel): void {
    (level === 'cities' ? this.cities : level === 'districts' ? this.districts : this.areas).reload();
  }

  private rowOf(rows: readonly LocationListItem[], id: string | null): LocationListItem | null {
    return id === null ? null : (rows.find((row) => row.id === id) ?? null);
  }
}
