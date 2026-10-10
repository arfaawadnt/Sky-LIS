import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { ACCOUNT_API, type BranchDashboard, type MyAccountProfile } from '../../../core/api/account.contracts';
import { AUDIT_API } from '../../../core/api/audit.contracts';
import { AUTH_API, type AuthResponse } from '../../../core/api/auth.contracts';
import { ORGANIZATION_API } from '../../../core/api/organization.contracts';
import { PLATFORM_API } from '../../../core/api/platform.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { I18nService } from '../../../core/i18n/i18n.service';
import { fileNameOf } from '../data/account.api';
import { AuditLogFacade, dayToInstant } from './audit-log.facade';
import { DashboardFacade } from './dashboard.facade';
import { ProfileFacade } from './profile.facade';
import { SupportDeskFacade, SupportFacade } from './support.facade';
import { WalletFacade } from './wallet.facade';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));
const page = <T>(items: T[]) => ({ items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1 });

async function runEffects(): Promise<void> {
  TestBed.tick();
  await settle();
}

async function signIn(permissions: readonly string[]): Promise<void> {
  const pending = TestBed.inject(AuthStore).signIn('user', 'pw', null);
  TestBed.inject(HttpTestingController).expectOne(AUTH_API.signIn).flush({
    step: 'signed-in',
    accessToken: 't',
    expiresAtUtc: null,
    refreshToken: null,
    profile: { userId: 'u1', displayName: 'Mona', tenantId: 't1', roleName: null, permissions, language: 'en', scope: 'full', branchId: 'b1', branchIds: ['b1'] },
    tenants: null,
  } satisfies AuthResponse);
  await pending;
}

const DASHBOARD: BranchDashboard = {
  branchId: 'b1',
  date: '2026-10-09',
  fromUtc: '2026-10-08T21:00:00Z',
  toUtc: '2026-10-09T21:00:00Z',
  currencyCode: 'EGP',
  kpis: { registrations: 12, collectedToday: 1500, samplesReceived: null, pendingResults: null, unverifiedBacklog: null, delayedTests: null, rejectedSamples: null, walletBalance: null, walletCurrency: null },
  registrationsByDay: [{ date: '2026-10-09', count: 12 }],
  delayedTests: [],
};

