import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { type Paged, PLATFORM_API, type SubscriptionClientItem, type SubscriptionClientList, type SubscriptionFilter } from '../../../core/api/platform.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { CsvExportService } from '../data/csv-export';
import { ClientsFacade } from '../state/clients.facade';
import { subscriptionTone } from '../ui/subscription-clients.component';
import { ClientsPage, subscriptionFilterOf } from './clients.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function page<T>(items: T[]): Paged<T> {
  return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 };
}

const soon: SubscriptionClientItem = {
  id: 't-soon',
  nameEn: 'Soon Lab',
  nameAr: 'معمل قريب',
  portalUrl: 'https://lis.example.test/lab/soon',
  status: 'Trial',
  level: 'Warning',
  reason: 'Trial',
  daysLeft: 2,
  endsAtUtc: '2026-10-26T09:00:00Z',
  trialEndsAtUtc: '2026-10-26T09:00:00Z',
  walletBalance: 0,
  monthlyFee: 3000,
  currencyCode: 'EGP',
  amountToTopUp: 3000,
  administratorName: 'Dr. Mona',
  administratorMobile: '+201112223333',
  lastReminderKey: 'trial-3:202610260900',
  lastReminderAtUtc: '2026-10-23T09:00:00Z',
};

const low: SubscriptionClientItem = { ...soon, id: 't-low', nameEn: 'Low Lab', nameAr: 'معمل منخفض', status: 'Active', reason: 'Wallet', daysLeft: 5, trialEndsAtUtc: null, lastReminderKey: null, lastReminderAtUtc: null };

function list(state: SubscriptionFilter, items: SubscriptionClientItem[]): SubscriptionClientList {
  return { state, days: 7, graceDays: 7, asOfUtc: '2026-10-24T09:00:00Z', items };
}

interface PageInternals {
  exportCsv(): Promise<void>;
}

describe('ClientsPage — Subscription filter (LIS-CR-012 C-16)', () => {
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
        {
          provide: CsvExportService,
          useValue: { download: (fileName: string) => downloads.push(fileName), downloadBlob: (fileName: string) => downloads.push(fileName) } satisfies Pick<CsvExportService, 'download' | 'downloadBlob'>,
        },
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

  function flushSubscription(state: SubscriptionFilter, items: SubscriptionClientItem[]): void {
    const request = backend.expectOne((candidate) => candidate.url === PLATFORM_API.subscriptionClients);
    expect(request.request.params.get('state')).toBe(state);
    request.flush(list(state, items));
  }

  async function render(): Promise<HTMLElement> {
    await settle();
    TestBed.tick();
    await settle();
    backend.match((request) => request.url === PLATFORM_API.clients).forEach((request) => request.flush(page([])));
    await settle();
    harness.detectChanges();
    return harness.routeNativeElement as HTMLElement;
  }

  it('opened from the dashboard tile (?subscription=expiring) lists the clients with the days left and the manager to call', async () => {
    await open('/clients?subscription=expiring');
    flushSubscription('expiring', [soon, low]);
    const host = await render();

    expect(host.querySelector<HTMLSelectElement>('[data-testid="subscription-filter"]')?.value).toBe('expiring');
    const row = host.querySelector('app-subscription-clients tbody tr[data-row="t-soon"]');
    expect(row?.textContent).toContain('Soon Lab');
    expect(row?.querySelector('.days')?.textContent?.trim()).toBe('2');
    expect(row?.querySelector('a[href="tel:+201112223333"]')).not.toBeNull();
    expect(host.querySelector('app-silent-clients')).toBeNull();
    expect(host.querySelector('app-paginator')).toBeNull();
  });

  it('narrows the rows with the page status and exports the shown rows', async () => {
    const view = await open('/clients?subscription=expiring');
    flushSubscription('expiring', [soon, low]);
    let host = await render();
    expect(host.querySelectorAll('app-subscription-clients tbody tr[data-row]').length).toBe(2);

    const facade = harness.fixture.debugElement.query((element) => element.componentInstance instanceof ClientsPage)?.injector.get(ClientsFacade);
    facade?.setStatus('Active');
    host = await render();
    expect(Array.from(host.querySelectorAll('app-subscription-clients tbody tr[data-row]')).map((row) => row.getAttribute('data-row'))).toEqual(['t-low']);

    await view.exportCsv();
    expect(downloads).toHaveLength(1);
    expect(downloads[0]).toMatch(/^clients-expiring-/);
  });

  it('choosing another state reloads the list', async () => {
    await open('/clients?subscription=grace');
    flushSubscription('grace', []);
    let host = await render();
    expect(host.querySelector('app-subscription-clients')).not.toBeNull();

    const select = host.querySelector<HTMLSelectElement>('[data-testid="subscription-filter"]')!;
    select.value = 'expired';
    select.dispatchEvent(new Event('change'));
    await render();
    flushSubscription('expired', []);
    host = await render();
    expect(host.querySelector('app-subscription-clients')).not.toBeNull();
  });

  it('reads only the known filters from the address and tones the levels', () => {
    expect(subscriptionFilterOf('grace')).toBe('grace');
    expect(subscriptionFilterOf('soon')).toBeNull();
    expect(subscriptionFilterOf(null)).toBeNull();
    expect([subscriptionTone('Warning'), subscriptionTone('Grace'), subscriptionTone('Expired')]).toEqual(['info', 'warn', 'bad']);
  });
});
