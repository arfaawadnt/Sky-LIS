import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { ACCOUNT_API, type BranchDashboard, type MyAccountProfile, type SupportTicketDetail } from '../../../core/api/account.contracts';
import { AUTH_API, type AuthProfile, type AuthResponse } from '../../../core/api/auth.contracts';
import { ORGANIZATION_API } from '../../../core/api/organization.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { BranchSelectionFacade } from '../state/branch-selection.facade';
import { DashboardFacade } from '../state/dashboard.facade';
import { ProfileFacade } from '../state/profile.facade';
import { SupportFacade } from '../state/support.facade';
import { DashboardPage } from './dashboard.page';
import { ProfilePage } from './profile.page';
import { SelectBranchPage } from './select-branch.page';
import { SupportPage } from './support.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

async function signIn(profile: Partial<AuthProfile>): Promise<void> {
  const pending = TestBed.inject(AuthStore).signIn('user', 'pw', null);
  TestBed.inject(HttpTestingController).expectOne(AUTH_API.signIn).flush({
    step: 'signed-in',
    accessToken: 't',
    expiresAtUtc: null,
    refreshToken: null,
    profile: { userId: 'u1', displayName: 'Mona', tenantId: 't1', roleName: null, permissions: [], language: 'en', scope: 'full', branchId: 'b1', branchIds: ['b1'], ...profile },
    tenants: null,
  } satisfies AuthResponse);
  await pending;
}

async function render<T>(fixture: ComponentFixture<T>): Promise<void> {
  fixture.detectChanges();
  TestBed.tick();
  await settle();
  fixture.detectChanges();
}