describe('Account facades (CP-05, CP-65, CP-66, CP-67, CP-68)', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        DashboardFacade,
        ProfileFacade,
        WalletFacade,
        SupportFacade,
        SupportDeskFacade,
        AuditLogFacade,
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  it('dashboard: asks for the local day with the offset, names the branch and reads the activity only with audit.view', async () => {
    await signIn(['dashboard.view']);
    const facade = TestBed.inject(DashboardFacade);
    facade.setDate('2026-10-09');
    await runEffects();

    const request = backend.expectOne((r) => r.url === ACCOUNT_API.dashboard);
    expect(request.request.params.get('date')).toBe('2026-10-09');
    expect(request.request.params.get('utcOffsetMinutes')).toMatch(/^-?\d+$/);
    request.flush(DASHBOARD);
    backend.expectOne(ORGANIZATION_API.selectableBranches).flush({ currentBranchId: 'b1', branches: [{ id: 'b1', code: 'MAIN', nameEn: 'Main', nameAr: 'الرئيسي' }] });
    backend.expectNone(AUDIT_API.entries);
    await runEffects();

    expect(facade.dashboard.value()?.kpis.registrations).toBe(12);
    expect(facade.branch()?.code).toBe('MAIN');
    expect(facade.canSeeActivity()).toBe(false);
    backend.verify();
  });

  it('dashboard: with audit.view the recent activity is the newest page of the audit log', async () => {
    await signIn(['dashboard.view', 'audit.view']);
    const facade = TestBed.inject(DashboardFacade);
    await runEffects();
    backend.expectOne((r) => r.url === ACCOUNT_API.dashboard).flush(DASHBOARD);
    backend.expectOne(ORGANIZATION_API.selectableBranches).flush([]);   // an older bare-array shape is accepted
    const activity = backend.expectOne((r) => r.url === AUDIT_API.entries);
    expect(activity.request.params.get('pageSize')).toBe('8');
    activity.flush(page([]));
    await runEffects();
    expect(facade.activity.value()?.items).toEqual([]);
  });

  it('profile: saving the language stores it, switches the interface and refreshes the session', async () => {
    await signIn([]);
    const facade = TestBed.inject(ProfileFacade);
    await runEffects();
    backend.expectOne(ACCOUNT_API.profile).flush({ userId: 'u1', tenantId: 't1', language: 'en' } as MyAccountProfile);

    const saved = facade.saveLanguage.execute('ar');
    await settle();
    const put = backend.expectOne(ACCOUNT_API.preferences);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ language: 'ar' });
    put.flush(null, { status: 204, statusText: 'No Content' });
    expect(await saved).toBe('ar');
    expect(TestBed.inject(I18nService).lang()).toBe('ar');
    backend.expectOne(AUTH_API.refresh);
  });

  it('profile: the password change posts both values (sealed by the interceptor) and surfaces ACC-010', async () => {
    await signIn([]);
    const facade = TestBed.inject(ProfileFacade);
    await runEffects();
    backend.expectOne(ACCOUNT_API.profile).flush({ userId: 'u1', tenantId: 't1', language: 'en' } as MyAccountProfile);

    const changed = facade.changePassword.execute({ currentPassword: 'old one', newPassword: 'a quiet morning walk' });
    await settle();
    const post = backend.expectOne(ACCOUNT_API.password);
    expect(post.request.body).toEqual({ currentPassword: 'old one', newPassword: 'a quiet morning walk' });
    post.flush({ code: 'ACC-010', title: 'Wrong', status: 403 }, { status: 403, statusText: 'Forbidden' });
    expect(await changed).toBeNull();
    expect(facade.changePassword.problem()?.code).toBe('ACC-010');
  });

  it('wallet: reads the wallet and the movements by kind, and reloads after a top-up request', async () => {
    const facade = TestBed.inject(WalletFacade);
    await runEffects();
    backend.expectOne(PLATFORM_API.myWallet).flush({ balance: 100, currencyCode: 'EGP', pendingTopUpRequests: [] });
    backend.expectOne((r) => r.url === `${PLATFORM_API.myWallet}/transactions`).flush(page([]));

    facade.setKind('TopUp');
    await runEffects();
    const filtered = backend.expectOne((r) => r.url === `${PLATFORM_API.myWallet}/transactions`);
    expect(filtered.request.params.get('kind')).toBe('TopUp');
    filtered.flush(page([]));

    const requested = facade.requestTopUp.execute({ amount: 500, note: null });
    await settle();
    const post = backend.expectOne(`${PLATFORM_API.myWallet}/top-up-requests`);
    expect(post.request.body).toEqual({ amount: 500, note: null });
    post.flush({ id: 'r1' }, { status: 201, statusText: 'Created' });
    await requested;
    await runEffects();
    backend.expectOne(PLATFORM_API.myWallet).flush({ balance: 100, currencyCode: 'EGP', pendingTopUpRequests: [] });
    backend.verify();
  });

  it('support: filters the register, opens a ticket and selects it, replies and reloads', async () => {
    const facade = TestBed.inject(SupportFacade);
    await runEffects();
    backend.expectOne((r) => r.url === ACCOUNT_API.tickets).flush(page([]));

    facade.setStatus('Open');
    facade.setMine(true);
    facade.setSearch('  printer ');
    await runEffects();
    const filtered = backend.expectOne((r) => r.url === ACCOUNT_API.tickets);
    expect([filtered.request.params.get('status'), filtered.request.params.get('mine'), filtered.request.params.get('search')]).toEqual(['Open', 'true', 'printer']);
    filtered.flush(page([]));

    const opened = facade.open.execute({ subject: 'Printer', description: 'Labels cut', priority: 'High' });
    await settle();
    backend.expectOne((r) => r.url === ACCOUNT_API.tickets && r.method === 'POST').flush({ id: 'tk1' }, { status: 201, statusText: 'Created' });
    await opened;
    await runEffects();
    expect(facade.selectedId()).toBe('tk1');
    backend.expectOne((r) => r.url === ACCOUNT_API.tickets && r.method === 'GET').flush(page([]));
    backend.expectOne(`${ACCOUNT_API.tickets}/tk1`).flush({ id: 'tk1', replies: [] });

    const replied = facade.reply.execute({ id: 'tk1', text: 'Any news?' });
    await settle();
    const reply = backend.expectOne(`${ACCOUNT_API.tickets}/tk1/replies`);
    expect(reply.request.body).toEqual({ text: 'Any news?' });
    reply.flush({ id: 'r1' }, { status: 201, statusText: 'Created' });
    expect(await replied).toEqual({ id: 'r1' });
    await runEffects();
    backend.expectOne((r) => r.url === ACCOUNT_API.tickets && r.method === 'GET').flush(page([]));
    backend.expectOne(`${ACCOUNT_API.tickets}/tk1`).flush({ id: 'tk1', replies: [{ id: 'r1' }] });
    backend.verify();
  });

  it('desk: starts on open tickets, narrows to a laboratory and resolves with a note', async () => {
    const facade = TestBed.inject(SupportDeskFacade);
    await runEffects();
    const first = backend.expectOne((r) => r.url === ACCOUNT_API.deskTickets);
    expect(first.request.params.get('status')).toBe('Open');
    first.flush(page([]));

    facade.setLaboratory('lab-a');
    await runEffects();
    expect(backend.expectOne((r) => r.url === ACCOUNT_API.deskTickets).request.params.get('tenantId')).toBe('lab-a');

    facade.select('tk9');
    const resolved = facade.resolve.execute({ id: 'tk9', note: 'Driver updated' });
    await settle();
    const post = backend.expectOne(`${ACCOUNT_API.deskTickets}/tk9/resolve`);
    expect(post.request.body).toEqual({ note: 'Driver updated' });
    post.flush(null, { status: 204, statusText: 'No Content' });
    expect(await resolved).toBeUndefined();
  });

  it('audit: applies local days as instants, reads the actors and exports the filtered CSV', async () => {
    const facade = TestBed.inject(AuditLogFacade);
    await runEffects();
    backend.expectOne((r) => r.url === AUDIT_API.entries).flush(page([]));
    backend.expectOne(ACCOUNT_API.auditActors).flush([{ userId: 'u1', displayName: 'Mona', username: 'mona', isActive: true }]);

    facade.apply({ fromDay: '2026-10-01', toDay: '2026-10-09', userId: 'u1', entityType: ' Treasury ', entityId: '' });
    await runEffects();
    const filtered = backend.expectOne((r) => r.url === AUDIT_API.entries);
    expect(filtered.request.params.get('from')).toBe(dayToInstant('2026-10-01', false));
    expect(filtered.request.params.get('to')).toBe(dayToInstant('2026-10-09', true));
    expect(filtered.request.params.get('entityType')).toBe('Treasury');
    expect(filtered.request.params.has('entityId')).toBe(false);
    filtered.flush(page([]));

    const exported = facade.export.execute(undefined);
    await settle();
    const csv = backend.expectOne((r) => r.url === ACCOUNT_API.auditExport);
    expect(csv.request.params.get('userId')).toBe('u1');
    expect(csv.request.params.get('language')).toBe('en');
    csv.flush(new Blob(['x']), { headers: { 'Content-Disposition': 'attachment; filename=audit-log-2026-10-09-0830.csv' } });
    expect((await exported)?.fileName).toBe('audit-log-2026-10-09-0830.csv');

    const verified = facade.verify.execute(undefined);
    await settle();
    backend.expectOne(AUDIT_API.verifyChain).flush({ ok: false, checked: 41, firstBrokenSequence: 42 });
    await verified;
    expect(facade.verification()).toEqual({ ok: false, checked: 41, firstBrokenSequence: 42 });
  });

  it('reads file names from Content-Disposition and rejects invalid days', () => {
    expect(fileNameOf("attachment; filename=audit.csv; filename*=UTF-8''audit%20log.csv")).toBe('audit.csv');
    expect(fileNameOf("attachment; filename*=UTF-8''audit%20log.csv")).toBe('audit log.csv');
    expect(fileNameOf(null)).toBeNull();
    expect(dayToInstant('2026-13-45x', false)).toBeNull();
    expect(dayToInstant('', true)).toBeNull();
  });
});
