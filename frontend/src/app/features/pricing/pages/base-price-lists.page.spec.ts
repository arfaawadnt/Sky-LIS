import { type HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { PRICING_API, PRICING_PERMISSIONS, type BasePriceListItem } from '../../../core/api/pricing.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { LISTS_PAGE } from '../testing/pricing.fixtures';
import { BasePriceListsPage } from './base-price-lists.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const PL25: BasePriceListItem = { ...LISTS_PAGE.items[0]!, id: 'pl25', code: 'PL25', nameEn: 'Main 2025', nameAr: 'رئيسية 2025', year: 2025, isActive: false };

/** The page's parts the scenarios drive (kept protected by the page). */
interface ListsInternals {
  readonly config: {
    readonly adapter: { list(filter: { search: string; active: boolean | null }, page: { page: number; pageSize: number }): Promise<unknown> };
    readonly rowActions: readonly { readonly id: string; run(item: BasePriceListItem): void | Promise<void> }[];
  };
  exportCsv(): Promise<void>;
}

describe('BasePriceListsPage (CP-33)', () => {
  let backend: HttpTestingController;
  let allowed: (key: string) => boolean = () => true;

  beforeEach(async () => {
    allowed = () => true;
    await TestBed.configureTestingModule({
      imports: [BasePriceListsPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: PermissionsService, useValue: { can: (key: string) => allowed(key) } satisfies Pick<PermissionsService, 'can'> },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function flush(fixture?: ComponentFixture<BasePriceListsPage>): Promise<void> {
    TestBed.tick();
    await settle();
    TestBed.tick();
    await settle();
    fixture?.detectChanges();
  }

  const lists = (request: HttpRequest<unknown>): boolean => request.url === PRICING_API.basePriceLists;
  /** The register's own read carries its status filter; the year options are read over every status. */
  const registerRead = (request: HttpRequest<unknown>): boolean => lists(request) && request.params.has('isActive');

  async function open(): Promise<{ fixture: ComponentFixture<BasePriceListsPage>; host: HTMLElement; page: ListsInternals }> {
    const fixture = TestBed.createComponent(BasePriceListsPage);
    await flush(fixture);
    for (const request of backend.match((candidate) => candidate.url === PRICING_API.basePriceLists)) {
      request.flush(request.request.params.has('isActive') ? LISTS_PAGE : { ...LISTS_PAGE, items: [...LISTS_PAGE.items, PL25], totalCount: 3 });
    }

    await flush(fixture);
    return { fixture, host: fixture.nativeElement as HTMLElement, page: fixture.componentInstance as unknown as ListsInternals };
  }

  it('filters by year on the server and starts again at page 1; "All years" lifts it (UAT B-07)', async () => {
    const { fixture, host } = await open();
    const select = host.querySelector<HTMLSelectElement>('.year-filter select');
    if (!select) {
      throw new Error('year filter missing');
    }

    expect([...select.options].map((option) => option.value)).toEqual(['', '2026', '2025']);

    select.value = '2025';
    select.dispatchEvent(new Event('change'));
    await flush(fixture);
    const filtered = backend.match((candidate) => lists(candidate));
    expect(filtered.length).toBe(1);
    expect(filtered[0]?.request.params.get('year')).toBe('2025');
    expect(filtered[0]?.request.params.get('page')).toBe('1');
    expect(filtered[0]?.request.params.get('isActive')).toBe('true');
    filtered[0]?.flush({ ...LISTS_PAGE, items: [], totalCount: 0 });
    await flush(fixture);

    select.value = '';
    select.dispatchEvent(new Event('change'));
    await flush(fixture);
    const all = backend.expectOne(registerRead);
    expect(all.request.params.has('year')).toBe(false);
    all.flush(LISTS_PAGE);
    backend.verify();
  });

  it('offers Export CSV only with pricing.export (UAT B-07)', async () => {
    allowed = (key) => key !== PRICING_PERMISSIONS.export;
    const reader = await open();
    expect([...reader.host.querySelectorAll('app-page-header button')].some((button) => button.textContent?.includes('pricing.exportCsv'))).toBe(false);

    allowed = () => true;
    const exporter = await open();
    expect([...exporter.host.querySelectorAll('app-page-header button')].some((button) => button.textContent?.includes('pricing.exportCsv'))).toBe(true);
  });

  it('exports exactly the lists of the search, the status and the year on screen (UAT B-07)', async () => {
    const { fixture, host, page } = await open();
    const select = host.querySelector<HTMLSelectElement>('.year-filter select');
    select!.value = '2026';
    select!.dispatchEvent(new Event('change'));
    await flush(fixture);
    backend.expectOne(registerRead).flush(LISTS_PAGE);

    const listed = page.config.adapter.list({ search: 'main', active: null }, { page: 1, pageSize: 50 });
    await flush(fixture);
    backend.expectOne(lists).flush(LISTS_PAGE);
    await listed;

    const exporting = page.exportCsv();
    await flush(fixture);
    const request = backend.expectOne(lists);
    expect(request.request.params.get('search')).toBe('main');
    expect(request.request.params.get('year')).toBe('2026');
    expect(request.request.params.has('isActive')).toBe(false);
    expect(request.request.params.get('pageSize')).toBe('200');
    request.flush(LISTS_PAGE);
    await exporting;
    backend.verify();
  });

  it('reads the existing codes of every status before proposing the copy code (UAT B-06)', async () => {
    const { fixture, host, page } = await open();
    const copy = page.config.rowActions.find((action) => action.id === 'copy');
    const opening = copy?.run(LISTS_PAGE.items[0]!);
    await flush(fixture);
    const lookup = backend.expectOne(lists);
    expect(lookup.request.params.get('search')).toBe('PL27');
    expect(lookup.request.params.has('isActive')).toBe(false);
    lookup.flush({ ...LISTS_PAGE, items: [{ ...PL25, code: 'PL27', year: 2027 }], totalCount: 1 });
    await opening;
    await flush(fixture);

    expect(host.querySelector<HTMLInputElement>('app-copy-list-dialog input[formcontrolname="code"]')?.value).toBe('PL27-2');
    backend.verify();
  });
});
