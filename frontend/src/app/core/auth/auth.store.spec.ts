import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection, resource } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ACCOUNT_API } from '../api/account.contracts';
import { AUTH_API, type AuthResponse } from '../api/auth.contracts';
import { AuthStore } from './auth.store';
import type { Portal } from '../portal/portal';
import { REFRESH_LOCK, type RefreshLock, refreshLockName } from './refresh-lock';
import { broadcastSessionChannel, readSessionMessage, sessionChannelName } from './session-channel';
import { portalAuthUrl, portalOfUrl } from './session-portal';
import { PLATFORM_TENANT_ID } from '../tenancy/tenant-ids';
import { idleSignInUrl, isLoginRoute, laboratorySlugOf, meaningfulReturnUrl, normaliseLaboratorySlug, signInUrl } from './session-navigation';
import { FakeBrowserNavigation, FakeSessionChannel, provideSessionTesting } from './session-testing';

function signedIn(token: string, branchId: string | null = null, userId = 'u1', tenantId = 't1'): AuthResponse {
  return {
    step: 'signed-in',
    accessToken: token,
    expiresAtUtc: '2026-10-09T12:15:00Z',
    refreshToken: null,
    profile: {
      userId,
      displayName: 'Mona Adel',
      tenantId,
      roleName: null,
      permissions: ['audit.view'],
      language: 'ar',
      scope: 'full',
      branchId,
      branchIds: ['b1', 'b2'],
    },
    tenants: null,
  };
}

