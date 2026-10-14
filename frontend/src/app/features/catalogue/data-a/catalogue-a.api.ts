import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import {
  CATALOGUE_A_API,
  CATALOGUE_LOOKUP_MAX_PAGE_SIZE,
  type CatalogueListFilter,
  type ClinicalGroupListItem,
  type ClinicalGroupRequest,
  type CreateLabTestRequest,
  type CreateServiceRequest,
  type CreatedResource,
  type FormulaCheckResult,
  type LabTestDetail,
  type LabTestGeneral,
  type LabTestListFilter,
  type LabTestListItem,
  type LabTestTiming,
  type LabTestType,
  type ListValueItem,
  type MeasurementUnitListItem,
  type MeasurementUnitRequest,
  type Paged,
  type ParagraphItem,
  type PatientConditionListItem,
  type PatientConditionRequest,
  type RangeBandItem,
  type SampleConditionItem,
  type SampleTypeDetail,
  type SampleTypeListItem,
  type SampleTypeRequest,
  type ServiceDetail,
  type ServiceListFilter,
  type ServiceListItem,
  type ServiceLookupItem,
  type ServiceMemberItem,
  type ServiceSequenceChange,
  type ServiceSequenceRow,
  type ServiceType,
  type TestLookupItem,
  type UpdateServiceRequest,
} from '../../../core/api/catalogue-a.contracts';
import { emptyPage } from '../../../core/api/paging';
import { toHttpParams } from '../../../shared/forms/paging';

/** The largest list the editors load for their selects (server page limit). */
export const OPTIONS_PAGE_SIZE = 200;

/** Catalogue B's report templates (the half-B module owns the endpoint). */
const REPORT_TEMPLATES_URL = '/api/v1/catalogue/report-templates';

/** The id and names of a row of another list, as a select needs them. */
export interface NamedOption {
  readonly id: string;
  readonly nameEn: string;
  readonly nameAr: string;
}

/** Typed calls of catalogue half A; URLs are built with `HttpParams`, never by concatenating user input (pitfall P-60). */
@Injectable({ providedIn: 'root' })
export class CatalogueAApi {
  private readonly http = inject(HttpClient);

  // ---- clinical groups (CP-39)

  listClinicalGroups(filter: CatalogueListFilter, signal?: AbortSignal): Promise<Paged<ClinicalGroupListItem>> {
    return this.page<ClinicalGroupListItem>(CATALOGUE_A_API.clinicalGroups, listParams(filter), signal);
  }

  /** The rows of the list's current search and status filter (UAT C-04: the export is the grid). */
  exportClinicalGroups(filter: CatalogueExportFilter): Promise<readonly ClinicalGroupListItem[]> {
    return this.all<ClinicalGroupListItem>(`${CATALOGUE_A_API.clinicalGroups}/export`, exportParams(filter));
  }

