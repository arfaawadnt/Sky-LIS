import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { ACCOUNT_API } from '../api/account.contracts';
import { AUTH_API, type AuthResponse } from '../api/auth.contracts';
import { AuthStore } from '../auth/auth.store';
import { FakeBrowserNavigation, FakeSessionChannel, provideSessionTesting } from '../auth/session-testing';
import { isProfileChange, profileChangeInterceptor } from './profile-change.interceptor';

describe('profileChangeInterceptor (R2-01)', () => {
  let channel: FakeSessionChannel;
  let http: HttpClient;
  let backend: HttpTestingController;

  beforeEach(async () => {
    channel = new FakeSessionChannel();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([profileChangeInterceptor])),
        provideHttpClientTesting(),
        ...provideSessionTesting(new FakeBrowserNavigation(), channel),
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    const pending = TestBed.inject(AuthStore).signIn('mona', 'pw', null);
    backend.expectOne(AUTH_API.signIn).flush({
      step: 'signed-in',
      accessToken: 't',
      expiresAtUtc: null,
      refreshToken: null,
      profile: { userId: 'u1', displayName: 'Mona', tenantId: 't1', roleName: null, permissions: [], language: 'en', scope: 'full' },
      tenants: null,
    } satisfies AuthResponse);
    await pending;
    channel.posted.length = 0;
  });

  it('tells the other tabs after a successful password change or language save', async () => {
    const changed = firstValueFrom(http.put<void>(ACCOUNT_API.password, { currentPassword: 'a', newPassword: 'b' }));
    backend.expectOne(ACCOUNT_API.password).flush(null, { status: 204, statusText: 'No Content' });
    await changed;
    expect(channel.posted).toEqual([{ kind: 'profile-changed', userId: 'u1', tenantId: 't1' }]);

    const saved = firstValueFrom(http.put<void>(ACCOUNT_API.preferences, { language: 'ar' }));
    backend.expectOne(ACCOUNT_API.preferences).flush(null, { status: 204, statusText: 'No Content' });
    await saved;
    expect(channel.posted.length).toBe(2);
  });

  it('announces nothing for a refused change or a read', async () => {
    const refused = firstValueFrom(http.put<void>(ACCOUNT_API.password, {})).catch(() => null);
    backend.expectOne(ACCOUNT_API.password).flush({ code: 'ACC-001' }, { status: 400, statusText: 'Bad Request' });
    await refused;

    const read = firstValueFrom(http.get(ACCOUNT_API.profile));
    backend.expectOne(ACCOUNT_API.profile).flush({});
    await read;

    expect(channel.posted).toEqual([]);
  });

  it('recognises the own-profile writes only', () => {
    expect(isProfileChange('PUT', ACCOUNT_API.password)).toBe(true);
    expect(isProfileChange('POST', `https://lab.example${ACCOUNT_API.password}?x=1`)).toBe(true);
    expect(isProfileChange('GET', ACCOUNT_API.preferences)).toBe(false);
    expect(isProfileChange('PUT', '/api/v1/organization/users/u1/password')).toBe(false);
  });
});
