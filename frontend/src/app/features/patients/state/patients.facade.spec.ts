import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { PATIENTS_API, type Paged, type PatientListItem, type RegisterPatientRequest, type SimilarPatient } from '../../../core/api/patients.contracts';
import { PatientsFacade } from './patients.facade';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function page(items: PatientListItem[]): Paged<PatientListItem> {
  return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 };
}

const candidate: SimilarPatient = {
  id: 'p-1',
  number: 'PAT-2026-00001',
  title: 'Mr',
  fullName: 'Mohamed Ahmed Ali',
  nameAr: null,
  gender: 'Male',
  dateOfBirth: '1990-01-01',
  dateOfBirthIsEstimated: false,
  age: { years: 36, months: 9, days: 8 },
  mobile: '01001234567',
  nationalId: null,
  match: 'Exact',
};

const request: RegisterPatientRequest = {
  title: 'Mrs',
  fullName: 'Mona Hassan',
  nameAr: null,
  gender: 'Female',
  dateOfBirth: '1990-05-01',
  age: null,
  patientConditionId: null,
  mobile: '01001234567',
  nationalId: null,
  nationality: null,
  email: null,
  governorateId: null,
  city: null,
  address: null,
  comments: null,
  isVip: null,
};

describe('PatientsFacade (01 §6.3)', () => {
  let facade: PatientsFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), PatientsFacade] });
    facade = TestBed.inject(PatientsFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  /** Runs the resource effects (which issue the HTTP calls) and lets the promises settle. */
  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  /** The first load issues the register and the reference reads; the reference lists are fine to fail (degrade to free text). */
  async function initialLoad(): Promise<void> {
    await runEffects();
    backend.expectOne((r) => r.url === PATIENTS_API.patients).flush(page([]));
    backend.expectOne(`${PATIENTS_API.patients}/form-settings`).flush({ mandatoryMobile: false, mandatoryNationalId: false, similarNameWarning: true });
    for (const url of [PATIENTS_API.countries, PATIENTS_API.governorates, PATIENTS_API.conditions]) {
      backend.match((r) => r.url === url).forEach((call) => call.flush({ code: 'HTTP-404' }, { status: 404, statusText: 'Not Found' }));
    }

    await runEffects();
  }

  it('loads active files first and filters on search, gender and VIP with the page reset', async () => {
    await runEffects();
    const first = backend.expectOne((r) => r.url === PATIENTS_API.patients);
    expect(first.request.params.get('status')).toBe('Active');
    expect(first.request.params.has('q')).toBe(false);
    expect(first.request.params.has('vip')).toBe(false);
    first.flush(page([]));
    backend.match(() => true).forEach((call) => call.flush([]));

    facade.setPage({ page: 3, pageSize: 20 });
    facade.setSearch('mona');
    facade.setGender('Female');
    facade.setVip(true);
    await runEffects();

    const filtered = backend.expectOne((r) => r.url === PATIENTS_API.patients);
    expect(filtered.request.params.get('q')).toBe('mona');
    expect(filtered.request.params.get('gender')).toBe('Female');
    expect(filtered.request.params.get('vip')).toBe('true');
    expect(filtered.request.params.get('page')).toBe('1');
    expect(filtered.request.params.get('pageSize')).toBe('20');
    filtered.flush(page([]));
    backend.verify();
  });

  it('registers a file and reloads the register', async () => {
    await initialLoad();

    const pending = facade.register.execute(request);
    await settle();
    const post = backend.expectOne((r) => r.method === 'POST' && r.url === PATIENTS_API.patients);
    expect(post.request.body).toEqual(request);
    post.flush({ id: 'new-1', number: 'PAT-2026-00002' }, { status: 201, statusText: 'Created' });
    expect(await pending).toEqual({ id: 'new-1', number: 'PAT-2026-00002' });

    await runEffects();
    backend.expectOne((r) => r.url === PATIENTS_API.patients).flush(page([]));
    backend.verify();
  });

  it('asks for similar files while typing and warns only when the tenant enabled the warning', async () => {
    await initialLoad();

    facade.onTyped({ name: 'm', mobile: '' });
    await runEffects();
    backend.expectNone((r) => r.url.endsWith('/similar'));

    facade.onTyped({ name: 'mohamed ahmed', mobile: '01001234567' });
    await runEffects();
    const similar = backend.expectOne((r) => r.url === `${PATIENTS_API.patients}/similar`);
    expect(similar.request.params.get('name')).toBe('mohamed ahmed');
    expect(similar.request.params.get('mobile')).toBe('01001234567');
    similar.flush({ warningEnabled: true, candidates: [candidate] });
    await runEffects();
    expect(facade.candidates()).toEqual([candidate]);

    facade.onTyped({ name: 'mohamed ali', mobile: '' });
    await runEffects();
    backend.expectOne((r) => r.url === `${PATIENTS_API.patients}/similar`).flush({ warningEnabled: false, candidates: [candidate] });
    await runEffects();
    expect(facade.candidates()).toEqual([]);
    backend.verify();
  });

  it('sends the row version with an update and keeps the problem of a refusal', async () => {
    await initialLoad();

    const pending = facade.update.execute({ id: 'p-1', request: { ...request, rowVersion: 42 } });
    await settle();
    const put = backend.expectOne(`${PATIENTS_API.patients}/p-1`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ ...request, rowVersion: 42 });
    put.flush({ code: 'CONCURRENCY-409', title: 'stale', status: 409 }, { status: 409, statusText: 'Conflict' });

    expect(await pending).toBeNull();
    expect(facade.update.pending()).toBe(false);
  });
});