describe('AuthStore (D-01, P-61)', () => {
  let store: AuthStore;
  let http: HttpTestingController;
  let navigation: FakeBrowserNavigation;
  let channel: FakeSessionChannel;

  beforeEach(() => {
    navigation = new FakeBrowserNavigation();
    channel = new FakeSessionChannel();
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), ...provideSessionTesting(navigation, channel)],
    });
    store = TestBed.inject(AuthStore);
    http = TestBed.inject(HttpTestingController);
    window.localStorage.clear();
    window.sessionStorage.clear();
  });

  afterEach(() => http.verify());

  it('keeps the access token in memory only and exposes identity from the profile', async () => {
    const pending = store.signIn('mona', 'secret', { laboratory: 'nile-lab' });
    const request = http.expectOne(AUTH_API.signIn);
    expect(request.request.body).toEqual({ username: 'mona', password: 'secret', laboratory: 'nile-lab' });   // LIS-CR-005: no tenant id
    request.flush(signedIn('token-1'));
    await pending;

    expect(store.token()).toBe('token-1');
    expect(store.isAuthenticated()).toBe(true);
    expect(store.currentUser()?.displayName).toBe('Mona Adel');
    expect(store.currentTenant()).toBe('t1');
    expect(store.hasPermission('audit.view')).toBe(true);
    expect(store.hasPermission('audit.export')).toBe(false);
    expect(window.localStorage.length).toBe(0);
    expect(window.sessionStorage.length).toBe(0);
    expect(document.cookie).toBe('');
  });

  it('refreshes single-flight: concurrent callers share one request', async () => {
    const first = store.refresh();
    const second = store.refresh();

    const requests = http.match(AUTH_API.refresh);
    expect(requests.length).toBe(1);
    requests[0]?.flush(signedIn('token-2'));

    expect(await first).toBe('token-2');
    expect(await second).toBe('token-2');
    expect(store.token()).toBe('token-2');

    const again = store.refresh();
    http.expectOne(AUTH_API.refresh).flush(signedIn('token-3'));
    expect(await again).toBe('token-3');
  });

  it('hydrate always resolves and leaves the store signed out when the cookie is gone', async () => {
    const pending = store.hydrate();
    http.expectOne(AUTH_API.refresh).flush({ code: 'AUTH-012', title: 'rejected', status: 401 }, { status: 401, statusText: 'Unauthorized' });
    await pending;

    expect(store.isHydrated()).toBe(true);
    expect(store.isAuthenticated()).toBe(false);
    expect(store.token()).toBeNull();
    expect(navigation.loads).toEqual([]); // no session to end: no reload loop at start-up
  });

  it('posts admin: true from the Super Admin address and stays signed out on a step that is not signed-in (LIS-CR-005)', async () => {
    const pending = store.signIn('root', 'secret', { admin: true });
    const request = http.expectOne(AUTH_API.signIn);
    expect(request.request.body).toEqual({ username: 'root', password: 'secret', admin: true });
    request.flush({ step: 'mfa', accessToken: null, expiresAtUtc: null, refreshToken: null, profile: null } satisfies AuthResponse);
    const response = await pending;

    expect(response.step).toBe('mfa');
    expect(store.isAuthenticated()).toBe(false);
  });

  it('ends a laboratory session on its own address, and an idle end there too (LIS-CR-005)', async () => {
    const pending = store.signIn('mona', 'secret', { laboratory: 'nile-lab' });
    const response = signedIn('token-1');
    http.expectOne(AUTH_API.signIn).flush({ ...response, profile: response.profile === null ? null : { ...response.profile, portalSlug: 'nile-lab' } });
    await pending;

    const signOut = store.signOut('idle');
    http.expectOne(AUTH_API.signOut).flush(null);
    await signOut;
    expect(navigation.loads).toEqual(['/lab/nile-lab?reason=idle']);
  });

  it('ends a platform session on the Super Admin sign-in, keeping a Super Admin return URL (LIS-CR-005)', async () => {
    const pending = store.signIn('root', 'secret', { admin: true });
    http.expectOne(AUTH_API.signIn).flush(signedIn('token-1', null, 'a1', PLATFORM_TENANT_ID));
    await pending;
    navigation.url = '/admin/clients';

    const refresh = store.refresh();
    http.expectOne(`${AUTH_API.refresh}?portal=admin`).flush({ code: 'AUTH-012', title: 'rejected', status: 401 }, { status: 401, statusText: 'Unauthorized' });
    await refresh;
    expect(navigation.loads).toEqual(['/admin/login?returnUrl=%2Fadmin%2Fclients']);
  });

  describe('one browser, two portals (LIS-CR-014 G1)', () => {
    it('starts a Super Admin page from the Super Admin session (?portal=admin)', async () => {
      navigation.url = '/admin/clients';
      const pending = store.hydrate();
      http.expectNone(AUTH_API.refresh);
      http.expectOne(`${AUTH_API.refresh}?portal=admin`).flush(signedIn('token-admin', null, 'a1', PLATFORM_TENANT_ID));
      await pending;

      expect(store.isAuthenticated()).toBe(true);
      expect(store.currentTenant()).toBe(PLATFORM_TENANT_ID);
    });

    it('starts a laboratory page from the laboratory session (no portal named)', async () => {
      navigation.url = '/lab/nile-lab';
      const pending = store.hydrate();
      http.expectOne(AUTH_API.refresh).flush(signedIn('token-lab'));
      await pending;

      expect(store.currentTenant()).toBe('t1');
    });

    it('never starts a tab in the other portal\'s session (a cookie set before the split)', async () => {
      navigation.url = '/registration/visits';
      const pending = store.hydrate();
      http.expectOne(AUTH_API.refresh).flush(signedIn('token-admin', null, 'a1', PLATFORM_TENANT_ID));
      await pending;

      expect(store.isAuthenticated()).toBe(false);
      expect(store.token()).toBeNull();
      expect(navigation.loads).toEqual([]);   // no session of this portal: the page's own guards decide
    });

    it('signs out of the Super Admin portal only, and lands on its sign-in page', async () => {
      const pending = store.signIn('root', 'secret', { admin: true });
      http.expectOne(AUTH_API.signIn).flush(signedIn('token-1', null, 'a1', PLATFORM_TENANT_ID));
      await pending;
      navigation.url = '/admin/clients';

      const signOut = store.signOut();
      http.expectNone(AUTH_API.signOut);
      http.expectOne(`${AUTH_API.signOut}?portal=admin`).flush(null, { status: 204, statusText: 'No Content' });
      await signOut;
      expect(navigation.loads).toEqual(['/admin/login']);
      expect(channel.posted.at(-1)).toEqual({ kind: 'signed-out' });
    });

    it('refreshes under the lock of the tab\'s portal', async () => {
      const portals: (Portal | undefined)[] = [];
      const recordingLock: RefreshLock = (task, portal) => {
        portals.push(portal);
        return task();
      };
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        providers: [
          provideZonelessChangeDetection(),
          provideHttpClient(),
          provideHttpClientTesting(),
          ...provideSessionTesting(navigation, channel),
          { provide: REFRESH_LOCK, useValue: recordingLock },
        ],
      });
      const portalStore = TestBed.inject(AuthStore);
      const backend = TestBed.inject(HttpTestingController);
      navigation.url = '/admin';
      const admin = portalStore.refresh();
      backend.expectOne(`${AUTH_API.refresh}?portal=admin`).flush(signedIn('token-a', null, 'a1', PLATFORM_TENANT_ID));
      await admin;
      navigation.url = '/results/worklist';
      const client = portalStore.refresh();
      backend.expectOne(AUTH_API.refresh).flush(signedIn('token-c', null, 'a1', PLATFORM_TENANT_ID));
      await client;

      expect(portals).toEqual(['admin', 'client']);
      backend.verify();
      http = backend;   // the outer afterEach verifies the module in use
    });

    it('reads the portal from the address: /admin and below is the Super Admin', () => {
      expect(portalOfUrl('/admin')).toBe('admin');
      expect(portalOfUrl('/admin/login/recover')).toBe('admin');
      expect(portalOfUrl('/admin?x=1')).toBe('admin');
      expect(portalOfUrl('/administration')).toBe('client');
      expect(portalOfUrl('/lab/nile-lab')).toBe('client');
      expect(portalOfUrl('/login')).toBe('client');
      expect(portalOfUrl('/')).toBe('client');
      expect(portalAuthUrl(AUTH_API.refresh, 'client')).toBe(AUTH_API.refresh);
      expect(portalAuthUrl(AUTH_API.signOut, 'admin')).toBe('/api/v1/auth/sign-out?portal=admin');
      expect(refreshLockName('admin')).toBe('platform-session-refresh:admin');
    });
  });

  it('exposes the branch of the session and replaces the profile after selecting one (CP-04)', async () => {
    const signIn = store.signIn('mona', 'secret', null);
    http.expectOne(AUTH_API.signIn).flush(signedIn('token-1'));
    await signIn;

    expect(store.currentBranch()).toBeNull();
    expect(store.branchIds()).toEqual(['b1', 'b2']);

    const pending = store.selectBranch('b2');
    const request = http.expectOne(AUTH_API.selectBranch);
    expect(request.request.body).toEqual({ branchId: 'b2' });
    request.flush(signedIn('token-1', 'b2').profile);
    await pending;

    expect(store.currentBranch()).toBe('b2');
    expect(store.token()).toBe('token-1');
    expect(store.isAuthenticated()).toBe(true);
  });

  it('a refused branch leaves the profile untouched', async () => {
    const signIn = store.signIn('mona', 'secret', null);
    http.expectOne(AUTH_API.signIn).flush(signedIn('token-1'));
    await signIn;

    const pending = store.selectBranch('b9');
    http.expectOne(AUTH_API.selectBranch).flush({ code: 'TENANT-005', title: 'not assigned', status: 403 }, { status: 403, statusText: 'Forbidden' });
    await expect(pending).rejects.toBeTruthy();

    expect(store.currentBranch()).toBeNull();
    expect(store.isAuthenticated()).toBe(true);
  });

  it('password recovery posts the identifier, then the code and the new password (CP-03)', async () => {
    const start = store.startPasswordRecovery('+201001234567', 'ar');
    const started = http.expectOne(AUTH_API.recoveryStart);
    expect(started.request.body).toEqual({ usernameOrMobile: '+201001234567', language: 'ar' });
    started.flush(null, { status: 202, statusText: 'Accepted' });
    await start;

    const complete = store.completePasswordRecovery('mona', '123456', 'New-Password-2026!');
    const completed = http.expectOne(AUTH_API.recoveryComplete);
    expect(completed.request.body).toEqual({ usernameOrMobile: 'mona', code: '123456', newPassword: 'New-Password-2026!' });
    completed.flush(null, { status: 204, statusText: 'No Content' });
    await complete;

    expect(store.isAuthenticated()).toBe(false);
  });

  it('sign-out re-runs no resource of the open page and asks the server nothing more before /login loads (round 2 R2-05)', async () => {
    const signIn = store.signIn('mona', 'secret', null);
    http.expectOne(AUTH_API.signIn).flush(signedIn('token-1', 'b1'));
    await signIn;

    // A page's resources keyed by the user and the branch, as the dashboard and the branch picker are.
    const loads: string[] = [];
    TestBed.runInInjectionContext(() =>
      resource({
        params: () => ({ user: store.currentUser()?.id ?? '', branch: store.currentBranch() }),
        loader: ({ params }) => {
          loads.push(`${params.user}/${params.branch ?? '-'}`);
          return Promise.resolve(null);
        },
      }),
    );
    TestBed.tick();
    await Promise.resolve();
    expect(loads).toEqual(['u1/b1']);

    const signOut = store.signOut();
    http.expectOne(AUTH_API.signOut).flush(null, { status: 204, statusText: 'No Content' });
    await signOut;
    TestBed.tick();
    await Promise.resolve();

    expect(loads).toEqual(['u1/b1']);
    expect(store.hasEnded()).toBe(true);
    expect(store.token()).toBeNull();
    expect(store.currentBranch()).toBe('b1');
    await expect(store.refresh()).resolves.toBeNull();
    http.expectNone(AUTH_API.refresh);
    channel.deliver({ kind: 'signed-out' });
    expect(navigation.loads).toEqual(['/login']);
  });

  it('sign-out clears local state even when the server call fails', async () => {
    const signIn = store.signIn('mona', 'secret', null);
    http.expectOne(AUTH_API.signIn).flush(signedIn('token-1'));
    await signIn;

    const signOut = store.signOut();
    http.expectOne(AUTH_API.signOut).flush(null, { status: 500, statusText: 'Server Error' });
    await signOut;

    expect(store.token()).toBeNull();
    expect(store.isAuthenticated()).toBe(false);
    // A-01 / B-11: a full page load of the sign-in page without a return URL, and the other tabs are told
    expect(navigation.loads).toEqual(['/login']);
    expect(channel.posted).toEqual([{ kind: 'signed-in', userId: 'u1', tenantId: 't1' }, { kind: 'signed-out' }]);
  });

  describe('identity of the tab (C-01)', () => {
    async function signInAsMona(): Promise<void> {
      const pending = store.signIn('mona', 'secret', null);
      http.expectOne(AUTH_API.signIn).flush(signedIn('token-1'));
      await pending;
    }

    it('refuses a refresh that returns another user: no token, state cleared, sign-in page reloaded', async () => {
      await signInAsMona();
      navigation.url = '/results/worklist';

      const pending = store.refresh();
      http.expectOne(AUTH_API.refresh).flush(signedIn('token-of-y', null, 'u2', 't1'));

      expect(await pending).toBeNull();
      expect(store.token()).toBeNull();
      expect(store.profile()).toBeNull();
      expect(navigation.loads).toEqual(['/login']); // the page belonged to the previous user: no return URL
    });

    it('refuses a refresh that returns the same user in another tenant', async () => {
      await signInAsMona();

      const pending = store.refresh();
      http.expectOne(AUTH_API.refresh).flush(signedIn('token-lab-c', null, 'u1', 't2'));

      expect(await pending).toBeNull();
      expect(store.isAuthenticated()).toBe(false);
      expect(navigation.loads).toEqual(['/login']);
    });

    it('accepts a refresh of the same user and tenant', async () => {
      await signInAsMona();

      const pending = store.refresh();
      http.expectOne(AUTH_API.refresh).flush(signedIn('token-2', 'b1'));

      expect(await pending).toBe('token-2');
      expect(store.currentBranch()).toBe('b1');
      expect(navigation.loads).toEqual([]);
    });

    it('saves the results grid density as a preference of the membership and the profile follows (results page, 2026-10-14)', async () => {
      await signInAsMona();
      expect(store.profile()?.resultsDensity).toBeUndefined();

      const pending = store.updateResultsDensity('compact');
      const put = http.expectOne(ACCOUNT_API.preferences);
      expect(put.request.method).toBe('PUT');
      expect(put.request.body).toEqual({ resultsDensity: 'compact' });
      put.flush(null, { status: 204, statusText: 'No Content' });
      await pending;

      expect(store.profile()?.resultsDensity).toBe('compact');
      expect(store.profile()?.language).toBe('ar');   // the language of the signed-in fixture is untouched
    });

    it('a lost session reloads the sign-in page with the page the user was on', async () => {
      await signInAsMona();
      navigation.url = '/results/worklist?unit=CHEM';

      const pending = store.refresh();
      http.expectOne(AUTH_API.refresh).flush({ code: 'AUTH-012', title: 'rejected', status: 401 }, { status: 401, statusText: 'Unauthorized' });

      expect(await pending).toBeNull();
      expect(store.isAuthenticated()).toBe(false);
      expect(navigation.loads).toEqual(['/login?returnUrl=%2Fresults%2Fworklist%3Funit%3DCHEM']);
    });

    it('another tab signing out reloads this one', async () => {
      await signInAsMona();

      channel.deliver({ kind: 'signed-out' });

      expect(store.isAuthenticated()).toBe(false);
      expect(navigation.loads).toEqual(['/login']);
    });

    it('another tab signing in as someone else reloads this one; the same identity does not', async () => {
      await signInAsMona();

      channel.deliver({ kind: 'signed-in', userId: 'u1', tenantId: 't1' });
      expect(store.isAuthenticated()).toBe(true);
      expect(navigation.loads).toEqual([]);

      channel.deliver({ kind: 'signed-in', userId: 'u2', tenantId: 't1' });
      expect(store.isAuthenticated()).toBe(false);
      expect(navigation.loads).toEqual(['/login']);
    });

    it('a tab without a session ignores the other tabs', () => {
      channel.deliver({ kind: 'signed-out' });
      channel.deliver({ kind: 'signed-in', userId: 'u2', tenantId: 't1' });
      channel.deliver({ kind: 'profile-changed', userId: 'u1', tenantId: 't1' });
      expect(navigation.loads).toEqual([]);
      expect(store.profileRevision()).toBe(0);
    });

    it('a profile change in another tab of the same identity keeps the session and moves the profile revision (R2-01)', async () => {
      await signInAsMona();

      channel.deliver({ kind: 'profile-changed', userId: 'u1', tenantId: 't1' });
      expect(store.profileRevision()).toBe(1);
      expect(store.isAuthenticated()).toBe(true);
      expect(navigation.loads).toEqual([]);

      // another user's or tenant's profile is none of this tab's business (their sign-in already reloaded it)
      channel.deliver({ kind: 'profile-changed', userId: 'u2', tenantId: 't1' });
      channel.deliver({ kind: 'profile-changed', userId: 'u1', tenantId: 't2' });
      expect(store.profileRevision()).toBe(1);
      expect(store.isAuthenticated()).toBe(true);
    });

    it('a change of branch tells the other tabs, which show the new branch (validation F-05)', async () => {
      await signInAsMona();

      const pending = store.selectBranch('b2');
      http.expectOne(AUTH_API.selectBranch).flush(signedIn('ignored', 'b2').profile);
      await pending;
      expect(channel.posted.at(-1)).toEqual({ kind: 'branch-changed', userId: 'u1', tenantId: 't1', branchId: 'b2' });

      // the other side: another tab of the same identity moved the shared session to b1
      channel.deliver({ kind: 'branch-changed', userId: 'u1', tenantId: 't1', branchId: 'b1' });
      expect(store.currentBranch()).toBe('b1');
      expect(store.isAuthenticated()).toBe(true);
      expect(navigation.loads).toEqual([]);

      // another identity's branch is none of this tab's business
      channel.deliver({ kind: 'branch-changed', userId: 'u2', tenantId: 't1', branchId: 'b9' });
      channel.deliver({ kind: 'branch-changed', userId: 'u1', tenantId: 't2', branchId: 'b9' });
      expect(store.currentBranch()).toBe('b1');
    });

    it('announces its own profile change to the other tabs, and nothing when signed out (R2-01)', async () => {
      store.announceProfileChange();
      expect(channel.posted).toEqual([]);

      await signInAsMona();
      store.announceProfileChange();
      expect(channel.posted.at(-1)).toEqual({ kind: 'profile-changed', userId: 'u1', tenantId: 't1' });
      expect(store.profileRevision()).toBe(0); // this tab refreshes its own caches
    });
  });
});