  createClinicalGroup(request: ClinicalGroupRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(CATALOGUE_A_API.clinicalGroups, request));
  }

  updateClinicalGroup(id: string, request: Omit<ClinicalGroupRequest, 'code'>): Promise<void> {
    return this.put(`${CATALOGUE_A_API.clinicalGroups}/${encodeURIComponent(id)}`, request);
  }

  setClinicalGroupActive(id: string, active: boolean): Promise<void> {
    return this.activation(CATALOGUE_A_API.clinicalGroups, id, active);
  }

  // ---- sample types and conditions (CP-43)

  listSampleTypes(filter: CatalogueListFilter, signal?: AbortSignal): Promise<Paged<SampleTypeListItem>> {
    return this.page<SampleTypeListItem>(CATALOGUE_A_API.sampleTypes, listParams(filter), signal);
  }

  exportSampleTypes(filter: CatalogueExportFilter): Promise<readonly SampleTypeListItem[]> {
    return this.all<SampleTypeListItem>(`${CATALOGUE_A_API.sampleTypes}/export`, exportParams(filter));
  }

  getSampleType(id: string): Promise<SampleTypeDetail> {
    return firstValueFrom(this.http.get<SampleTypeDetail>(`${CATALOGUE_A_API.sampleTypes}/${encodeURIComponent(id)}`));
  }

  createSampleType(request: SampleTypeRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(CATALOGUE_A_API.sampleTypes, { ...request, conditions: [] }));
  }

  updateSampleType(id: string, request: Omit<SampleTypeRequest, 'code'>): Promise<void> {
    return this.put(`${CATALOGUE_A_API.sampleTypes}/${encodeURIComponent(id)}`, request);
  }

  saveSampleConditions(id: string, conditions: readonly SampleConditionItem[]): Promise<void> {
    return this.put(`${CATALOGUE_A_API.sampleTypes}/${encodeURIComponent(id)}/conditions`, { conditions });
  }

  setSampleTypeActive(id: string, active: boolean): Promise<void> {
    return this.activation(CATALOGUE_A_API.sampleTypes, id, active);
  }

  // ---- units (CP-44) and patient conditions (CP-45)

  listUnits(filter: CatalogueListFilter, signal?: AbortSignal): Promise<Paged<MeasurementUnitListItem>> {
    return this.page<MeasurementUnitListItem>(CATALOGUE_A_API.units, listParams(filter), signal);
  }

  exportUnits(filter: CatalogueExportFilter): Promise<readonly MeasurementUnitListItem[]> {
    return this.all<MeasurementUnitListItem>(`${CATALOGUE_A_API.units}/export`, exportParams(filter));
  }

  createUnit(request: MeasurementUnitRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(CATALOGUE_A_API.units, request));
  }

  updateUnit(id: string, request: MeasurementUnitRequest): Promise<void> {
    return this.put(`${CATALOGUE_A_API.units}/${encodeURIComponent(id)}`, request);
  }

  setUnitActive(id: string, active: boolean): Promise<void> {
    return this.activation(CATALOGUE_A_API.units, id, active);
  }

  listPatientConditions(filter: CatalogueListFilter, signal?: AbortSignal): Promise<Paged<PatientConditionListItem>> {
    return this.page<PatientConditionListItem>(CATALOGUE_A_API.patientConditions, listParams(filter), signal);
  }

  exportPatientConditions(filter: CatalogueExportFilter): Promise<readonly PatientConditionListItem[]> {
    return this.all<PatientConditionListItem>(`${CATALOGUE_A_API.patientConditions}/export`, exportParams(filter));
  }

  createPatientCondition(request: PatientConditionRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(CATALOGUE_A_API.patientConditions, request));
  }

  updatePatientCondition(id: string, request: PatientConditionRequest): Promise<void> {
    return this.put(`${CATALOGUE_A_API.patientConditions}/${encodeURIComponent(id)}`, request);
  }

  setPatientConditionActive(id: string, active: boolean): Promise<void> {
    return this.activation(CATALOGUE_A_API.patientConditions, id, active);
  }

  // ---- services (CP-40, CP-42, CP-55)

  listServices(filter: ServiceListFilter, signal?: AbortSignal): Promise<Paged<ServiceListItem>> {
    return this.page<ServiceListItem>(CATALOGUE_A_API.services, { ...listParams(filter), type: filter.type, clinicalGroupId: filter.clinicalGroupId }, signal);
  }

  exportServices(filter: CatalogueExportFilter, type: ServiceType | null, clinicalGroupId: string | null): Promise<readonly ServiceListItem[]> {
    return this.all<ServiceListItem>(`${CATALOGUE_A_API.services}/export`, { ...exportParams(filter), type, clinicalGroupId });
  }

  getService(id: string): Promise<ServiceDetail> {
    return firstValueFrom(this.http.get<ServiceDetail>(`${CATALOGUE_A_API.services}/${encodeURIComponent(id)}`));
  }

  createService(request: CreateServiceRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(CATALOGUE_A_API.services, request));
  }

  updateService(id: string, request: UpdateServiceRequest): Promise<void> {
    return this.put(`${CATALOGUE_A_API.services}/${encodeURIComponent(id)}`, request);
  }

  replaceServiceMembers(id: string, members: readonly ServiceMemberItem[]): Promise<void> {
    return this.put(`${CATALOGUE_A_API.services}/${encodeURIComponent(id)}/members`, { members });
  }

  setServiceActive(id: string, active: boolean): Promise<void> {
    return this.activation(CATALOGUE_A_API.services, id, active);
  }

  listServiceSequence(search: string, type: ServiceType | null, signal?: AbortSignal): Promise<readonly ServiceSequenceRow[]> {
    return firstValueFrom(this.http.get<readonly ServiceSequenceRow[]>(CATALOGUE_A_API.serviceSequence, { params: toHttpParams({ search, type }) }), { defaultValue: [] }).then(abortable(signal));
  }

  saveServiceSequence(items: readonly ServiceSequenceChange[]): Promise<void> {
    return this.put(CATALOGUE_A_API.serviceSequence, { items });
  }

  lookupServices(search: string, type: ServiceType | null, signal?: AbortSignal): Promise<Paged<ServiceLookupItem>> {
    return this.page<ServiceLookupItem>(CATALOGUE_A_API.lookupServices, { search, type, page: 1, pageSize: CATALOGUE_LOOKUP_MAX_PAGE_SIZE }, signal);
  }

  // ---- tests (CP-41)

  listTests(filter: LabTestListFilter, signal?: AbortSignal): Promise<Paged<LabTestListItem>> {
    return this.page<LabTestListItem>(CATALOGUE_A_API.tests, { ...listParams(filter), type: filter.type, clinicalGroupId: filter.clinicalGroupId }, signal);
  }

  exportTests(filter: CatalogueExportFilter, type: LabTestType | null, clinicalGroupId: string | null): Promise<readonly LabTestListItem[]> {
    return this.all<LabTestListItem>(`${CATALOGUE_A_API.tests}/export`, { ...exportParams(filter), type, clinicalGroupId });
  }

  getTest(id: string): Promise<LabTestDetail> {
    return firstValueFrom(this.http.get<LabTestDetail>(`${CATALOGUE_A_API.tests}/${encodeURIComponent(id)}`));
  }

  createTest(request: CreateLabTestRequest): Promise<CreatedResource> {
    return firstValueFrom(this.http.post<CreatedResource>(CATALOGUE_A_API.tests, request));
  }

  updateTest(id: string, request: LabTestGeneral): Promise<void> {
    return this.put(`${CATALOGUE_A_API.tests}/${encodeURIComponent(id)}`, request);
  }

  setTestTiming(id: string, timing: LabTestTiming): Promise<void> {
    return this.put(`${CATALOGUE_A_API.tests}/${encodeURIComponent(id)}/timing`, timing);
  }

  replaceRangeBands(id: string, bands: readonly RangeBandItem[]): Promise<void> {
    return this.put(`${CATALOGUE_A_API.tests}/${encodeURIComponent(id)}/range-bands`, { bands });
  }

  replaceListValues(id: string, values: readonly ListValueItem[]): Promise<void> {
    return this.put(`${CATALOGUE_A_API.tests}/${encodeURIComponent(id)}/list-values`, { values });
  }

  setFormula(id: string, expression: string | null): Promise<void> {
    return this.put(`${CATALOGUE_A_API.tests}/${encodeURIComponent(id)}/formula`, { expression });
  }

  replaceParagraphs(id: string, paragraphs: readonly ParagraphItem[]): Promise<void> {
    return this.put(`${CATALOGUE_A_API.tests}/${encodeURIComponent(id)}/paragraphs`, { paragraphs });
  }

  /** The services that carry the test after the call (CP-41 "parent services"): it joins the new ones and leaves the omitted ones. */
  setParentServices(id: string, serviceIds: readonly string[]): Promise<void> {
    return this.put(`${CATALOGUE_A_API.tests}/${encodeURIComponent(id)}/parent-services`, { serviceIds });
  }

  checkFormula(expression: string, testId: string | null): Promise<FormulaCheckResult> {
    return firstValueFrom(this.http.post<FormulaCheckResult>(CATALOGUE_A_API.formulaCheck, { expression, testId }));
  }

  setTestActive(id: string, active: boolean): Promise<void> {
    return this.activation(CATALOGUE_A_API.tests, id, active);
  }

  lookupTests(search: string, type: LabTestType | null, signal?: AbortSignal): Promise<Paged<TestLookupItem>> {
    return this.page<TestLookupItem>(CATALOGUE_A_API.lookupTests, { search, type, page: 1, pageSize: CATALOGUE_LOOKUP_MAX_PAGE_SIZE }, signal);
  }

  /**
   * Report templates of catalogue B (`GET /api/v1/catalogue/report-templates`, same wave) for the clinical-group form;
   * only the id and names are read, so the two halves stay independent. An unavailable list yields no options.
   */
  listReportTemplateOptions(): Promise<readonly NamedOption[]> {
    return this.page<NamedOption>(REPORT_TEMPLATES_URL, { isActive: true, page: 1, pageSize: OPTIONS_PAGE_SIZE }).then((page) => page.items);
  }

  // ---- helpers

  private page<T>(url: string, params: Readonly<Record<string, string | number | boolean | null>>, signal?: AbortSignal): Promise<Paged<T>> {
    return firstValueFrom(this.http.get<Paged<T>>(url, { params: toHttpParams(params) }), { defaultValue: emptyPage<T>() }).then(abortable(signal));
  }

  private all<T>(url: string, params: Readonly<Record<string, string | number | boolean | null>> = {}): Promise<readonly T[]> {
    return firstValueFrom(this.http.get<readonly T[]>(url, { params: toHttpParams(params) }), { defaultValue: [] });
  }

  /** A 204 arrives as a null body; it resolves to undefined so a mutation's null still means failure. */
  private put(url: string, body: unknown): Promise<void> {
    return firstValueFrom(this.http.put<void>(url, body), { defaultValue: undefined }).then(() => undefined);
  }

  private activation(url: string, id: string, active: boolean): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${url}/${encodeURIComponent(id)}/${active ? 'reactivate' : 'deactivate'}`, null), { defaultValue: undefined }).then(() => undefined);
  }
}

/** What an export is narrowed by: the list's search text and status (`null` = all). */
export interface CatalogueExportFilter {
  readonly search: string;
  readonly isActive: boolean | null;
}

function exportParams(filter: CatalogueExportFilter): Record<string, string | boolean | null> {
  return { search: filter.search.trim() === '' ? null : filter.search.trim(), isActive: filter.isActive };
}

function listParams(filter: CatalogueListFilter): Record<string, string | number | boolean | null> {
  return { search: filter.search, isActive: filter.isActive, page: filter.page, pageSize: filter.pageSize, sort: filter.sort ?? null, direction: filter.direction ?? null };
}

/** Resources pass an abort signal; a late result for an aborted load is dropped. */
function abortable<T>(signal: AbortSignal | undefined): (value: T) => T {
  return (value) => {
    if (signal?.aborted) {
      throw new DOMException('aborted', 'AbortError');
    }

    return value;
  };
}