describe('Account pages', () => {
  let backend: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        BranchSelectionFacade,
        DashboardFacade,
        ProfileFacade,
        SupportFacade,
      ],
    });
    backend = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
  });

  it('CP-04 selects the only branch at once and continues to the dashboard', async () => {
    await signIn({ branchId: null, branchIds: [] });
    const fixture = TestBed.createComponent(SelectBranchPage);
    await render(fixture);
    backend.expectOne(ORGANIZATION_API.selectableBranches).flush({ currentBranchId: null, branches: [{ id: 'b7', code: 'MAIN', nameEn: 'Main', nameAr: 'الرئيسي' }] });
    await render(fixture);

    const select = backend.expectOne(AUTH_API.selectBranch);
    expect(select.request.body).toEqual({ branchId: 'b7' });
    select.flush({ userId: 'u1', displayName: 'Mona', tenantId: 't1', roleName: null, permissions: [], language: 'en', scope: 'full', branchId: 'b7', branchIds: [] } satisfies AuthProfile);
    await settle();
    await settle();
    expect(TestBed.inject(AuthStore).currentBranch()).toBe('b7');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/account/dashboard');
  });

  it('CP-04 offers several branches as cards and selects the one clicked', async () => {
    await signIn({ branchId: null, branchIds: ['b1', 'b2'] });
    const fixture = TestBed.createComponent(SelectBranchPage);
    fixture.componentRef.setInput('returnUrl', '/samples');
    await render(fixture);
    backend.expectOne(ORGANIZATION_API.selectableBranches).flush({
      currentBranchId: null,
      branches: [
        { id: 'b1', code: 'MAIN', nameEn: 'Main', nameAr: 'الرئيسي' },
        { id: 'b2', code: 'NASR', nameEn: 'Nasr City', nameAr: 'مدينة نصر' },
      ],
    });
    await render(fixture);
    backend.expectNone(AUTH_API.selectBranch);

    const cards = (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button.branch');
    expect(cards.length).toBe(2);
    cards[1]?.click();
    await settle();
    backend.expectOne(AUTH_API.selectBranch).flush({ userId: 'u1', displayName: 'Mona', tenantId: 't1', roleName: null, permissions: [], language: 'en', scope: 'full', branchId: 'b2' } satisfies AuthProfile);
    await settle();
    await settle();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/samples');
  });

  it('CP-05 shows the figures, "—" for what is not live yet, and the 7-day chart', async () => {
    await signIn({ permissions: ['dashboard.view', 'wallet.view'] });
    const fixture = TestBed.createComponent(DashboardPage);
    await render(fixture);
    const dashboard: BranchDashboard = {
      branchId: 'b1',
      date: '2026-10-09',
      fromUtc: '2026-10-08T21:00:00Z',
      toUtc: '2026-10-09T21:00:00Z',
      currencyCode: 'EGP',
      kpis: { registrations: 42, collectedToday: 1500, samplesReceived: 3, pendingResults: null, unverifiedBacklog: null, delayedTests: null, rejectedSamples: 0, walletBalance: -10, walletCurrency: 'EGP' },
      registrationsByDay: ['03', '04', '05', '06', '07', '08', '09'].map((day, index) => ({ date: `2026-10-${day}`, count: index * 2 })),
      delayedTests: [],
    };
    backend.expectOne((r) => r.url === ACCOUNT_API.dashboard).flush(dashboard);
    backend.expectOne(ORGANIZATION_API.selectableBranches).flush({ currentBranchId: 'b1', branches: [{ id: 'b1', code: 'MAIN', nameEn: 'Main', nameAr: 'الرئيسي' }] });
    await render(fixture);

    const root = fixture.nativeElement as HTMLElement;
    const tiles = root.querySelectorAll('app-stat-card');
    // Release 1 (prototype v2.1): registrations, collected today, pending results, unverified backlog, delayed tests + the
    // wallet (wallet.view); the two sample tiles belong to Release 2 and stay hidden.
    expect(tiles.length).toBe(6);
    expect(tiles[0]?.textContent).toContain('42');
    expect(tiles[2]?.textContent).toContain('—');   // pending results: Results is not live
    expect(root.textContent).not.toContain('account.dashboard.kpi.samplesReceived');
    expect(root.querySelectorAll('app-bar-chart rect').length).toBe(7);
    expect(root.textContent).toContain('MAIN');
  });

  it('CP-65 shows the password rule live and does not send a password that contains the username', async () => {
    await signIn({});
    const fixture = TestBed.createComponent(ProfilePage);
    await render(fixture);
    backend.expectOne(ACCOUNT_API.profile).flush({
      userId: 'u1',
      username: 'lab.admin',
      displayName: 'Mona',
      email: 'mona@example.test',
      mobile: null,
      language: 'en',
      tenantId: 't1',
      isPlatform: false,
      fullNameEn: null,
      fullNameAr: null,
      employeeCode: null,
      designation: null,
      roleNameEn: 'Administrator',
      roleNameAr: 'مسؤول',
      mustChangePassword: true,
      passwordChangedAtUtc: '2026-10-01T08:00:00Z',
      passwordExpiresAtUtc: null,
      passwordPolicy: { minLength: 10, maxLength: 128 },
      laboratory: { tenantId: 't1', nameEn: null, nameAr: null, portalUrl: null, countryNameEn: null, countryNameAr: null, planCode: null, planNameEn: null, planNameAr: null, managerName: null, managerMobile: null, mobile: null, email: null, status: null, trialEndsAtUtc: null },
    } satisfies MyAccountProfile);
    await render(fixture);

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.warn-strip')).not.toBeNull();   // must change
    expect(root.querySelector('.info-strip')?.textContent).toContain('account.profile.laboratoryUnavailable');
    const type = (name: string, value: string): void => {
      const input = root.querySelector<HTMLInputElement>(`input[formcontrolname=${name}]`);
      if (input) {
        input.value = value;
        input.dispatchEvent(new Event('input'));
      }
    };
    type('current', 'old password');
    type('next', 'my-lab.admin-key');
    type('confirmation', 'my-lab.admin-key');
    fixture.detectChanges();
    expect(root.querySelectorAll('.rules li.ok').length).toBe(3);   // length, differs, matches — not "no username"
    root.querySelector<HTMLButtonElement>('form button[type=submit]')?.click();
    await settle();
    backend.expectNone(ACCOUNT_API.password);
  });

  it('CP-67 opens the conversation of a ticket from the register', async () => {
    await signIn({ permissions: ['support.view', 'support.create'] });
    const fixture = TestBed.createComponent(SupportPage);
    await render(fixture);
    backend.expectOne((r) => r.url === ACCOUNT_API.tickets).flush({
      items: [{ id: 'tk1', number: 'TKT-2026-00001', tenantId: 't1', laboratoryNameEn: null, laboratoryNameAr: null, subject: 'Printer', priority: 'High', status: 'Resolved', openedByUserId: 'u1', openedByName: 'Mona', openedAtUtc: '2026-10-09T08:00:00Z', lastActivityAtUtc: '2026-10-09T09:00:00Z', replyCount: 1, lastReplyFromPlatform: true }],
      page: 1,
      pageSize: 20,
      totalCount: 1,
      totalPages: 1,
    });
    await render(fixture);

    TestBed.inject(SupportFacade).select('tk1');
    await render(fixture);
    const detail: SupportTicketDetail = {
      id: 'tk1',
      number: 'TKT-2026-00001',
      tenantId: 't1',
      laboratoryNameEn: null,
      laboratoryNameAr: null,
      subject: 'Printer',
      description: 'Labels cut',
      priority: 'High',
      status: 'Resolved',
      openedByUserId: 'u1',
      openedByName: 'Mona',
      openedAtUtc: '2026-10-09T08:00:00Z',
      lastActivityAtUtc: '2026-10-09T09:00:00Z',
      resolvedAtUtc: '2026-10-09T09:00:00Z',
      closedAtUtc: null,
      replies: [{ id: 'r1', authorUserId: 'nt', authorName: 'NT Support', text: 'Fixed', fromPlatform: true, atUtc: '2026-10-09T09:00:00Z' }],
    };
    backend.expectOne(`${ACCOUNT_API.tickets}/tk1`).flush(detail);
    await render(fixture);

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('app-ticket-thread')).not.toBeNull();
    expect(root.querySelector('.message.platform')?.textContent).toContain('Fixed');
    const buttons = [...root.querySelectorAll<HTMLButtonElement>('app-ticket-thread .actions button')].map((button) => button.textContent?.trim());
    expect(buttons).toEqual(['account.support.reopen', 'account.support.close']);
  });
});