class FakeBroadcastChannel {
  static readonly instances: FakeBroadcastChannel[] = [];
  readonly sent: unknown[] = [];
  private listener: ((event: MessageEvent<unknown>) => void) | null = null;

  constructor(readonly name: string) {
    FakeBroadcastChannel.instances.push(this);
  }

  postMessage(message: unknown): void {
    this.sent.push(message);
  }

  addEventListener(_type: string, listener: (event: MessageEvent<unknown>) => void): void {
    this.listener = listener;
  }

  removeEventListener(): void {
    this.listener = null;
  }

  emit(data: unknown): void {
    this.listener?.({ data } as MessageEvent<unknown>);
  }
}

describe('session channel and sign-in URL helpers', () => {
  it('accepts only the known message shapes from another tab', () => {
    expect(readSessionMessage({ kind: 'profile-changed', userId: 'u', tenantId: null })).toEqual({ kind: 'profile-changed', userId: 'u', tenantId: null });
    expect(readSessionMessage({ kind: 'profile-changed', userId: 'u' })).toBeNull();
    expect(readSessionMessage({ kind: 'signed-out' })).toEqual({ kind: 'signed-out' });
    expect(readSessionMessage({ kind: 'branch-changed', userId: 'u', tenantId: 't', branchId: 'b' })).toEqual({ kind: 'branch-changed', userId: 'u', tenantId: 't', branchId: 'b' });
    expect(readSessionMessage({ kind: 'branch-changed', userId: 'u', tenantId: 't' })).toBeNull();
    expect(readSessionMessage({ kind: 'signed-in', userId: 'u', tenantId: 't', extra: 1 })).toEqual({ kind: 'signed-in', userId: 'u', tenantId: 't' });
    expect(readSessionMessage({ kind: 'signed-in', userId: 1 })).toBeNull();
    expect(readSessionMessage('signed-out')).toBeNull();
    expect(readSessionMessage(null)).toBeNull();
  });

  it('posts and receives on the browser channel of the tab\'s portal only (LIS-CR-014 G1)', () => {
    let portal: Portal = 'client';
    const view = { BroadcastChannel: FakeBroadcastChannel } as unknown as Window & typeof globalThis;
    const channel = broadcastSessionChannel(view, () => portal);
    const received: unknown[] = [];
    const stop = channel.listen((message) => received.push(message));
    const named = (name: string): FakeBroadcastChannel | undefined => FakeBroadcastChannel.instances.filter((instance) => instance.name === name).at(-1);
    const client = named('platform-session:client');
    const admin = named('platform-session:admin');

    channel.post({ kind: 'signed-out' });
    admin?.emit({ kind: 'signed-in', userId: 'a1', tenantId: 'p' });   // the Super Admin tabs' business
    client?.emit({ kind: 'signed-in', userId: 'u2', tenantId: 't' });
    client?.emit({ kind: 'junk' });
    portal = 'admin';   // the tab moved to a Super Admin page
    channel.post({ kind: 'signed-out', reason: 'idle' });
    client?.emit({ kind: 'signed-out' });
    admin?.emit({ kind: 'renewed', userId: 'a1', tenantId: 'p' });
    stop();
    admin?.emit({ kind: 'signed-out' });

    expect(sessionChannelName('client')).toBe('platform-session:client');
    expect(sessionChannelName('admin')).toBe('platform-session:admin');
    expect(client?.sent).toEqual([{ kind: 'signed-out' }]);
    expect(admin?.sent).toEqual([{ kind: 'signed-out', reason: 'idle' }]);
    expect(received).toEqual([
      { kind: 'signed-in', userId: 'u2', tenantId: 't' },
      { kind: 'renewed', userId: 'a1', tenantId: 'p' },
    ]);
    expect(() => broadcastSessionChannel(null).post({ kind: 'signed-out' })).not.toThrow();
  });

  it('chooses the sign-in address of the session: laboratory, Super Admin, or the find page (LIS-CR-005)', () => {
    const lab = { tenantId: 't1', portalSlug: 'nile-lab' };
    const platform = { tenantId: PLATFORM_TENANT_ID, portalSlug: null };
    expect(signInUrl('/results/worklist', lab)).toBe('/lab/nile-lab?returnUrl=%2Fresults%2Fworklist');
    expect(signInUrl('/admin/clients', lab)).toBe('/lab/nile-lab');   // a laboratory address never leads to a Super Admin page
    expect(signInUrl('/admin/clients', platform)).toBe('/admin/login?returnUrl=%2Fadmin%2Fclients');
    expect(signInUrl('/billing/payments', platform)).toBe('/admin/login');
    expect(signInUrl('/billing/payments', null)).toBe('/login?returnUrl=%2Fbilling%2Fpayments');
    expect(signInUrl('/admin/clients', null)).toBe('/admin/login?returnUrl=%2Fadmin%2Fclients');
    expect(signInUrl(null, { tenantId: 't1', portalSlug: null })).toBe('/login');   // a profile without an address
    expect(idleSignInUrl(lab)).toBe('/lab/nile-lab?reason=idle');
    expect(idleSignInUrl(platform)).toBe('/admin/login?reason=idle');
    expect(idleSignInUrl()).toBe('/login?reason=idle');
    for (const url of ['/login', '/login/recover', '/lab/nile-lab', '/lab/nile-lab/recover?x=1', '/admin/login', '/admin/login/recover']) {
      expect(isLoginRoute(url)).toBe(true);
    }

    expect(isLoginRoute('/admin/clients')).toBe(false);
    expect(isLoginRoute('/laboratory')).toBe(false);
    expect(normaliseLaboratorySlug('  https://sky.example.com/lab/Nile-Lab/recover ')).toBe('nile-lab');
    expect(laboratorySlugOf('https://sky.example.com/lab/nile-lab')).toBe('nile-lab');
    expect(laboratorySlugOf('https://nile-lab.sky.example.com')).toBeNull();
  });

  it('carries a return URL only when it is meaningful (B-11)', () => {
    expect(signInUrl('/login')).toBe('/login');
    expect(signInUrl('/login?returnUrl=%2Fx')).toBe('/login');
    expect(signInUrl('/')).toBe('/login');
    expect(signInUrl('//evil.example')).toBe('/login');
    expect(signInUrl(null)).toBe('/login');
    expect(signInUrl('/billing/payments')).toBe('/login?returnUrl=%2Fbilling%2Fpayments');
    expect(meaningfulReturnUrl('/login/recover')).toBeNull();
    expect(meaningfulReturnUrl('/lab/nile-lab?returnUrl=%2Fx')).toBeNull();
  });
});
