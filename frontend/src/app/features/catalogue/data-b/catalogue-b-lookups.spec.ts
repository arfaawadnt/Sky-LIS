import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { CATALOGUE_B_API, CATALOGUE_B_LOOKUP_API } from '../../../core/api/catalogue-b.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { CatalogueBApi, fileNameOf, localFileName } from './catalogue-b.api';
import { ListedFilter } from './listed-filter';
import { CatalogueBLookups, optionsOf, recordsOf, toSelectOption } from './catalogue-b-lookups';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

describe('Catalogue B lookups and API helpers', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()] });
    backend = TestBed.inject(HttpTestingController);
  });

  it('reads arrays and Paged envelopes alike and tolerates missing fields', () => {
    expect(recordsOf([{ id: 'a' }, 3, null]).length).toBe(1);
    expect(recordsOf({ items: [{ id: 'a' }, { id: 'b' }], page: 1 }).length).toBe(2);
    expect(recordsOf('nonsense')).toEqual([]);

    const options = optionsOf({ items: [{ id: 's1', shortName: 'CBC', nameEn: 'Blood count', isActive: false }, { code: 'NOID' }] });
    expect(options).toEqual([{ id: 's1', code: 'CBC', nameEn: 'Blood count', nameAr: 'Blood count', inactive: true }]);
    expect(toSelectOption({ id: 'g1', code: 'HEM', nameEn: 'Hematology', nameAr: 'أمراض الدم' })).toEqual({ value: 'g1', label: 'HEM · Hematology', labelAr: 'HEM · أمراض الدم' });
  });

  it('searches services through Catalogue A and tiers through Pricing', async () => {
    const lookups = TestBed.inject(CatalogueBLookups);

    const services = lookups.searchServices(' glu ');
    const serviceRequest = backend.expectOne((request) => request.url === CATALOGUE_B_LOOKUP_API.services);
    expect(serviceRequest.request.params.get('search')).toBe('glu');
    expect(serviceRequest.request.params.get('pageSize')).toBe('50');
    serviceRequest.flush({ items: [{ id: 's1', code: 'GLU', nameEn: 'Glucose', nameAr: 'جلوكوز' }], page: 1, pageSize: 50, totalCount: 1, totalPages: 1 });
    expect((await services).map((service) => service.code)).toEqual(['GLU']);

    const tiers = lookups.tiers();
    const tierRequest = backend.expectOne((request) => request.url === CATALOGUE_B_LOOKUP_API.tierOptions);
    expect(tierRequest.request.params.get('activeOnly')).toBe('true');
    tierRequest.flush([
      { id: 't1', payerId: 'p1', payerNameEn: 'Cash', payerNameAr: 'نقدي', nameEn: 'Default', nameAr: 'افتراضي' },
      { id: 't2', nameEn: 'Orphan' },
    ]);
    expect(await tiers).toEqual([{ id: 't1', payerId: 'p1', payerNameEn: 'Cash', payerNameAr: 'نقدي', nameEn: 'Default', nameAr: 'افتراضي' }]);
    backend.verify();
  });

  it('exports with the filter and saves under the server file name', async () => {
    const api = TestBed.inject(CatalogueBApi);
    const done = api.reasons.exportCsv({ kind: 'Discount', search: '', isActive: null });
    await settle();

    const request = backend.expectOne((candidate) => candidate.url === `${CATALOGUE_B_API.reasons}/export`);
    expect(request.request.params.get('kind')).toBe('Discount');
    expect(request.request.params.has('search')).toBe(false);
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['Kind,Name\r\n'], { type: 'text/csv' }), { headers: { 'Content-Disposition': 'attachment; filename=reasons-20261009-0830.csv' } });
    await done;
    backend.verify();

    expect(fileNameOf('attachment; filename="doctors.csv"; filename*=UTF-8\'\'doctors%20list.csv')).toBe('doctors list.csv');
    expect(fileNameOf('attachment; filename=organisms.csv')).toBe('organisms.csv');
    expect(fileNameOf(null)).toBeNull();
  });

  it('exports in the language of the user and names the file with the local date (UAT B-09)', async () => {
    const api = TestBed.inject(CatalogueBApi);
    const done = api.doctors.exportCsv({ search: 'dr', isActive: true });
    await settle();

    const request = backend.expectOne((candidate) => candidate.url === `${CATALOGUE_B_API.doctors}/export`);
    expect(request.request.params.get('lang')).toBe(TestBed.inject(I18nService).lang());
    expect(request.request.params.get('search')).toBe('dr');
    expect(request.request.params.get('isActive')).toBe('true');
    request.flush(new Blob(['Code'], { type: 'text/csv' }));
    await done;

    const evening = new Date(2026, 9, 11, 23, 30);
    expect(localFileName('doctors-20261011-2130.csv', 'doctors', evening)).toBe('doctors-2026-10-11.csv');
    expect(localFileName(null, 'reasons', evening)).toBe('reasons-2026-10-11.csv');
  });

  it('remembers the filter the register listed with, for its export (UAT C-04)', () => {
    const listed = new ListedFilter();
    expect(listed.query()).toEqual({ search: '', isActive: true });
    expect(listed.track({ search: 'v11', active: null })).toEqual({ search: 'v11', isActive: null });
    expect(listed.query()).toEqual({ search: 'v11', isActive: null });
  });
});
