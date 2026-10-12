import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { CATALOGUE_B_LOOKUP_API, type LinkedServiceView } from '../../../core/api/catalogue-b.contracts';
import { ORGANIZATION_API, ORGANIZATION_PERMISSIONS, type SelectableBranchesResponse } from '../../../core/api/organization.contracts';
import { PRICING_PERMISSIONS } from '../../../core/api/pricing.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import type { SelectOption } from '../../../shared/ui/master-data-page/master-data.types';
import { CatalogueBApi } from './catalogue-b.api';

/** One choice of a picker: an id with its code and both names (`inactive` greys it out). */
export interface CatalogueBOption {
  readonly id: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly inactive?: boolean;
}

/** A contract tier with its payer, as the sending-lab editor offers it ("Payer — Tier"). */
export interface CatalogueBTierOption {
  readonly id: string;
  readonly payerId: string;
  readonly payerNameEn: string;
  readonly payerNameAr: string;
  readonly nameEn: string;
  readonly nameAr: string;
}

type Json = Readonly<Record<string, unknown>>;

function isRecord(value: unknown): value is Json {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/** A list endpoint answers either an array or a `Paged` envelope; both become the array of records. */
export function recordsOf(body: unknown): readonly Json[] {
  const items: unknown = Array.isArray(body) ? body : isRecord(body) ? body['items'] : null;
  return Array.isArray(items) ? items.filter(isRecord) : [];
}

function text(record: Json, ...keys: readonly string[]): string {
  for (const key of keys) {
    const value = record[key];
    if (typeof value === 'string' && value.length > 0) {
      return value;
    }
  }

  return '';
}

/** A record of another module as a picker option (id + code/shortName + names), or null when it has no id. */
export function toOption(record: Json): CatalogueBOption | null {
  const id = text(record, 'id');
  if (id === '') {
    return null;
  }

  const nameEn = text(record, 'nameEn', 'name');
  return {
    id,
    code: text(record, 'code', 'shortName', 'icdCode'),
    nameEn,
    nameAr: text(record, 'nameAr') || nameEn,
    ...(record['isActive'] === false ? { inactive: true } : {}),
  };
}

export function optionsOf(body: unknown): readonly CatalogueBOption[] {
  return recordsOf(body)
    .map(toOption)
    .filter((option): option is CatalogueBOption => option !== null);
}

export function serviceOption(service: LinkedServiceView): CatalogueBOption {
  return { id: service.id, code: service.code, nameEn: service.nameEn, nameAr: service.nameAr, ...(service.isActive ? {} : { inactive: true }) };
}

/** `{ value, label, labelAr }` for the master-data `select` fields. */
export function toSelectOption(option: CatalogueBOption): SelectOption {
  const prefix = option.code ? `${option.code} · ` : '';
  return { value: option.id, label: prefix + option.nameEn, labelAr: prefix + option.nameAr };
}

/**
 * The lookups Catalogue B reads from the other modules of its wave (services and clinical groups of Catalogue A,
 * branches of Organization, contract tiers of Pricing) and its own option lists. Responses are parsed defensively:
 * array or `Paged`, missing fields become empty strings.
 */
@Injectable({ providedIn: 'root' })
export class CatalogueBLookups {
  private readonly http = inject(HttpClient);
  private readonly api = inject(CatalogueBApi);
  private readonly access = inject(PermissionsService);

  /** Active services matching the text (Catalogue A's picker endpoint, at most 50). */
  async searchServices(search: string): Promise<readonly CatalogueBOption[]> {
    const params = new HttpParams({ fromObject: search.trim() === '' ? { pageSize: 50 } : { search: search.trim(), pageSize: 50 } });
    return optionsOf(await firstValueFrom(this.http.get<unknown>(CATALOGUE_B_LOOKUP_API.services, { params })));
  }

  async clinicalGroups(): Promise<readonly CatalogueBOption[]> {
    const params = new HttpParams({ fromObject: { isActive: true, pageSize: 200 } });
    return optionsOf(await firstValueFrom(this.http.get<unknown>(CATALOGUE_B_LOOKUP_API.clinicalGroups, { params })));
  }

  /**
   * The branches of the laboratory: the branch register for a user who may read it (`organization.view`), else the
   * branches the session may select (CP-04, open to every signed-in user). A reader of the catalogue without the
   * organization right (reception) no longer meets a 403 on every visit of Doctors (validation v0.5.0 R-08).
   */
  async branches(): Promise<readonly CatalogueBOption[]> {
    if (!this.access.can(ORGANIZATION_PERMISSIONS.view)) {
      const selectable = await firstValueFrom(this.http.get<SelectableBranchesResponse>(ORGANIZATION_API.selectableBranches));
      return (selectable?.branches ?? []).map((branch) => ({ id: branch.id, code: branch.code, nameEn: branch.nameEn, nameAr: branch.nameAr || branch.nameEn }));
    }

    const params = new HttpParams({ fromObject: { pageSize: 200 } });
    return optionsOf(await firstValueFrom(this.http.get<unknown>(CATALOGUE_B_LOOKUP_API.branches, { params })));
  }

  /** Active contract tiers of active payers (Pricing's tier options); none without `pricing.view`. */
  async tiers(): Promise<readonly CatalogueBTierOption[]> {
    // the tier options are Pricing's (`pricing.view`); a catalogue reader without it gets none instead of a 403 (R-08)
    if (!this.access.can(PRICING_PERMISSIONS.view)) {
      return [];
    }

    const params = new HttpParams({ fromObject: { activeOnly: true } });
    const body = await firstValueFrom(this.http.get<unknown>(CATALOGUE_B_LOOKUP_API.tierOptions, { params }));
    return recordsOf(body)
      .map((record) => ({
        id: text(record, 'id'),
        payerId: text(record, 'payerId'),
        payerNameEn: text(record, 'payerNameEn'),
        payerNameAr: text(record, 'payerNameAr', 'payerNameEn'),
        nameEn: text(record, 'nameEn'),
        nameAr: text(record, 'nameAr', 'nameEn'),
      }))
      .filter((tier) => tier.id !== '' && tier.payerId !== '');
  }

  async doctors(): Promise<readonly CatalogueBOption[]> {
    return (await this.api.doctors.list({ isActive: true, pageSize: 200 })).items.map((doctor) => ({ id: doctor.id, code: doctor.code, nameEn: doctor.nameEn, nameAr: doctor.nameAr }));
  }

  async drugTypes(): Promise<readonly CatalogueBOption[]> {
    return (await this.api.drugTypes.list({ isActive: true, pageSize: 200 })).items.map((drugType) => ({ id: drugType.id, code: '', nameEn: drugType.nameEn, nameAr: drugType.nameAr }));
  }

  async commercialNames(): Promise<readonly CatalogueBOption[]> {
    return (await this.api.commercialNames.list({ isActive: true, pageSize: 200 })).items.map((name) => ({ id: name.id, code: '', nameEn: name.nameEn, nameAr: name.nameAr }));
  }
}
