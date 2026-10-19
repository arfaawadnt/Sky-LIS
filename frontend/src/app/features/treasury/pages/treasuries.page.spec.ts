import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { ORGANIZATION_API } from '../../../core/api/organization.contracts';
import { TREASURY_API, TREASURY_PERMISSIONS, type TreasuryListItem } from '../../../core/api/treasury.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import type { MasterDataFilter } from '../../../shared/ui/master-data-page/master-data.types';
import { TreasuriesPage } from './treasuries.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const north: TreasuryListItem = { id: 't1', code: 'NORTH-CASH', nameEn: 'North cash', nameAr: 'خزينة الشمال', branchId: 'b1', isActive: true, branchCode: 'NORTH', branchNameEn: 'North', branchNameAr: 'الشمال' };
const main: TreasuryListItem = { id: 't2', code: 'MAIN-CASH', nameEn: 'Main cash', nameAr: 'خزينة الرئيسي', branchId: 'b2', isActive: true, branchCode: 'MAIN', branchNameEn: 'Main', branchNameAr: 'الرئيسي' };

interface TreasuriesInternals {
  readonly config: {
    readonly searchMode?: 'server' | 'client';
    readonly sortMode?: 'server' | 'client';
    readonly columns: readonly { readonly key: string; readonly sortable?: boolean; readonly sortKey?: string | ((lang: string) => string); readonly value?: (item: TreasuryListItem) => unknown }[];
    readonly adapter: { list(filter: MasterDataFilter, page: { page: number; pageSize: number }): Promise<unknown> };
  };
  exportCsv(): Promise<void>;
}

