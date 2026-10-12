import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { ACCOUNT_API } from '../../../core/api/account.contracts';
import { AUTH_API, type AuthResponse } from '../../../core/api/auth.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { localDay } from '../data/account-format';
import { DashboardFacade } from '../state/dashboard.facade';
import { DashboardPage } from './dashboard.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

/** Validation A-10 (2026-10-11): the date box accepted a future day, and clearing it kept the old day. */
describe('Dashboard day (CP-05, validation A-10)', () => {
  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        DashboardFacade,
      ],
    });
    const pending = TestBed.inject(AuthStore).signIn('user', 'pw', null);
    TestBed.inject(HttpTestingController).expectOne(AUTH_API.signIn).flush({
      step: 'signed-in',
      accessToken: 't',
      expiresAtUtc: null,
      refreshToken: null,
      profile: { userId: 'u1', displayName: 'Mona', tenantId: 't1', roleName: null, permissions: ['dashboard.view'], language: 'en', scope: 'full', branchId: 'b1', branchIds: ['b1'] },
      tenants: null,
    } satisfies AuthResponse);
    await pending;
  });

  async function render(fixture: ComponentFixture<DashboardPage>): Promise<void> {
    fixture.detectChanges();
    TestBed.tick();
    await settle();
    fixture.detectChanges();
  }

  it('shows no collected money to a user without the billing right (validation observation O-9)', async () => {
    const fixture = TestBed.createComponent(DashboardPage);
    await render(fixture);
    const tiles = (fixture.componentInstance as unknown as { kpis: () => readonly { key: string }[] }).kpis().map((kpi) => kpi.key);

    expect(tiles).toContain('registrations');
    expect(tiles).not.toContain('collectedToday');
  });

  it('keeps a past day, and returns to today for a future, an empty or a malformed day', () => {
    const facade = TestBed.inject(DashboardFacade);
    const today = localDay();

    expect(facade.setDate('2020-02-29')).toBe('2020-02-29');
    expect(facade.query().date).toBe('2020-02-29');

    expect(facade.setDate('2999-01-01')).toBe(today);
    expect(facade.query().date).toBe(today);

    facade.setDate('2020-02-29');
    expect(facade.setDate('')).toBe(today);
    expect(facade.setDate('29/02/2020')).toBe(today);
    expect(facade.maxDate()).toBe(today);
  });

  it('offers no day after today and shows today again when the box is cleared', async () => {
    const fixture = TestBed.createComponent(DashboardPage);
    await render(fixture);
    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('input[type="date"]');
    if (input === null) {
      throw new Error('date box missing');
    }

    expect(input.getAttribute('max')).toBe(localDay());

    input.value = '2020-02-29';
    input.dispatchEvent(new Event('change'));
    await render(fixture);
    expect(TestBed.inject(DashboardFacade).query().date).toBe('2020-02-29');

    input.value = '';
    input.dispatchEvent(new Event('change'));
    await render(fixture);
    expect(input.value).toBe(localDay());
    expect(TestBed.inject(DashboardFacade).query().date).toBe(localDay());

    const backend = TestBed.inject(HttpTestingController);
    const requested = backend.match((r) => r.url === ACCOUNT_API.dashboard).map((call) => call.request.params.get('date'));
    expect(requested.every((date) => date !== null && date <= localDay())).toBe(true);
  });
});
