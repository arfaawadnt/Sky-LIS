import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { PLATFORM_API, type PlatformDashboard } from '../../../core/api/platform.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { AdminHomePage, PLATFORM_VIEW } from './admin-home.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const dashboard: PlatformDashboard = {
  period: 'month',
  fromUtc: '2026-10-01T00:00:00Z',
  toUtc: '2026-11-01T00:00:00Z',
  clients: { total: 11, active: 9, inactive: 1, trial: 1, bySubscription: 4, byManual: 7 },
  adminUsers: 3,
  activity: { servedPatients: 0, tests: 0, registrations: 0 },
  walletsAtOrBelowZero: 0,
  planDistribution: [],
  registrationsByClient: [],
  recentClients: [],
};

/** UAT R2-05: the Super Admin home shows the Release-1 tiles only, filled from the platform dashboard the dashboard page reads. */
describe('AdminHomePage (SA-01)', () => {
  let backend: HttpTestingController;
  let allowed = true;

  beforeEach(async () => {
    allowed = true;
    await TestBed.configureTestingModule({
      imports: [AdminHomePage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthStore, useValue: { hasPermission: (key: string) => allowed && key === PLATFORM_VIEW, currentUser: signal(null) } },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function render(): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(AdminHomePage);
    TestBed.tick();
    await settle();
    backend.match((request) => request.url.startsWith('/i18n/')).forEach((request) => request.flush({}));
    return fixture.nativeElement as HTMLElement;
  }

  it('shows Clients and Active subscriptions from the platform dashboard, and no Release-2 tile', async () => {
    const root = await render();
    backend.expectOne((request) => request.url === PLATFORM_API.dashboard).flush(dashboard);
    await settle();
    TestBed.tick();

    const tiles = Array.from(root.querySelectorAll('app-stat-card')).map((tile) => tile.textContent ?? '');
    expect(tiles.length).toBe(2);
    expect(tiles[0]).toContain('11');
    expect(tiles[1]).toContain('9');
    expect(root.textContent).not.toContain('openTickets');
    expect(root.textContent).not.toContain('agentsOnline');
  });

  it('without the dashboard permission the tiles read "—" and nothing is requested', async () => {
    allowed = false;
    const root = await render();
    backend.expectNone((request) => request.url === PLATFORM_API.dashboard);
    expect(Array.from(root.querySelectorAll('app-stat-card')).every((tile) => tile.textContent?.includes('—'))).toBe(true);
  });
});
