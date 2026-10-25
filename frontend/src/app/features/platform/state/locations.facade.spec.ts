import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { type GovernorateListItem, type LocationListItem, PLATFORM_API, PLATFORM_LOCATIONS_API } from '../../../core/api/platform.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { LocationsFacade } from './locations.facade';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const cairo: GovernorateListItem = { id: 'g-cairo', countryId: 'c-eg', countryNameEn: 'Egypt', countryNameAr: 'مصر', nameEn: 'Cairo', nameAr: 'القاهرة', district: null, remarks: null, isActive: true };

const nasr: LocationListItem = { id: 'c-nasr', parentId: 'g-cairo', nameEn: 'Nasr City', nameAr: 'مدينة نصر', isActive: true };
const seventh: LocationListItem = { id: 'd-7', parentId: 'c-nasr', nameEn: '7th district', nameAr: 'الحي السابع', isActive: true };

describe('LocationsFacade (SA-11b Locations, LIS-CR-003 §3)', () => {
  let facade: LocationsFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(withInterceptors([problemDetailsInterceptor])), provideHttpClientTesting(), LocationsFacade],
    });
    facade = TestBed.inject(LocationsFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  async function openCairo(): Promise<void> {
    facade.open(cairo);
    TestBed.tick();
    await settle();
    const request = backend.expectOne((r) => r.url === `${PLATFORM_API.governorates}/g-cairo/cities`);
    expect(request.request.params.get('isActive')).toBe('true');   // active rows only until "Show inactive"
    request.flush([nasr]);
    await settle();
  }

  it('loads the cities of the opened governorate, then the districts of the selected city and the areas of the selected district', async () => {
    await openCairo();
    expect(facade.cities.value()).toEqual([nasr]);
    expect(facade.parentOf('districts')).toBeNull();
    backend.expectNone((r) => r.url.endsWith('/districts'));

    facade.select('cities', 'c-nasr');
    TestBed.tick();
    await settle();
    backend.expectOne((r) => r.url === `${PLATFORM_LOCATIONS_API.districts.parents}/c-nasr/districts`).flush([seventh]);
    await settle();
    expect(facade.selectedCity()).toEqual(nasr);
    expect(facade.districts.value()).toEqual([seventh]);

    facade.select('districts', 'd-7');
    TestBed.tick();
    await settle();
    backend.expectOne((r) => r.url === `${PLATFORM_LOCATIONS_API.areas.parents}/d-7/areas`).flush([]);
    await settle();
    expect(facade.parentOf('areas')).toBe('d-7');

    // another city forgets the district and its areas
    facade.select('cities', 'c-other');
    TestBed.tick();
    await settle();
    expect(facade.districtId()).toBeNull();
    backend.expectOne((r) => r.url === `${PLATFORM_LOCATIONS_API.districts.parents}/c-other/districts`).flush([]);
    await settle();
  });

  it('searches a column on the server and includes inactive rows on request', async () => {
    await openCairo();
    facade.setFilter('cities', { search: ' nas ' });
    TestBed.tick();
    await settle();
    const searched = backend.expectOne((r) => r.url === `${PLATFORM_API.governorates}/g-cairo/cities`);
    expect(searched.request.params.get('search')).toBe('nas');
    searched.flush([nasr]);
    await settle();

    facade.setFilter('cities', { showInactive: true });
    TestBed.tick();
    await settle();
    const all = backend.expectOne((r) => r.url === `${PLATFORM_API.governorates}/g-cairo/cities`);
    expect(all.request.params.has('isActive')).toBe(false);
    all.flush([nasr, { ...nasr, id: 'c-old', nameEn: 'Old town', isActive: false }]);
    await settle();
    expect(facade.cities.value()?.length).toBe(2);
  });

  it('creates and renames through the level endpoints and reloads that level', async () => {
    await openCairo();
    const creating = facade.save.execute({ level: 'cities', parentId: 'g-cairo', id: null, request: { nameEn: 'Maadi', nameAr: 'المعادي' } });
    await settle();
    const post = backend.expectOne({ method: 'POST', url: `${PLATFORM_API.governorates}/g-cairo/cities` });
    expect(post.request.body).toEqual({ nameEn: 'Maadi', nameAr: 'المعادي' });
    post.flush({ id: 'c-maadi' }, { status: 201, statusText: 'Created' });
    await creating;
    TestBed.tick();
    await settle();
    backend.expectOne((r) => r.url === `${PLATFORM_API.governorates}/g-cairo/cities`).flush([nasr, { ...nasr, id: 'c-maadi', nameEn: 'Maadi' }]);
    await settle();
    expect(facade.cities.value()?.length).toBe(2);

    const renaming = facade.save.execute({ level: 'cities', parentId: 'g-cairo', id: 'c-maadi', request: { nameEn: 'El Maadi', nameAr: 'المعادي' } });
    await settle();
    backend.expectOne({ method: 'PUT', url: `${PLATFORM_LOCATIONS_API.cities.items}/c-maadi` }).flush(null, { status: 204, statusText: 'No Content' });
    await renaming;
    TestBed.tick();
    await settle();
    backend.expectOne((r) => r.url === `${PLATFORM_API.governorates}/g-cairo/cities`).flush([nasr]);
    await settle();
    expect(facade.save.problem()).toBeNull();
  });

  it('deactivates and reactivates a row, and keeps a refusal on the level it addressed', async () => {
    await openCairo();
    const deactivating = facade.toggle.execute({ level: 'cities', id: 'c-nasr', active: false });
    await settle();
    backend.expectOne({ method: 'POST', url: `${PLATFORM_LOCATIONS_API.cities.items}/c-nasr/deactivate` }).flush(
      { type: 'about:blank', title: 'Conflict', status: 409, code: 'GEO-112', detail: 'active children' },
      { status: 409, statusText: 'Conflict', headers: { 'content-type': 'application/problem+json' } },
    );
    expect(await deactivating).toBeNull();
    expect(facade.toggle.problem()?.code).toBe('GEO-112');
    expect(facade.activeLevel()).toBe('cities');
    backend.expectNone((r) => r.url === `${PLATFORM_API.governorates}/g-cairo/cities`);   // nothing changed: no reload

    const reactivating = facade.toggle.execute({ level: 'cities', id: 'c-old', active: true });
    await settle();
    backend.expectOne({ method: 'POST', url: `${PLATFORM_LOCATIONS_API.cities.items}/c-old/reactivate` }).flush(null, { status: 204, statusText: 'No Content' });
    await reactivating;
    TestBed.tick();
    await settle();
    backend.expectOne((r) => r.url === `${PLATFORM_API.governorates}/g-cairo/cities`).flush([nasr]);
    await settle();
    expect(facade.toggle.problem()).toBeNull();
  });

  it('LIS-CR-008 C-04: imports a locality file — a dry run writes nothing, an applied run reloads the columns', async () => {
    await openCairo();
    const file = new File(['city_en,city_ar\nShubra,شبرا\n'], 'cairo.csv', { type: 'text/csv' });
    const counts = { created: 1, existing: 0, refused: 0 };
    const none = { created: 0, existing: 0, refused: 0 };

    const checking = facade.importFile.execute({ file, dryRun: true });
    await settle();
    const dry = backend.expectOne((r) => r.method === 'POST' && r.url === `${PLATFORM_API.governorates}/g-cairo/localities/import?dryRun=true`);
    expect(dry.request.body instanceof FormData && (dry.request.body.get('file') as File).name).toBe('cairo.csv');
    dry.flush({ rows: 1, cities: counts, districts: none, areas: none, dryRun: true, applied: false, errors: [] });
    expect((await checking)?.cities.created).toBe(1);
    TestBed.tick();
    await settle();
    backend.expectNone((r) => r.url.endsWith('/cities'));   // nothing written, nothing reloaded

    const importing = facade.importFile.execute({ file, dryRun: false });
    await settle();
    backend.expectOne((r) => r.url.endsWith('/localities/import?dryRun=false')).flush({ rows: 1, cities: counts, districts: none, areas: none, dryRun: false, applied: true, errors: [] });
    expect((await importing)?.applied).toBe(true);
    TestBed.tick();
    await settle();
    backend.expectOne((r) => r.url === `${PLATFORM_API.governorates}/g-cairo/cities`).flush([nasr]);
    await settle();
  });

  it('forgets the previous governorate when another one opens', async () => {
    await openCairo();
    facade.select('cities', 'c-nasr');
    facade.setFilter('cities', { search: 'x' });
    TestBed.tick();
    await settle();
    backend.match(() => true).forEach((request) => request.flush([]));
    await settle();

    facade.open({ ...cairo, id: 'g-giza', nameEn: 'Giza' });
    TestBed.tick();
    await settle();
    expect(facade.cityId()).toBeNull();
    expect(facade.filters.cities().search).toBe('');
    backend.expectOne((r) => r.url === `${PLATFORM_API.governorates}/g-giza/cities`).flush([]);
    await settle();
  });
});
