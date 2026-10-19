import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { PATIENTS_API } from '../../core/api/patients.contracts';
import { PatientLookupApi } from './patient-lookup.api';

describe('PatientLookupApi (public lookups and the similar search, LIS-CR-003 §3)', () => {
  let api: PatientLookupApi;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(PatientLookupApi);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('reads the cities of a governorate once per session and keeps asking while the platform lists none', async () => {
    const first = api.cities('g-cairo');
    const request = backend.expectOne((r) => r.url === PATIENTS_API.cities && r.params.get('governorateId') === 'g-cairo');
    request.flush([{ id: 'c-1', governorateId: 'g-cairo', nameEn: 'Nasr City', nameAr: 'مدينة نصر' }, { id: 'x', nameEn: 'no parent' }]);
    expect(await first).toEqual([{ id: 'c-1', governorateId: 'g-cairo', nameEn: 'Nasr City', nameAr: 'مدينة نصر' }]);

    expect(await api.cities('g-cairo')).toHaveLength(1);   // cached: no second request
    backend.expectNone((r) => r.url === PATIENTS_API.cities);

    const empty = api.cities('g-aswan');
    backend.expectOne((r) => r.params.get('governorateId') === 'g-aswan').flush([]);
    expect(await empty).toEqual([]);
    const again = api.cities('g-aswan');
    backend.expectOne((r) => r.params.get('governorateId') === 'g-aswan').flush([]);   // "none yet" is asked again
    expect(await again).toEqual([]);
  });

  it('reads districts by city and areas by district, tolerating a failing endpoint as an empty list', async () => {
    const districts = api.districts('c-1');
    backend.expectOne((r) => r.url === PATIENTS_API.districts && r.params.get('cityId') === 'c-1').flush([{ id: 'd-1', cityId: 'c-1', nameEn: '7th', nameAr: 'السابع' }]);
    expect(await districts).toEqual([{ id: 'd-1', cityId: 'c-1', nameEn: '7th', nameAr: 'السابع' }]);

    const areas = api.areas('d-1');
    backend.expectOne((r) => r.url === PATIENTS_API.areas && r.params.get('districtId') === 'd-1').flush('down', { status: 500, statusText: 'Server Error' });
    expect(await areas).toEqual([]);
  });

  it('sends the second phone to the similar search beside the name and the mobile, never blanks', async () => {
    const pending = api.similar({ name: ' Mona ', mobile: '', secondPhone: '01112223333' });
    const request = backend.expectOne((r) => r.url === `${PATIENTS_API.patients}/similar`);
    expect(request.request.params.get('name')).toBe('Mona');
    expect(request.request.params.has('mobile')).toBe(false);
    expect(request.request.params.get('secondPhone')).toBe('01112223333');
    request.flush({ warningEnabled: true, candidates: [] });
    expect(await pending).toEqual({ warningEnabled: true, candidates: [] });

    expect(await api.similar({ name: '', mobile: ' ' })).toEqual({ warningEnabled: true, candidates: [] });
    backend.expectNone((r) => r.url === `${PATIENTS_API.patients}/similar`);
  });
});
