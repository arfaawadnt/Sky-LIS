import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { type Paged, PLATFORM_API, type SilentClientItem, type SilentClientList } from '../../../core/api/platform.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { CsvExportService } from '../data/csv-export';
import { ClientsFacade } from '../state/clients.facade';
import { ClientsPage, silentDaysOf } from './clients.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function page<T>(items: T[]): Paged<T> {
  return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 };
}

const quiet: SilentClientItem = {
  id: 't-quiet',
  nameEn: 'Quiet Lab',
  nameAr: 'معمل هادئ',
  portalUrl: 'https://lis.example.test/lab/quiet',
  status: 'Trial',
  createdAtUtc: '2026-09-01T09:00:00Z',
  lastSignInAtUtc: '2026-10-20T09:00:00Z',
  lastRegistrationAtUtc: null,
  lastActivityAtUtc: '2026-10-20T09:00:00Z',
  silentDays: 4,
  administratorName: 'Dr. Mona',
  administratorMobile: '+201112223333',
};

const other: SilentClientItem = { ...quiet, id: 't-other', nameEn: 'Other Lab', nameAr: 'معمل آخر', status: 'Active', administratorName: 'Dr. Ali', administratorMobile: '+201000000000' };

function list(days: number, items: SilentClientItem[]): SilentClientList {
  return { days, asOfUtc: '2026-10-24T09:00:00Z', trial: items.filter((item) => item.status === 'Trial').length, items };
}

interface PageInternals {
  exportCsv(): Promise<void>;
}

describe('ClientsPage — Silent filter (LIS-CR-007 A-01)', () => {
  let backend: HttpTestingController;
  let harness: RouterTestingHarness;
  const downloads: string[] = [];

  beforeEach(async () => {
    downloads.length = 0;
    await TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        provideRouter([{ path: 'clients', component: ClientsPage, providers: [ClientsFacade] }]),
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
        { provide: CsvExportService, useValue: { downloadBlob: (fileName: string) => downloads.push(fileName) } satisfies Pick<CsvExportService, 'downloadBlob'> },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function open(url: string): Promise<PageInternals> {
    harness = await RouterTestingHarness.create();
    const view = (await harness.navigateByUrl(url, ClientsPage)) as unknown as PageInternals;
    TestBed.tick();
    await settle();
    backend.match((request) => request.url === PLATFORM_API.clients).forEach((request) => request.flush(page([])));
    backend.match((request) => request.url === PLATFORM_API.plans || request.url === PLATFORM_API.countries).forEach((request) => request.flush(page([])));
    await settle();
    return view;
  }

  function flushSilent(days: number, items: SilentClientItem[]): void {
    const request = backend.expectOne((candidate) => candidate.url === PLATFORM_API.silentClients);
    expect(request.request.params.get('days')).toBe(String(days));
    request.flush(list(days, items));
  }

  /** Runs the effects and resources, answers the register's own reloads (the Silent table does not use them) and renders; never waits for stability while a request is pending. */
  async function render(): Promise<HTMLElement> {
    await settle();
    TestBed.tick();
    await settle();
    backend.match((request) => request.url === PLATFORM_API.clients).forEach((request) => request.flush(page([])));
    await settle();
    harness.detectChanges();
    return harness.routeNativeElement as HTMLElement;
  }

  it('opened from the dashboard tile (?silent=3) shows the silent clients with the administrator to call, not the register', async () => {
    await open('/clients?silent=3');
    flushSilent(3, [quiet, other]);
    const host = await render();

    const select = host.querySelector<HTMLSelectElement>('[data-testid="silent-filter"]');
    expect(select?.value).toBe('3');
    const row = host.querySelector('app-silent-clients tbody tr[data-row="t-quiet"]');
    expect(row?.textContent).toContain('Quiet Lab');
    expect(row?.textContent).toContain('https://lis.example.test/lab/quiet');
    expect(row?.querySelector('a[href="tel:+201112223333"]')).not.toBeNull();
    expect(row?.querySelector('.silent-days')?.textContent?.trim()).toBe('4');
    expect(host.querySelector('app-paginator')).toBeNull();
    expect(host.querySelector('app-silent-clients [role="status"]')).not.toBeNull();
  });

  it('narrows the rows with the page search and status, changes the window and returns to the register', async () => {
    await open('/clients?silent=3');
    flushSilent(3, [quiet, other]);
    let host = await render();
    expect(host.querySelectorAll('app-silent-clients tbody tr[data-row]').length).toBe(2);

    const facade = harness.fixture.debugElement.query((element) => element.componentInstance instanceof ClientsPage)?.injector.get(ClientsFacade);
    facade?.setStatus('Trial');
    host = await render();
    expect(Array.from(host.querySelectorAll('app-silent-clients tbody tr[data-row]')).map((row) => row.getAttribute('data-row'))).toEqual(['t-quiet']);
    facade?.setStatus(null);
    facade?.setSearch('ali');
    host = await render();
    expect(Array.from(host.querySelectorAll('app-silent-clients tbody tr[data-row]')).map((row) => row.getAttribute('data-row'))).toEqual(['t-other']);

    const select = host.querySelector<HTMLSelectElement>('[data-testid="silent-filter"]');
    if (select === null) {
      throw new Error('no silent filter');
    }

    select.value = '7';
    select.dispatchEvent(new Event('change'));
    await render();
    flushSilent(7, []);
    host = await render();
    expect(TestBed.inject(Router).url).toContain('silent=7');
    expect(host.querySelector('app-silent-clients tbody tr[data-row]')).toBeNull();

    select.value = '';
    select.dispatchEvent(new Event('change'));
    host = await render();
    expect(host.querySelector('app-silent-clients')).toBeNull();
    expect(host.querySelector('app-paginator')).not.toBeNull();
    expect(TestBed.inject(Router).url).not.toContain('silent=');
  });

  it('exports the server CSV of the window from the page header', async () => {
    const view = await open('/clients?silent=14');
    flushSilent(14, [quiet]);
    await render();

    const exporting = view.exportCsv();
    await settle();
    const request = backend.expectOne((candidate) => candidate.url === `${PLATFORM_API.silentClients}/export`);
    expect(request.request.params.get('days')).toBe('14');
    expect(request.request.params.get('language')).toBe('en');
    request.flush(new Blob(['x'], { type: 'text/csv' }), { headers: { 'Content-Disposition': 'attachment; filename=silent-clients-14d-2026-10-24-0900.csv' } });
    await exporting;
    expect(downloads).toEqual(['silent-clients-14d-2026-10-24-0900.csv']);
  });

  it('reads only the offered windows from the address', () => {
    expect(silentDaysOf('3')).toBe(3);
    expect(silentDaysOf('30')).toBe(30);
    expect(silentDaysOf('5')).toBeNull();
    expect(silentDaysOf('')).toBeNull();
    expect(silentDaysOf(null)).toBeNull();
    expect(silentDaysOf('abc')).toBeNull();
  });
});
