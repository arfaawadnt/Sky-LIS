import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { emptyPage, type Paged } from '../../core/api/paging';
import {
  type AreaOption,
  type CityOption,
  type ConditionOption,
  type CountryOption,
  type DistrictOption,
  type CreatedPatient,
  type GovernorateOption,
  PATIENTS_API,
  type PatientDemographicsLock,
  type PatientDetail,
  type PatientFormSettings,
  type PatientListItem,
  type RegisterPatientRequest,
  type SimilarPatientsResponse,
  type UpdatePatientRequest,
} from '../../core/api/patients.contracts';

const DEFAULT_SETTINGS: PatientFormSettings = { mandatoryMobile: false, mandatoryNationalId: false, similarNameWarning: true };

/** What the similar-patient lookup (BR-01) is asked with: the typed name, mobile and (C3) second phone; blanks are not sent. */
export interface SimilarQuery {
  readonly name: string;
  readonly mobile: string;
  readonly secondPhone?: string;
}

/**
 * The calls a patient search/create form needs: search, similar candidates (BR-01), form settings, register, and the
 * reference lists of other modules read tolerantly (an absent or failing list is an empty one, so the form degrades to
 * free text instead of breaking). Used by the patient picker and the patients pages; moves to `shared/` with the
 * picker. Reference lists are fetched once per session.
 */
@Injectable({ providedIn: 'root' })
export class PatientLookupApi {
  private readonly http = inject(HttpClient);
  private countriesCache: Promise<readonly CountryOption[]> | null = null;
  private governoratesCache: Promise<readonly GovernorateOption[]> | null = null;
  private conditionsCache: Promise<readonly ConditionOption[]> | null = null;
  private readonly citiesCache = new Map<string, Promise<readonly CityOption[]>>();
  private readonly districtsCache = new Map<string, Promise<readonly DistrictOption[]>>();
  private readonly areasCache = new Map<string, Promise<readonly AreaOption[]>>();

  /** Active files matching a number, name, mobile or national id, newest first. */
  search(q: string, pageSize: number, signal?: AbortSignal): Promise<Paged<PatientListItem>> {
    const params = new HttpParams({ fromObject: { q, status: 'Active', page: 1, pageSize } });
    return firstValueFrom(this.http.get<Paged<PatientListItem>>(PATIENTS_API.patients, { params }), { defaultValue: emptyPage<PatientListItem>() }).then(abortable(signal));
  }

  similar(query: SimilarQuery, signal?: AbortSignal): Promise<SimilarPatientsResponse> {
    const fromObject: Record<string, string> = {};
    if (query.name.trim() !== '') {
      fromObject['name'] = query.name.trim();
    }

    if (query.mobile.trim() !== '') {
      fromObject['mobile'] = query.mobile.trim();
    }

    const secondPhone = query.secondPhone?.trim() ?? '';
    if (secondPhone !== '') {
      fromObject['secondPhone'] = secondPhone;
    }

    if (Object.keys(fromObject).length === 0) {
      return Promise.resolve({ warningEnabled: true, candidates: [] });
    }

    return firstValueFrom(this.http.get<SimilarPatientsResponse>(`${PATIENTS_API.patients}/similar`, { params: new HttpParams({ fromObject }) }), {
      defaultValue: { warningEnabled: true, candidates: [] },
    }).then(abortable(signal));
  }

  formSettings(signal?: AbortSignal): Promise<PatientFormSettings> {
    return firstValueFrom(this.http.get<PatientFormSettings>(`${PATIENTS_API.patients}/form-settings`), { defaultValue: DEFAULT_SETTINGS })
      .catch(() => DEFAULT_SETTINGS)
      .then(abortable(signal));
  }

  get(id: string, signal?: AbortSignal): Promise<PatientDetail> {
    return firstValueFrom(this.http.get<PatientDetail>(`${PATIENTS_API.patients}/${encodeURIComponent(id)}`)).then(abortable(signal));
  }

  register(request: RegisterPatientRequest): Promise<CreatedPatient> {
    return firstValueFrom(this.http.post<CreatedPatient>(PATIENTS_API.patients, request));
  }

