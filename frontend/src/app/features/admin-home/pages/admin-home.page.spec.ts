import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { PLATFORM_API, type PlatformDashboard } from '../../../core/api/platform.contracts';
import type { AuthProfile } from '../../../core/api/auth.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { I18nService } from '../../../core/i18n/i18n.service';
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
  silentClients: 0,
  silentTrialClients: 0,
  silentDays: 3,
};

/** UAT R2-05: the Super Admin home shows the Release-1 tiles only, filled from the platform dashboard the dashboard page reads. */
describe('AdminHomePage (SA-01)', () => {
  let backend: HttpTestingController;
  let allowed = true;
  const profile = signal<AuthProfile | null>(null);

  beforeEach(async () => {
    allowed = true;
    profile.set(null);
    await TestBed.configureTestingModule({
      imports: [AdminHomePage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthStore, useValue: { hasPermission: (key: string) => allowed && key === PLATFORM_VIEW, profile } },
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

  it('greets the user by the name of the language on screen, following a switch both ways (round 2 R2-03)', async () => {
    // The language of the sign-in decides displayName; the greeting must not keep it after a switch.
    profile.set({ userId: 'u-1', displayName: 'مدير المنصة', displayNameEn: 'Platform Administrator', displayNameAr: 'مدير المنصة', tenantId: null, roleName: null, permissions: [PLATFORM_VIEW], language: 'ar', scope: 'platform' });
    const i18n = TestBed.inject(I18nService);
    i18n.setLanguage('en');
    const fixture = TestBed.createComponent(AdminHomePage);
    const root = fixture.nativeElement as HTMLElement;
    const greeting = (): string => root.querySelector('app-section-card strong')?.textContent?.trim() ?? '';
    const step = async (): Promise<void> => {
      TestBed.tick();
      await settle();
      backend.match((request) => request.url.startsWith('/i18n/')).forEach((request) => request.flush({}));
      fixture.detectChanges();
    };
    await step();
    backend.match((request) => request.url === PLATFORM_API.dashboard).forEach((request) => request.flush(dashboard));
    await step();
    expect(greeting()).toBe('Platform Administrator');

    i18n.setLanguage('ar');
    await step();
    expect(greeting()).toBe('مدير المنصة');

    i18n.setLanguage('en');
    await step();
    expect(greeting()).toBe('Platform Administrator');
  });
});
