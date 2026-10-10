import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { AUTH_API } from '../api/auth.contracts';
import { CredentialEnvelopeClient } from '../crypto/credential-envelope.client';
import { credentialEnvelopeInterceptor } from './credential-envelope.interceptor';
import { problemDetailsInterceptor } from './problem-details.interceptor';

/** Promise-based steps (refresh, sealing) issue their HTTP request a tick later; let the event loop turn before asserting. */
const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

describe('credentialEnvelopeInterceptor (01 §6.4, D-03)', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let sealCalls: { fields: readonly string[]; audience: string }[];

  beforeEach(() => {
    sealCalls = [];
    const client: Pick<CredentialEnvelopeClient, 'sealFields'> = {
      sealFields: async (body, fields, audience) => {
        sealCalls.push({ fields, audience });
        const sealed: Record<string, unknown> = { ...body };
        for (const field of fields) {
          sealed[field] = `sealed(${String(body[field])})#${sealCalls.length}`;
        }

        return sealed;
      },
    };

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: CredentialEnvelopeClient, useValue: client },
        provideHttpClient(withInterceptors([credentialEnvelopeInterceptor, problemDetailsInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('seals the password of a sign-in before it leaves the browser, bound to the method and path', async () => {
    const call = firstValueFrom(http.post(AUTH_API.signIn, { username: 'mona', password: 'pw', tenant: null }));
    await settle();

    const request = backend.expectOne(AUTH_API.signIn);
    expect(request.request.body).toEqual({ username: 'mona', password: 'sealed(pw)#1', tenant: null });
    expect(sealCalls).toEqual([{ fields: ['password'], audience: 'POST /api/v1/auth/sign-in' }]);
    request.flush({ step: 'signed-in' });
    await call;
  });

  it('leaves requests without credential fields untouched', async () => {
    const call = firstValueFrom(http.get('/api/v1/auth/me'));
    await settle();
    backend.expectOne('/api/v1/auth/me').flush({});
    await call;
    expect(sealCalls.length).toBe(0);
  });

  it('retries once with a fresh challenge when the server reports an expired or replayed challenge', async () => {
    const call = firstValueFrom(http.post(AUTH_API.signIn, { username: 'mona', password: 'pw', tenant: null }));
    await settle();

    backend.expectOne(AUTH_API.signIn).flush({ code: 'CRED-003', title: 'replayed', status: 400 }, { status: 400, statusText: 'Bad Request' });
    await settle();
    const retry = backend.expectOne(AUTH_API.signIn);
    expect(retry.request.body).toMatchObject({ password: 'sealed(pw)#2' });
    retry.flush({ step: 'signed-in' });
    await call;

    expect(sealCalls.length).toBe(2);
  });

  it('does not retry other refusals', async () => {
    const call = firstValueFrom(http.post(AUTH_API.signIn, { username: 'mona', password: 'pw', tenant: null }));
    await settle();
    backend.expectOne(AUTH_API.signIn).flush({ code: 'AUTH-002', title: 'bad', status: 401 }, { status: 401, statusText: 'Unauthorized' });

    await expect(call).rejects.toMatchObject({ code: 'AUTH-002' });
    expect(sealCalls.length).toBe(1);
  });
});
