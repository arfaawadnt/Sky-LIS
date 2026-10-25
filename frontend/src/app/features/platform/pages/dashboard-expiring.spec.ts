import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { PLATFORM_API, type PlatformDashboard } from '../../../core/api/platform.contracts';
import { DashboardFacade } from '../state/dashboard.facade';
import { DashboardPage } from './dashboard.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function dashboard(expiringSoonClients: number, graceClients: number, expiredClients: number): PlatformDashboard {
  return {
    period: 'month',
    fromUtc: '2026-10-01T00:00:00Z',
    toUtc: '2026-11-01T00:00:00Z',
    clients: { total: 5, active: 3, inactive: 1, trial: 1, bySubscription: 2, byManual: 3 },
    adminUsers: 2,
    activity: { servedPatients: 0, tests: 0, registrations: 0 },
    walletsAtOrBelowZero: 0,
    planDistribution: [],
    registrationsByClient: [],
    recentClients: [],
    silentClients: 0,
    silentTrialClients: 0,
    silentDays: 3,
    expiringSoonClients,
    graceClients,
    expiredClients,
  };
}

describe('DashboardPage — expiring soon tile (LIS-CR-012 C-16)', () => {
  let backend: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'admin', children: [{ path: 'dashboard', component: DashboardPage, providers: [DashboardFacade] }] }]),
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function open(body: PlatformDashboard): Promise<HTMLElement> {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/admin/dashboard', DashboardPage);
    TestBed.tick();
    await settle();
    backend.match((request) => request.url === PLATFORM_API.dashboard).forEach((request) => request.flush(body));
    await settle();
    TestBed.tick();
    harness.detectChanges();
    await harness.fixture.whenStable();
    return harness.routeNativeElement as HTMLElement;
  }

  it('counts the clients ending within a week, in red when one expired, and links to the Subscription filter', async () => {
    const host = await open(dashboard(3, 1, 2));

    const tile = host.querySelector<HTMLAnchorElement>('a[data-testid="expiring-tile"]');
    expect(tile?.getAttribute('href')).toBe('/admin/clients?subscription=expiring');
    expect(tile?.querySelector('.val')?.textContent?.trim()).toBe('3');
    expect(tile?.querySelector('.kpi')?.classList.contains('bad')).toBe(true);
    expect(tile?.querySelector('.hint')?.textContent).toMatch(/1 in grace · 2 expired|subscription.tileHint/);
  });

  it('is amber with only expiring clients and green with none', async () => {
    let host = await open(dashboard(2, 0, 0));
    expect(host.querySelector('a[data-testid="expiring-tile"] .kpi')?.classList.contains('warn')).toBe(true);

    TestBed.resetTestingModule();
    await TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'admin', children: [{ path: 'dashboard', component: DashboardPage, providers: [DashboardFacade] }] }]),
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
    host = await open(dashboard(0, 0, 0));
    expect(host.querySelector('a[data-testid="expiring-tile"] .kpi')?.classList.contains('ok')).toBe(true);
  });
});