  /** T2 from any page that edits a file (the patient file, Search registrations with its `visitId`); a 204 resolves to `undefined`. */
  update(id: string, request: UpdatePatientRequest): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${PATIENTS_API.patients}/${encodeURIComponent(id)}`, request), { defaultValue: undefined }).then(() => undefined);
  }

  /** The demographics lock the form shows before the user types (PAT-032): judged on `visitId` when given, on the whole file otherwise. */
  demographicsLock(id: string, visitId: string | null, signal?: AbortSignal): Promise<PatientDemographicsLock> {
    const params = visitId === null ? new HttpParams() : new HttpParams({ fromObject: { visitId } });
    return firstValueFrom(this.http.get<PatientDemographicsLock>(`${PATIENTS_API.patients}/${encodeURIComponent(id)}/demographics-lock`, { params })).then(abortable(signal));
  }

  countries(): Promise<readonly CountryOption[]> {
    this.countriesCache ??= this.list(PATIENTS_API.countries, toCountry);
    return this.countriesCache;
  }

  governorates(): Promise<readonly GovernorateOption[]> {
    this.governoratesCache ??= this.list(PATIENTS_API.governorates, toGovernorate);
    return this.governoratesCache;
  }

  conditions(): Promise<readonly ConditionOption[]> {
    this.conditionsCache ??= this.list(PATIENTS_API.conditions, toCondition);
    return this.conditionsCache;
  }

  /** The active cities of a governorate (A2; empty when the platform lists none, so the form keeps its free-text city). */
  cities(governorateId: string): Promise<readonly CityOption[]> {
    return cached(this.citiesCache, governorateId, () => this.list(PATIENTS_API.cities, toCity, { governorateId }));
  }

  districts(cityId: string): Promise<readonly DistrictOption[]> {
    return cached(this.districtsCache, cityId, () => this.list(PATIENTS_API.districts, toDistrict, { cityId }));
  }

  areas(districtId: string): Promise<readonly AreaOption[]> {
    return cached(this.areasCache, districtId, () => this.list(PATIENTS_API.areas, toArea, { districtId }));
  }

  private list<T>(url: string, map: (value: Record<string, unknown>) => T | null, extra: Record<string, string | number | boolean> = {}): Promise<readonly T[]> {
    const params = new HttpParams({ fromObject: { ...extra, page: 1, pageSize: 200 } });
    return firstValueFrom(this.http.get<unknown>(url, { params }), { defaultValue: [] })
      .then((body) => itemsOf(body).map(map).filter((item): item is T => item !== null))
      .catch((): readonly T[] => []);
  }
}

/** A list endpoint may answer an array or a `Paged<T>`; anything else is empty. */
export function itemsOf(body: unknown): Record<string, unknown>[] {
  const items: unknown = Array.isArray(body) ? body : isRecord(body) ? body['items'] : null;
  return Array.isArray(items) ? items.filter(isRecord) : [];
}

function toCountry(value: Record<string, unknown>): CountryOption | null {
  const code = text(value['code']) ?? text(value['isoCode']) ?? text(value['iso2']);
  const nameEn = text(value['nameEn']) ?? text(value['name']) ?? code;
  if (code === null || nameEn === null || value['isActive'] === false) {
    return null;
  }

  return { id: text(value['id']) ?? code, code, nameEn, nameAr: text(value['nameAr']) ?? nameEn, mobileLength: count(value['mobileLength']) ?? count(value['mobileDigits']), dialCode: text(value['dialCode']) };
}

function toGovernorate(value: Record<string, unknown>): GovernorateOption | null {
  const id = text(value['id']);
  const nameEn = text(value['nameEn']) ?? text(value['name']);
  if (id === null || nameEn === null || value['isActive'] === false) {
    return null;
  }

  return { id, nameEn, nameAr: text(value['nameAr']) ?? nameEn, countryId: text(value['countryId']) };
}

function toCity(value: Record<string, unknown>): CityOption | null {
  const id = text(value['id']);
  const governorateId = text(value['governorateId']);
  const nameEn = text(value['nameEn']);
  return id === null || governorateId === null || nameEn === null ? null : { id, governorateId, nameEn, nameAr: text(value['nameAr']) ?? nameEn };
}

function toDistrict(value: Record<string, unknown>): DistrictOption | null {
  const id = text(value['id']);
  const cityId = text(value['cityId']);
  const nameEn = text(value['nameEn']);
  return id === null || cityId === null || nameEn === null ? null : { id, cityId, nameEn, nameAr: text(value['nameAr']) ?? nameEn };
}

function toArea(value: Record<string, unknown>): AreaOption | null {
  const id = text(value['id']);
  const districtId = text(value['districtId']);
  const nameEn = text(value['nameEn']);
  return id === null || districtId === null || nameEn === null ? null : { id, districtId, nameEn, nameAr: text(value['nameAr']) ?? nameEn };
}

/** One request per parent for the session; a failed one is forgotten so the next form asks again. */
function cached<T>(cache: Map<string, Promise<readonly T[]>>, key: string, load: () => Promise<readonly T[]>): Promise<readonly T[]> {
  let pending = cache.get(key);
  if (pending === undefined) {
    pending = load().then((items) => {
      if (items.length === 0) {
        cache.delete(key);   // "no cities yet" is re-asked: the platform may add some while the session runs
      }

      return items;
    });
    cache.set(key, pending);
  }

  return pending;
}

function toCondition(value: Record<string, unknown>): ConditionOption | null {
  const id = text(value['id']);
  const nameEn = text(value['nameEn']) ?? text(value['name']);
  if (id === null || nameEn === null || value['isActive'] === false) {
    return null;
  }

  return { id, nameEn, nameAr: text(value['nameAr']) ?? nameEn };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function text(value: unknown): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value : null;
}

function count(value: unknown): number | null {
  return typeof value === 'number' && Number.isInteger(value) && value > 0 ? value : null;
}

/** Resources pass an abort signal; HttpClient has no fetch-level abort here, so a late result for an aborted load is dropped. */
export function abortable<T>(signal: AbortSignal | undefined): (value: T) => T {
  return (value) => {
    if (signal?.aborted) {
      throw new DOMException('aborted', 'AbortError');
    }

    return value;
  };
}
