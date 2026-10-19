import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { type ClientListItem, type ClientLoginLogItem, type Paged, PLATFORM_API } from '../../../core/api/platform.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { CsvExportService } from '../data/csv-export';
import { LoginLogsFacade } from '../state/login-logs.facade';
import { LoginLogsPage } from './login-logs.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function page<T>(items: T[]): Paged<T> {
  return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 };
}

const client: ClientListItem = {
  id: 't1', nameEn: 'Al Noor Lab', nameAr: 'معمل النور', portalSlug: 'al-noor', portalUrl: 'https://al-noor.skylis.app', countryId: 'eg', countryNameEn: 'Egypt', countryNameAr: 'مصر',
  governorateId: 'cairo', governorateNameEn: 'Cairo', governorateNameAr: 'القاهرة', managerName: 'Dr. Mona', managerMobile: '+201112223333', planId: 'p1', planCode: 'STARTER',
  planNameEn: 'Starter', planNameAr: 'الأساسية', walletBalance: 0, walletCurrency: 'EGP', branches: 1, users: 2, createdAtUtc: '2026-10-09T09:00:00Z', source: 'Manual', status: 'Active', trialEndsAtUtc: null,
};

const signedOut: ClientLoginLogItem = {
  familyId: 'f1', tenantId: 't1', clientNameEn: 'Al Noor Lab', clientNameAr: 'معمل النور', userId: 'u1', username: 'noor.reception', displayName: 'Noor Reception',
  loginAtUtc: '2026-10-19T09:00:00Z', logoutAtUtc: '2026-10-19T10:07:00Z', lastSeenAtUtc: '2026-10-19T10:07:00Z', ipAddress: '10.0.0.7', device: 'Computer', os: 'Windows 10', browser: 'Chrome 129',
  durationMinutes: 67, status: 'SignedOut', revocationReason: null,
};

const revoked: ClientLoginLogItem = {
  ...signedOut, familyId: 'f2', tenantId: 't9', clientNameEn: null, clientNameAr: null, userId: 'u2', username: 'sara.cashier', displayName: 'Sara Cashier', logoutAtUtc: '2026-10-19T09:15:00Z',
  ipAddress: null, device: 'Mobile', os: 'iOS 17.5', browser: 'Safari 17', durationMinutes: 15, status: 'Revoked', revocationReason: 'Admin',
};

interface PageAccess {
  form: { setValue(value: Record<string, string>): void; patchValue(value: Record<string, string>): void };
  apply(): void;
  reset(): void;
  exportCsv(): Promise<void>;
  criteriaProblem(): string | null;
}

describe('LoginLogsPage (SA-08b)', () => {
  let backend: HttpTestingController;
  const downloads: string[] = [];

  beforeEach(async () => {
    downloads.length = 0;
    await TestBed.configureTestingModule({
      imports: [LoginLogsPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        LoginLogsFacade,
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
        { provide: CsvExportService, useValue: { downloadBlob: (fileName: string) => downloads.push(fileName) } satisfies Pick<CsvExportService, 'downloadBlob'> },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  function expectLogs(): ReturnType<HttpTestingController['expectOne']> {
    return backend.expectOne((request) => request.method === 'GET' && request.url === PLATFORM_API.loginLogs);
  }

  async function render(): Promise<{ fixture: ReturnType<typeof TestBed.createComponent<LoginLogsPage>>; host: HTMLElement; view: PageAccess }> {
    const fixture = TestBed.createComponent(LoginLogsPage);
    TestBed.tick();
    await settle();
    expectLogs().flush(page([signedOut, revoked]));
    backend.expectOne((request) => request.url === PLATFORM_API.clients).flush(page([client]));
    await settle();
    await fixture.whenStable();
    return { fixture, host: fixture.nativeElement as HTMLElement, view: fixture.componentInstance as unknown as PageAccess };
  }

  it('shows one row per session with the client, the user, UTC times, the device and the way it ended', async () => {
    const { host } = await render();

    const first = host.querySelector('tbody tr[data-row="f1"]');
    expect(first?.textContent).toContain('Al Noor Lab');
    expect(first?.textContent).toContain('Noor Reception');
    expect(first?.textContent).toContain('noor.reception');
    expect(first?.textContent).toContain('19/10/2026 09:00');   // UTC, not the browser's zone
    expect(first?.textContent).toContain('19/10/2026 10:07');
    expect(first?.textContent).toContain('Windows 10 · Chrome 129');
    expect(first?.textContent).toContain('1h 07m');
    expect(first?.textContent).toMatch(/Signed out|status\.SignedOut/);

    const second = host.querySelector('tbody tr[data-row="f2"]');
    expect(second?.textContent).toMatch(/No client record|noClientRecord/);
    expect(second?.textContent).toMatch(/Ended by an administrator|reason\.Admin/);
    expect(second?.textContent).toContain('15m');

    const options = Array.from(host.querySelectorAll<HTMLOptionElement>('select[formcontrolname="tenantId"] option')).map((option) => option.textContent?.trim());
    expect(options).toContain('Al Noor Lab');
  });

  it('applies the filter card as UTC query parameters and refuses a reversed window before sending it', async () => {
    const { fixture, view } = await render();

    view.form.patchValue({ tenantId: 't1', search: ' noor ', loginFrom: '2026-10-19T08:00', loginTo: '2026-10-19T18:30', device: 'Computer', status: 'SignedOut', minMinutes: '5', maxMinutes: '' });
    view.apply();
    TestBed.tick();
    await settle();
    const filtered = expectLogs();
    const params = filtered.request.params;
    expect([params.get('tenantId'), params.get('search'), params.get('loginFromUtc'), params.get('loginToUtc'), params.get('device'), params.get('status'), params.get('minMinutes'), params.get('page')])
      .toEqual(['t1', 'noor', '2026-10-19T08:00:00.000Z', '2026-10-19T18:30:00.000Z', 'Computer', 'SignedOut', '5', '1']);
    expect(params.has('maxMinutes')).toBe(false);
    filtered.flush(page([]));
    await fixture.whenStable();

    view.form.patchValue({ loginFrom: '2026-10-20T08:00', loginTo: '2026-10-19T18:30' });
    view.apply();
    TestBed.tick();
    await settle();
    expect(view.criteriaProblem()).toMatch(/before it ends|reversedWindow/);
    backend.expectNone((request) => request.url === PLATFORM_API.loginLogs);

    view.form.patchValue({ loginFrom: '', loginTo: '', minMinutes: '30', maxMinutes: '10' });
    view.apply();
    expect(view.criteriaProblem()).toMatch(/must not exceed|crossedMinutes/);

    view.reset();
    TestBed.tick();
    await settle();
    const cleared = expectLogs();
    expect(cleared.request.params.keys().sort()).toEqual(['page', 'pageSize']);
    cleared.flush(page([]));
    expect(view.criteriaProblem()).toBeNull();
  });

  it('downloads the CSV the server builds under its file name', async () => {
    const { view } = await render();

    const exporting = view.exportCsv();
    await settle();
    backend.expectOne((request) => request.url === `${PLATFORM_API.loginLogs}/export`)
      .flush(new Blob(['﻿Client'], { type: 'text/csv' }), { headers: { 'Content-Disposition': 'attachment; filename=client-login-logs-2026-10-19-0900.csv' } });
    await exporting;
    expect(downloads).toEqual(['client-login-logs-2026-10-19-0900.csv']);
  });
});