describe('TreasuriesPage (CP-28)', () => {
  let backend: HttpTestingController;
  let allowed: (key: string) => boolean = () => true;

  beforeEach(async () => {
    allowed = () => true;
    await TestBed.configureTestingModule({
      imports: [TreasuriesPage],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),   // the C11 links (RouterLink) need a router
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: (key: string) => allowed(key) } satisfies Pick<PermissionsService, 'can'> },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function open(): Promise<TreasuriesInternals> {
    const fixture = TestBed.createComponent(TreasuriesPage);
    TestBed.tick();
    await settle();
    backend.match((request) => request.url === TREASURY_API.treasuries).forEach((request) => request.flush({ items: [north, main], page: 1, pageSize: 50, totalCount: 2, totalPages: 1 }));
    await settle();
    return fixture.componentInstance as unknown as TreasuriesInternals;
  }

  it('shows the branch as CODE · name from the list, and a reader never asks for the branch register (UAT A-04)', async () => {
    allowed = (key) => key === TREASURY_PERMISSIONS.view;
    const page = await open();

    const branch = page.config.columns.find((column) => column.key === 'branchId');
    expect(branch?.value?.(north)).toBe('NORTH · North');
    expect(branch?.value?.({ ...north, branchCode: null, branchNameEn: null, branchNameAr: null })).toBe('b1');
    backend.expectNone((request) => request.url === ORGANIZATION_API.branches);
  });

  it('a manager without organization.view picks among the selectable branches, never the register (validation v0.5.0 R-08)', async () => {
    allowed = (key) => key !== 'organization.view';
    await open();
    backend.expectNone((request) => request.url === ORGANIZATION_API.branches);
    backend.expectOne(ORGANIZATION_API.selectableBranches).flush({ currentBranchId: 'b2', branches: [{ id: 'b2', code: 'MAIN', nameEn: 'Main', nameAr: 'الرئيسي' }] });
    await settle();
    backend.verify();
  });

  it('offers Export CSV only with treasury.export, not with the view key alone (UAT R1-05)', async () => {
    allowed = (key) => key !== TREASURY_PERMISSIONS.export;
    const reader = TestBed.createComponent(TreasuriesPage);
    TestBed.tick();
    await settle();
    reader.detectChanges();
    expect((reader.nativeElement as HTMLElement).querySelector('app-treasury-export-button')).toBeNull();

    allowed = (key) => key === TREASURY_PERMISSIONS.view || key === TREASURY_PERMISSIONS.export;
    const exporter = TestBed.createComponent(TreasuriesPage);
    TestBed.tick();
    await settle();
    exporter.detectChanges();
    expect((exporter.nativeElement as HTMLElement).querySelector('app-treasury-export-button')).not.toBeNull();
  });

  it('exports the rows of the status filter and the search, as the grid shows them (UAT B-10)', async () => {
    const page = await open();
    backend.match((request) => request.url === ORGANIZATION_API.branches).forEach((request) => request.flush({ items: [], page: 1, pageSize: 200, totalCount: 0, totalPages: 0 }));
    const listed = page.config.adapter.list({ search: 'north', active: null }, { page: 1, pageSize: 50 });
    await settle();
    backend.expectOne((request) => request.url === TREASURY_API.treasuries).flush({ items: [north], page: 1, pageSize: 50, totalCount: 1, totalPages: 1 });
    await listed;

    const exporting = page.exportCsv();
    await settle();
    const request = backend.expectOne((candidate) => candidate.url === TREASURY_API.treasuries);
    expect(request.request.params.has('isActive')).toBe(false);
    expect(request.request.params.get('search')).toBe('north');
    request.flush({ items: [north], page: 1, pageSize: 200, totalCount: 1, totalPages: 1 });
    await exporting;
  });

  it('B-03: sorts the whole catalogue on the server by the code or the name of the language on screen', async () => {
    const page = await open();
    expect(page.config.sortMode).toBe('server');
    const name = page.config.columns.find((column) => column.key === 'name');
    const key = name?.sortKey;
    expect(typeof key === 'function' ? [key('en'), key('ar')] : key).toEqual(['nameEn', 'nameAr']);

    const listed = page.config.adapter.list({ search: '', active: true, sort: { key: 'nameAr', direction: 'desc' } }, { page: 1, pageSize: 50 });
    await settle();
    const request = backend.expectOne((candidate) => candidate.url === TREASURY_API.treasuries);
    expect([request.request.params.get('sort'), request.request.params.get('direction')]).toEqual(['nameAr', 'desc']);
    request.flush({ items: [north, main], page: 1, pageSize: 50, totalCount: 2, totalPages: 1 });
    await listed;

    const unsorted = page.config.adapter.list({ search: '', active: true }, { page: 1, pageSize: 50 });
    await settle();
    const plain = backend.expectOne((candidate) => candidate.url === TREASURY_API.treasuries);
    expect(plain.request.params.has('sort')).toBe(false);
    plain.flush({ items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 });
    await unsorted;
  });

  it('searches on the server, so an Arabic name is found from the English UI and every row the server returns is shown (UAT B-08)', async () => {
    const page = await open();
    expect(page.config.searchMode).toBe('server');

    const listed = page.config.adapter.list({ search: ' الشمال ', active: true }, { page: 1, pageSize: 50 });
    await settle();
    const request = backend.expectOne((candidate) => candidate.url === TREASURY_API.treasuries);
    expect(request.request.params.get('search')).toBe('الشمال');
    expect(request.request.params.get('isActive')).toBe('true');
    request.flush({ items: [north], page: 1, pageSize: 50, totalCount: 1, totalPages: 1 });
    expect(await listed).toEqual({ items: [north], page: 1, pageSize: 50, totalCount: 1, totalPages: 1 });

    const cleared = page.config.adapter.list({ search: '', active: true }, { page: 1, pageSize: 50 });
    await settle();
    const all = backend.expectOne((candidate) => candidate.url === TREASURY_API.treasuries);
    expect(all.request.params.has('search')).toBe(false);
    all.flush({ items: [north, main], page: 1, pageSize: 50, totalCount: 2, totalPages: 1 });
    await cleared;
  });
});
