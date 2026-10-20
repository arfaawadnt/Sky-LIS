import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { AUTH_API, type AuthResponse } from '../api/auth.contracts';
import { AuthStore } from '../auth/auth.store';
import { FakeBrowserNavigation, provideSessionTesting } from '../auth/session-testing';
import { authInterceptor } from './auth.interceptor';
import { csrfInterceptor } from './csrf.interceptor';
import { problemDetailsInterceptor } from './problem-details.interceptor';

/** Promise-based steps (refresh, sealing) issue their HTTP request a tick later; let the event loop turn before asserting. */
const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const signedIn = (token: string, userId = 'u1', tenantId = 't1'): AuthResponse => ({
  step: 'signed-in',
  accessToken: token,
  expiresAtUtc: null,
  refreshToken: null,
  profile: { userId, displayName: 'Mona', tenantId, roleName: null, permissions: [], language: 'en', scope: 'full' },
  tenants: null,
});

describe('authInterceptor + csrfInterceptor (01 §6.4)', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let store: AuthStore;
  let navigation: FakeBrowserNavigation;

  beforeEach(() => {
    navigation = new FakeBrowserNavigation();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([{ path: 'login', children: [] }]),
        provideHttpClient(withInterceptors([csrfInterceptor, authInterceptor, problemDetailsInterceptor])),
        provideHttpClientTesting(),
        ...provideSessionTesting(navigation),
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    store = TestBed.inject(AuthStore);
  });

  afterEach(() => backend.verify());

  async function signInWith(token: string): Promise<void> {
    const pending = store.signIn('mona', 'pw', null);
    backend.expectOne(AUTH_API.signIn).flush(signedIn(token));
    await pending;
  }

  it('adds the bearer from memory and the CSRF header to API requests only', async () => {
    await signInWith('token-1');

    const call = firstValueFrom(http.get('/api/v1/things'));
    const request = backend.expectOne('/api/v1/things');
    expect(request.request.headers.get('Authorization')).toBe('Bearer token-1');
    expect(request.request.headers.get('X-Requested-With')).toBe('Platform');
    request.flush([]);
    await call;

    const external = firstValueFrom(http.get('/i18n/shell.en.json'));
    const other = backend.expectOne('/i18n/shell.en.json');
    expect(other.request.headers.has('Authorization')).toBe(false);
    expect(other.request.headers.has('X-Requested-With')).toBe(false);
    other.flush({});
    await external;
  });

  it('on 401 refreshes once and replays the request with the new token', async () => {
    await signInWith('stale');

    const call = firstValueFrom(http.get<{ ok: boolean }>('/api/v1/things'));
    backend.expectOne('/api/v1/things').flush({ code: 'AUTH-006', title: 'expired', status: 401 }, { status: 401, statusText: 'Unauthorized' });

    const refresh = backend.expectOne(AUTH_API.refresh);
    expect(refresh.request.headers.get('X-Requested-With')).toBe('Platform');
    refresh.flush(signedIn('fresh'));
    await settle();

    const replay = backend.expectOne('/api/v1/things');
    expect(replay.request.headers.get('Authorization')).toBe('Bearer fresh');
    replay.flush({ ok: true });

    expect(await call).toEqual({ ok: true });
    expect(store.token()).toBe('fresh');
  });

  it('two concurrent 401s share one refresh', async () => {
    await signInWith('stale');

    const a = firstValueFrom(http.get('/api/v1/a'));
    const b = firstValueFrom(http.get('/api/v1/b'));
    backend.expectOne('/api/v1/a').flush(null, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/v1/b').flush(null, { status: 401, statusText: 'Unauthorized' });

    const refreshes = backend.match(AUTH_API.refresh);
    expect(refreshes.length).toBe(1);
    refreshes[0]?.flush(signedIn('fresh'));
    await settle();

    backend.expectOne('/api/v1/a').flush('A');
    backend.expectOne('/api/v1/b').flush('B');
    expect(await a).toBe('A');
    expect(await b).toBe('B');
  });

  it('a failed refresh clears the session and surfaces the original 401 as a typed problem', async () => {
    await signInWith('stale');

    const call = firstValueFrom(http.get('/api/v1/things'));
    backend.expectOne('/api/v1/things').flush({ code: 'AUTH-006', title: 'expired', status: 401 }, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne(AUTH_API.refresh).flush({ code: 'AUTH-012', title: 'rejected', status: 401 }, { status: 401, statusText: 'Unauthorized' });

    await expect(call).rejects.toMatchObject({ name: 'ProblemError', code: 'AUTH-006', status: 401 });
    expect(store.isAuthenticated()).toBe(false);
    expect(navigation.loads).toEqual(['/login']); // the store reloads the sign-in page (A-01); the root carries no return URL
  });

  it('never replays a request when the refresh comes back as another user (C-01)', async () => {
    await signInWith('token-of-x');
    navigation.url = '/admin/profile';

    const call = firstValueFrom(http.put('/api/v1/account/password', { current: 'a', next: 'b' })).catch((error: unknown) => error);
    backend.expectOne('/api/v1/account/password').flush({ code: 'AUTH-006', title: 'expired', status: 401 }, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne(AUTH_API.refresh).flush(signedIn('token-of-y', 'u2', 't1'));
    await settle();

    backend.expectNone('/api/v1/account/password');
    expect(await call).toMatchObject({ code: 'AUTH-006', status: 401 });
    expect(store.isAuthenticated()).toBe(false);
    expect(store.token()).toBeNull();
    expect(navigation.loads).toEqual(['/login']);
  });

  it('never replays a request when the refresh comes back in another tenant (C-01)', async () => {
    await signInWith('token-lab-b');

    const call = firstValueFrom(http.post('/api/v1/things', { a: 1 })).catch((error: unknown) => error);
    backend.expectOne('/api/v1/things').flush(null, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne(AUTH_API.refresh).flush(signedIn('token-lab-c', 'u1', 't2'));
    await settle();

    backend.expectNone('/api/v1/things');
    expect(await call).toMatchObject({ status: 401 });
    expect(navigation.loads).toEqual(['/login']);
  });

  it('a 401 without a session routes to sign-in without a pointless return URL', async () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl');

    const call = firstValueFrom(http.get('/api/v1/things'));
    backend.expectOne('/api/v1/things').flush(null, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne(AUTH_API.refresh).flush(null, { status: 401, statusText: 'Unauthorized' });

    await expect(call).rejects.toMatchObject({ status: 401 });
    expect(navigate).toHaveBeenCalledWith('/login');   // LIS-CR-005: the find page
    expect(navigation.loads).toEqual([]);
  });

  it('never retries the auth endpoints themselves', async () => {
    const call = firstValueFrom(http.post(AUTH_API.signIn, { username: 'x', password: 'y', laboratory: 'nile-lab' }));
    backend.expectOne(AUTH_API.signIn).flush({ code: 'AUTH-002', title: 'bad', status: 401 }, { status: 401, statusText: 'Unauthorized' });

    await expect(call).rejects.toMatchObject({ code: 'AUTH-002' });
    backend.expectNone(AUTH_API.refresh);
  });

  it('sends no API request once the session was signed out and /login is loading (round 2 R2-05)', async () => {
    await signInWith('token-1');
    const signOut = store.signOut();
    backend.expectOne(AUTH_API.signOut).flush(null, { status: 204, statusText: 'No Content' });
    await signOut;

    const call = firstValueFrom(http.get('/api/v1/dashboard/branch'));
    await expect(call).rejects.toBeDefined();
    await settle();
    backend.expectNone('/api/v1/dashboard/branch');
    backend.expectNone(AUTH_API.refresh);
    expect(navigation.loads).toEqual(['/login']);
  });
});
