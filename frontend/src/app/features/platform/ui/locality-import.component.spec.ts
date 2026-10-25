import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { type GovernorateListItem, type LocalityImportResult, PLATFORM_API } from '../../../core/api/platform.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { LocationsFacade } from '../state/locations.facade';
import { LocalityImportComponent } from './locality-import.component';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const cairo: GovernorateListItem = { id: 'g-cairo', countryId: 'c-eg', countryNameEn: 'Egypt', countryNameAr: 'مصر', nameEn: 'Cairo', nameAr: 'القاهرة', district: null, remarks: null, isActive: true };

const level = (created: number, existing: number, refused: number): LocalityImportResult['cities'] => ({ created, existing, refused });

describe('LocalityImportComponent (SA-11 Import CSV, LIS-CR-008 C-04)', () => {
  let backend: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LocalityImportComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        LocationsFacade,
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function render(): Promise<{ host: HTMLElement; fixture: ReturnType<typeof TestBed.createComponent<LocalityImportComponent>> }> {
    TestBed.inject(LocationsFacade).open(cairo);
    const fixture = TestBed.createComponent(LocalityImportComponent);
    TestBed.tick();
    await settle();
    backend.match((request) => request.url === `${PLATFORM_API.governorates}/g-cairo/cities`).forEach((request) => request.flush([]));
    await fixture.whenStable();
    return { host: fixture.nativeElement as HTMLElement, fixture };
  }

  async function choose(host: HTMLElement): Promise<void> {
    const input = host.querySelector<HTMLInputElement>('input[type=file]');
    if (input === null) {
      throw new Error('no file input');
    }

    const file = new File(['city_en\nShubra\n'], 'cairo.csv', { type: 'text/csv' });
    Object.defineProperty(input, 'files', { value: { item: () => file, length: 1 }, configurable: true });
    input.dispatchEvent(new Event('change'));
    await settle();
  }

  it('checks the chosen file first and imports it on demand', async () => {
    const { host, fixture } = await render();
    await choose(host);
    backend.expectOne((request) => request.url.endsWith('/localities/import?dryRun=true')).flush({
      rows: 2, cities: level(1, 1, 0), districts: level(0, 0, 0), areas: level(0, 0, 0), dryRun: true, applied: false, errors: [],
    } satisfies LocalityImportResult);
    await settle();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(host.querySelectorAll('.summary .counts tbody tr').length).toBe(3);   // one row per level
    const apply = host.querySelector<HTMLButtonElement>('.summary .btn.primary');
    expect(apply?.disabled).toBe(false);

    apply?.click();
    await settle();
    backend.expectOne((request) => request.url.endsWith('/localities/import?dryRun=false')).flush({
      rows: 2, cities: level(1, 1, 0), districts: level(0, 0, 0), areas: level(0, 0, 0), dryRun: false, applied: true, errors: [],
    } satisfies LocalityImportResult);
    await settle();
    TestBed.tick();
    await settle();
    backend.match(() => true).forEach((request) => request.flush([]));
    await fixture.whenStable();
    expect(host.querySelector('.summary')).toBeNull();   // done: the summary closes, the columns reloaded
  });

  it('lists the refused lines and keeps Import disabled (all-or-nothing)', async () => {
    const { host, fixture } = await render();
    await choose(host);
    backend.expectOne((request) => request.url.endsWith('/localities/import?dryRun=true')).flush({
      rows: 2, cities: level(1, 0, 0), districts: level(0, 0, 0), areas: level(0, 0, 1), dryRun: true, applied: false,
      errors: [{ line: 3, level: 'Area', code: 'GEO-025', message: 'An area needs its district.' }],
    } satisfies LocalityImportResult);
    await settle();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(host.querySelectorAll('.errors li').length).toBe(1);
    expect(host.querySelector<HTMLButtonElement>('.summary .btn.primary')?.disabled).toBe(true);
    backend.expectNone((request) => request.url.endsWith('dryRun=false'));
  });
});
