import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { CATALOGUE_A_API, CATALOGUE_A_PERMISSIONS } from '../../../core/api/catalogue-a.contracts';
import { SAMPLE_COMPATIBILITY_API, type SampleCompatibilityGroupView } from '../../../core/api/sample-compatibility.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { SampleCompatibilityFacade } from '../state-a/sample-compatibility.facade';
import { CompatibleConditionsPage } from './compatible-conditions.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const sugar: SampleCompatibilityGroupView = {
  id: 'g1',
  code: 'SUGAR',
  nameEn: 'Sugar samples',
  nameAr: 'عينات السكر',
  isActive: true,
  members: [
    { sampleTypeId: 'ser', sampleTypeCode: 'SER', sampleTypeNameEn: 'Serum', sampleTypeNameAr: 'مصل', sampleConditionId: 'fast', sampleConditionCode: 'FAST', sampleConditionNameEn: 'Fasting', sampleConditionNameAr: 'صائم', priority: 1, available: true },
    { sampleTypeId: 'ser', sampleTypeCode: 'SER', sampleTypeNameEn: 'Serum', sampleTypeNameAr: 'مصل', sampleConditionId: 'rand', sampleConditionCode: 'RAND', sampleConditionNameEn: 'Random', sampleConditionNameAr: 'عشوائي', priority: 2, available: true },
  ],
};

describe('CompatibleConditionsPage: compatible samples (LIS-CR-015 M4)', () => {
  let backend: HttpTestingController;
  let allowed: (key: string) => boolean = () => true;

  beforeEach(async () => {
    allowed = () => true;
    await TestBed.configureTestingModule({
      imports: [CompatibleConditionsPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        SampleCompatibilityFacade,
        { provide: PermissionsService, useValue: { can: (key: string) => allowed(key) } satisfies Pick<PermissionsService, 'can'> },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function flushLists(): Promise<void> {
    await settle();
    backend.match((request) => request.method === 'GET' && request.url === SAMPLE_COMPATIBILITY_API).forEach((request) => request.flush({ items: [sugar], page: 1, pageSize: 50, totalCount: 1, totalPages: 1 }));
    backend
      .match((request) => request.method === 'GET' && request.url === CATALOGUE_A_API.sampleTypes)
      .forEach((request) => request.flush({ items: [{ id: 'ser', code: 'SER', shortName: 'SER', nameEn: 'Serum', nameAr: 'مصل', activeConditionCount: 2, isActive: true, forCulture: false }], page: 1, pageSize: 200, totalCount: 1, totalPages: 1 }));
    backend
      .match((request) => request.method === 'GET' && request.url === `${CATALOGUE_A_API.sampleTypes}/ser`)
      .forEach((request) =>
        request.flush({
          id: 'ser',
          code: 'SER',
          shortName: 'SER',
          nameEn: 'Serum',
          nameAr: 'مصل',
          isActive: true,
          forCulture: false,
          conditions: [
            { id: 'fast', code: 'FAST', nameEn: 'Fasting', nameAr: 'صائم', isActive: true },
            { id: 'rand', code: 'RAND', nameEn: 'Random', nameAr: 'عشوائي', isActive: true },
          ],
        }),
      );
    await settle();
    TestBed.tick();
  }

  async function render(): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(CompatibleConditionsPage);
    TestBed.tick();
    await flushLists();
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('lists the groups with their members as ordered chips', async () => {
    const root = await render();
    const row = root.querySelector('app-data-table tbody tr');
    expect(row?.textContent).toContain('SUGAR');
    expect(row?.textContent).toContain('1 · Serum · Fasting');
    expect(row?.textContent).toContain('2 · Serum · Random');
  });

  it('sends the search and the status filter to the server', async () => {
    await render();
    const facade = TestBed.inject(SampleCompatibilityFacade);
    facade.setSearch('fast');
    facade.setActiveFilter(null);
    TestBed.tick();
    await settle();
    const request = backend.expectOne((candidate) => candidate.url === SAMPLE_COMPATIBILITY_API);
    expect(request.request.params.get('search')).toBe('fast');
    expect(request.request.params.has('isActive')).toBe(false);
    request.flush({ items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 });
  });

  it('edits a group in the drawer and saves the new order', async () => {
    const root = await render();
    root.querySelector<HTMLButtonElement>('app-data-table .row-btn.edit')?.click();
    TestBed.tick();
    await flushLists();

    root.querySelector<HTMLButtonElement>('tr[data-member="2"] .row-btn.up')?.click();
    TestBed.tick();
    root.querySelector<HTMLButtonElement>('app-compatibility-group-drawer .btn.primary')?.click();
    await settle();

    const put = backend.expectOne((request) => request.method === 'PUT' && request.url === `${SAMPLE_COMPATIBILITY_API}/g1`);
    expect(put.request.body).toEqual({
      nameEn: 'Sugar samples',
      nameAr: 'عينات السكر',
      members: [
        { sampleTypeId: 'ser', sampleConditionId: 'rand' },
        { sampleTypeId: 'ser', sampleConditionId: 'fast' },
      ],
    });
    put.flush(null, { status: 204, statusText: 'No Content' });
    await flushLists();
  });

  it('shows no add, edit or switch action to a reader', async () => {
    allowed = (key) => key === CATALOGUE_A_PERMISSIONS.view;
    const root = await render();
    expect(root.querySelector('app-page-header .btn.primary')).toBeNull();
    expect(root.querySelector('app-data-table .row-btn')).toBeNull();
  });
});
