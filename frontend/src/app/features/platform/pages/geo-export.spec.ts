import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, type Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { type CountryListItem, type GovernorateListItem, type Paged, PLATFORM_API } from '../../../core/api/platform.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import type { MasterDataFilter } from '../../../shared/ui/master-data-page/master-data.types';
import { CsvExportService } from '../data/csv-export';
import { CountriesPage } from './countries.page';
import { GovernoratesPage } from './governorates.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function paged<T>(items: T[]): Paged<T> {
  return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 };
}

const egypt: CountryListItem = {
  id: 'eg', nameEn: 'Egypt', nameAr: 'مصر', isoCode: 'EG', dialCode: '+20', currency: 'Pound', currencyCode: 'EGP', language: 'ar',
  timeZone: 'Africa/Cairo', mobileLength: 10, flag: null, remarks: null, isActive: true, governorateCount: 1,
};

const cairo: GovernorateListItem = { id: 'cai', countryId: 'eg', countryNameEn: 'Egypt', countryNameAr: 'مصر', nameEn: 'Cairo', nameAr: 'القاهرة', district: null, remarks: null, isActive: true };

interface ExportingPage {
  readonly config: { readonly adapter: { list(filter: MasterDataFilter, page: { page: number; pageSize: number }): Promise<unknown> } };
  exportCsv(): Promise<void>;
}

/**
 * UAT R2-02: the Countries and Governorates CSV exports write the rows of the search and status filter on screen (one row
 * on screen used to give 6 and 28 rows in the file), as the other registers do since C-04.
 */
describe('Countries and Governorates export the filtered set (UAT R2-02)', () => {
  let backend: HttpTestingController;
  let download: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    download = vi.fn();
    await TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
        { provide: CsvExportService, useValue: { download } },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function exportOf<T>(component: Type<unknown>, url: string, search: string, row: T): Promise<void> {
    const fixture = TestBed.createComponent(component);
    TestBed.tick();
    await settle();
    backend.match(() => true).forEach((request) => request.flush(paged([])));
    await settle();
    const page = fixture.componentInstance as ExportingPage;

    const listed = page.config.adapter.list({ search, active: null }, { page: 1, pageSize: 50 });
    await settle();
    backend.expectOne((request) => request.url === url).flush(paged([row]));
    await listed;

    const exporting = page.exportCsv();
    await settle();
    const request = backend.expectOne((candidate) => candidate.url === url);
    expect(request.request.params.get('search')).toBe(search);
    expect(request.request.params.has('isActive')).toBe(false);
    request.flush(paged([row]));
    await exporting;

    expect(download).toHaveBeenCalledTimes(1);
    expect((download.mock.calls[0] as unknown[])[2]).toEqual([row]);
  }

  it('countries: the search reaches the export', async () => {
    await exportOf(CountriesPage, PLATFORM_API.countries, 'Egypt', egypt);
  });

  it('governorates: the search reaches the export', async () => {
    await exportOf(GovernoratesPage, PLATFORM_API.governorates, 'Cairo', cairo);
  });
});
