import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { ACCOUNT_API, type BranchDashboard, type MyAccountProfile, type SupportTicketDetail } from '../../../core/api/account.contracts';
import { AUTH_API, type AuthProfile, type AuthResponse } from '../../../core/api/auth.contracts';
import { AUDIT_API } from '../../../core/api/audit.contracts';
import { ORGANIZATION_API } from '../../../core/api/organization.contracts';
import { PLATFORM_TENANT_ID } from '../../../core/tenancy/tenant-ids';
import { AuthStore } from '../../../core/auth/auth.store';
import { I18nService } from '../../../core/i18n/i18n.service';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { AuditLogFacade } from '../state/audit-log.facade';
import { BranchSelectionFacade } from '../state/branch-selection.facade';
import { DashboardFacade } from '../state/dashboard.facade';
import { ProfileFacade } from '../state/profile.facade';
import { SupportFacade } from '../state/support.facade';
import { AuditLogPage } from './audit-log.page';
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
        AuditLogFacade,
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
    await signIn({ permissions: ['dashboard.view', 'wallet.view', 'billing.view'] });
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

  it('CP-05 on a past day: no "today" captions, the day and every figure in Arabic-Indic digits, activity in words (UAT A-12)', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input: string | URL | Request) =>
      new Response(
        JSON.stringify(
          String(input) === '/i18n/account.ar.json'
            ? {
                'dashboard.today': 'اليوم',
                'dashboard.onDay': 'في {date}',
                'dashboard.kpi.collected': 'المحصّل',
                'dashboard.kpi.collectedToday': 'المحصّل اليوم',
                'dashboard.chartBar': '{date}: {count} تسجيلات',
                'dashboard.last7': 'التسجيلات — آخر {days} أيام',
                'audit.actionName.ResultVerified': 'اعتماد نتيجة',
              }
            : {},
        ),
        { status: 200 },
      ),
    );
    await signIn({ permissions: ['dashboard.view', 'audit.view', 'billing.view'] });
    const i18n = TestBed.inject(I18nService);
    i18n.setLanguage('ar');
    await i18n.load('account');
    TestBed.inject(DashboardFacade).setDate('2020-01-05');
    const fixture = TestBed.createComponent(DashboardPage);
    await render(fixture);
    backend.expectOne((r) => r.url === ACCOUNT_API.dashboard).flush({
      branchId: 'b1',
      date: '2020-01-05',
      fromUtc: '2020-01-04T22:00:00Z',
      toUtc: '2020-01-05T22:00:00Z',
      currencyCode: 'EGP',
      kpis: { registrations: 16, collectedToday: 2436, samplesReceived: null, pendingResults: 14, unverifiedBacklog: 2, delayedTests: 0, rejectedSamples: null, walletBalance: null, walletCurrency: null },
      registrationsByDay: [{ date: '2020-01-05', count: 16 }],
      delayedTests: [],
    } satisfies BranchDashboard);
    backend.expectOne(ORGANIZATION_API.selectableBranches).flush({ currentBranchId: 'b1', branches: [{ id: 'b1', code: 'MAIN', nameEn: 'Main', nameAr: 'الرئيسي' }] });
    backend.expectOne((r) => r.url === '/api/v1/audit/entries').flush({
      items: [{ id: 'e1', kind: 'Event', atUtc: '2020-01-05T08:00:00Z', actorUserId: 'u1', actorName: 'Mona', entityType: 'ResultLogEntry', entityId: 'r1', action: 'ResultVerified', correlationId: null, changes: null, reason: null, sequence: 1, payload: '{}' }],
      page: 1,
      pageSize: 8,
      totalCount: 1,
      totalPages: 1,
    });
    await render(fixture);

    const root = fixture.nativeElement as HTMLElement;
    const tiles = [...root.querySelectorAll('app-stat-card')].map((tile) => tile.textContent ?? '');
    expect(tiles.join(' ')).not.toContain('اليوم');
    expect(tiles.join(' ')).toContain('المحصّل');
    expect(tiles.join(' ')).not.toContain('المحصّل اليوم');
    expect(tiles[0]).toContain('١٦');
    const chart = root.querySelector('app-bar-chart')?.textContent ?? '';
    expect(chart).toContain('١٦');
    expect(chart).not.toMatch(/[0-9]/);   // values and descriptions: no Latin digits next to Arabic-Indic tiles
    expect(root.textContent).toContain('آخر ٧ أيام');   // the caption's day count in the interface digits (UAT R1-06)
    const header = root.querySelector('app-page-header')?.textContent ?? '';
    expect(header).not.toContain('2020-01-05');
    expect(header).toContain('٢٠٢٠');
    const activity = root.querySelector('.activity')?.textContent ?? '';
    expect(activity).toContain('اعتماد نتيجة');
    expect(activity).toContain('Result log entry');   // no dictionary entry here: words, not the type name
    expect(activity).not.toContain('ResultVerified');
    expect(activity).not.toContain('ResultLogEntry');
    expect(root.querySelector('a[href="/account/audit"]')).toBeNull();
    expect(root.textContent).not.toContain('account.dashboard.openAudit');   // CP-68 is Release 2: no "Open audit" link
  });

  it('CP-05 reads a day with one registration in the singular, in both languages (validation v0.5.0 R-10)', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input: string | URL | Request) => {
      const url = String(input);
      const dictionary = url === '/i18n/account.en.json'
        ? { 'dashboard.chartBar': '{date}: {count} registrations', 'dashboard.chartBarOne': '{date}: 1 registration' }
        : url === '/i18n/account.ar.json'
          ? { 'dashboard.chartBar': '{date}: {count} تسجيلات', 'dashboard.chartBarOne': '{date}: تسجيل واحد' }
          : {};
      return new Response(JSON.stringify(dictionary), { status: 200 });
    });
    await signIn({ permissions: ['dashboard.view'] });
    const i18n = TestBed.inject(I18nService);
    await i18n.load('account');
    const fixture = TestBed.createComponent(DashboardPage);
    await render(fixture);
    backend.expectOne((r) => r.url === ACCOUNT_API.dashboard).flush({
      branchId: 'b1',
      date: '2026-10-12',
      fromUtc: '2026-10-11T21:00:00Z',
      toUtc: '2026-10-12T21:00:00Z',
      currencyCode: 'EGP',
      kpis: { registrations: 1, collectedToday: 0, samplesReceived: null, pendingResults: 0, unverifiedBacklog: 0, delayedTests: 0, rejectedSamples: null, walletBalance: null, walletCurrency: null },
      registrationsByDay: [{ date: '2026-10-11', count: 3 }, { date: '2026-10-12', count: 1 }],
      delayedTests: [],
    } satisfies BranchDashboard);
    backend.expectOne(ORGANIZATION_API.selectableBranches).flush({ currentBranchId: 'b1', branches: [{ id: 'b1', code: 'MAIN', nameEn: 'Main', nameAr: 'الرئيسي' }] });
    await render(fixture);

    const descriptions = (): string[] => [...(fixture.nativeElement as HTMLElement).querySelectorAll('app-bar-chart li')].map((item) => item.textContent?.trim() ?? '');
    expect(descriptions()).toHaveLength(2);
    expect(descriptions()[0]).toMatch(/: 3 registrations$/);
    expect(descriptions()[1]).toMatch(/: 1 registration$/);

    i18n.setLanguage('ar');
    await i18n.load('account');
    await render(fixture);
    expect(descriptions()[0]).toMatch(/: ٣ تسجيلات$/);
    expect(descriptions()[1]).toMatch(/: تسجيل واحد$/);
    i18n.setLanguage('en');
  });

  it('CP-05 lists the delayed tests the tile counts: accession, patient, test, delay and status (UAT R1-02)', async () => {
    await signIn({ permissions: ['dashboard.view'] });
    const fixture = TestBed.createComponent(DashboardPage);
    await render(fixture);
    backend.expectOne((r) => r.url === ACCOUNT_API.dashboard).flush({
      branchId: 'b1',
      date: '2026-10-11',
      fromUtc: '2026-10-10T21:00:00Z',
      toUtc: '2026-10-11T21:00:00Z',
      currencyCode: 'EGP',
      kpis: { registrations: 3, collectedToday: 0, samplesReceived: null, pendingResults: 2, unverifiedBacklog: 0, delayedTests: 2, rejectedSamples: null, walletBalance: null, walletCurrency: null },
      registrationsByDay: [{ date: '2026-10-11', count: 3 }],
      delayedTests: [
        { visitId: 'v1', accession: 'ACC-2026-00012', patientName: 'Mona Adel', testName: 'Glucose', testNameAr: 'جلوكوز', expectedAtUtc: '2026-10-11T12:00:00Z', delayMinutes: 125, status: 'Ordered' },
        { visitId: 'v2', accession: 'ACC-2026-00013', patientName: null, testName: 'Urea', expectedAtUtc: '2026-10-11T13:40:00Z', delayMinutes: 25, status: 'Reviewed' },
      ],
    } satisfies BranchDashboard);
    backend.expectOne(ORGANIZATION_API.selectableBranches).flush({ currentBranchId: 'b1', branches: [{ id: 'b1', code: 'MAIN', nameEn: 'Main', nameAr: 'الرئيسي' }] });
    await render(fixture);

    const rows = [...(fixture.nativeElement as HTMLElement).querySelectorAll('app-data-table tbody tr')].map((row) => row.textContent ?? '');
    expect(rows.length).toBe(2);
    expect(rows[0]).toContain('ACC-2026-00012');
    expect(rows[0]).toContain('Mona Adel');
    expect(rows[0]).toContain('Glucose');
    expect(rows[0]).toContain('Ordered');
    expect(rows[1]).toContain('ACC-2026-00013');
    expect(rows[1]).toContain('Urea');
    expect(rows[1]).toContain('Reviewed');
    // UAT R3-03: the due time is shown (local date-time of expectedAtUtc), beside the delay.
    const headers = [...(fixture.nativeElement as HTMLElement).querySelectorAll('app-data-table thead th')].map((cell) => cell.textContent ?? '');
    expect(headers.some((text) => /dashboard.due|Due/.test(text))).toBe(true);
    expect(rows[0]).toMatch(/2026|11/);
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('account.dashboard.noDelays');
  });

  it('CP-65 announces the saved language in the new language, once its dictionary is loaded (UAT A-07 / C-10)', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input: string | URL | Request) =>
      new Response(JSON.stringify(String(input) === '/i18n/account.ar.json' ? { 'profile.languageSaved': 'تم حفظ اللغة.' } : {}), { status: 200 }),
    );
    const toast = vi.spyOn(TestBed.inject(ToastService), 'success');
    await signIn({});
    const fixture = TestBed.createComponent(ProfilePage);
    await render(fixture);
    backend.expectOne(ACCOUNT_API.profile).flush({ userId: 'u1', language: 'en', tenantId: 't1', passwordPolicy: { minLength: 8, maxLength: 128 } });
    await render(fixture);

    const page = fixture.componentInstance as unknown as { language: { setValue(value: string): void }; saveLanguage(): Promise<void> };
    page.language.setValue('ar');
    const saving = page.saveLanguage();
    await settle();
    backend.expectOne(ACCOUNT_API.preferences).flush(null, { status: 204, statusText: 'No Content' });
    await saving;

    expect(TestBed.inject(I18nService).lang()).toBe('ar');
    expect(toast).toHaveBeenCalledWith('تم حفظ اللغة.');
    expect(toast).not.toHaveBeenCalledWith('account.profile.languageSaved');
  });

  it('SA-09 shows the client of each entry and offers the client filter in the platform log only (UAT C-18)', async () => {
    await signIn({ tenantId: PLATFORM_TENANT_ID, permissions: ['audit.view'] });
    const fixture = TestBed.createComponent(AuditLogPage);
    await render(fixture);
    backend.expectOne((r) => r.url === AUDIT_API.entries).flush({
      items: [
        { id: 'e1', kind: 'Event', atUtc: '2026-10-11T08:00:00Z', actorUserId: 'u1', actorName: 'Admin', entityType: 'Tenant', entityId: 'lab-a', action: 'TenantPlanChanged', correlationId: null, changes: null, reason: null, sequence: 56, payload: '{}', clientTenantId: 'lab-a', clientNameEn: 'V11 Validation Lab', clientNameAr: 'مختبر التحقق' },
        { id: 'e2', kind: 'FieldChange', atUtc: '2026-10-11T07:00:00Z', actorUserId: 'u1', actorName: 'Admin', entityType: 'Plan', entityId: 'p1', action: 'Modified', correlationId: null, changes: [], reason: null, sequence: null, payload: null, clientTenantId: null },
      ],
      page: 1,
      pageSize: 50,
      totalCount: 2,
      totalPages: 1,
    });
    backend.expectOne(ACCOUNT_API.auditActors).flush([]);
    backend.expectOne(AUDIT_API.clients).flush([{ tenantId: 'lab-a', nameEn: 'V11 Validation Lab', nameAr: 'مختبر التحقق', portalSlug: 'v11' }]);
    await render(fixture);

    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('account.audit.client');
    expect(root.textContent).toContain('V11 Validation Lab');
    const options = [...root.querySelectorAll<HTMLOptionElement>('select[formcontrolname=clientTenantId] option')].map((option) => option.value);
    expect(options).toEqual(['', 'lab-a']);
    // C-12: the entity hint names Super Admin entities in the platform log
    expect(root.textContent).toContain('account.audit.entityHintPlatform');
  });

  it('CP-68 a laboratory log has no client column or filter', async () => {
    await signIn({ permissions: ['audit.view'] });
    const fixture = TestBed.createComponent(AuditLogPage);
    await render(fixture);
    backend.expectOne((r) => r.url === AUDIT_API.entries).flush({ items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 });
    backend.expectOne(ACCOUNT_API.auditActors).flush([]);
    backend.expectOne(AUDIT_API.clients).flush([]);
    await render(fixture);

    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).not.toContain('account.audit.client');
    expect(root.querySelector('select[formcontrolname=clientTenantId]')).toBeNull();
    expect(root.textContent).toContain('account.audit.entityHint');
    expect(root.textContent).not.toContain('account.audit.entityHintPlatform');
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
    // UAT R3-04: no internal identifier of the laboratory on the page.
    expect(root.textContent).not.toContain('account.profile.tenantId');
    expect(root.querySelector('.layout')?.textContent ?? '').not.toMatch(/\bt1\b/);
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
