import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { PUBLIC_SUBSCRIPTION_API, type PublicCountry, type SubscriptionStarted } from '../../../core/api/public-subscription.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { SubscriptionFacade } from './subscription.facade';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const EGYPT: PublicCountry = { id: 'eg', nameEn: 'Egypt', nameAr: 'مصر', isoCode: 'EG', dialCode: '+20', mobileLength: 10, flag: null, currencyCode: 'EGP', language: 'ar' };
const STARTED: SubscriptionStarted = { subscriptionId: 'sub-1', expiresAtUtc: '2026-10-09T13:00:00Z', maskedMobile: '+201******678', codeLifetimeSeconds: 300 };

const START_REQUEST = {
  labNameEn: 'Al Noor',
  labNameAr: 'النور',
  countryId: 'eg',
  governorateId: 'cairo',
  address: 'Nasr City',
  mobile: '01012345678',
  licenceNo: null,
  managerName: 'Ahmed',
  managerMobile: '01012345679',
  email: null,
  language: 'en',
};

describe('SubscriptionFacade (CP-02)', () => {
  let facade: SubscriptionFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideRouter([]), provideHttpClient(withInterceptors([problemDetailsInterceptor])), provideHttpClientTesting(), SubscriptionFacade] });
    facade = TestBed.inject(SubscriptionFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => vi.useRealTimers());

  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  it('loads the countries, then the governorates of the chosen country', async () => {
    await runEffects();
    backend.expectOne(PUBLIC_SUBSCRIPTION_API.countries).flush([EGYPT]);
    await runEffects();
    expect(facade.countries.value()).toEqual([EGYPT]);

    facade.selectCountry('eg');
    await runEffects();
    const governorates = backend.expectOne((request) => request.url === PUBLIC_SUBSCRIPTION_API.governorates);
    expect(governorates.request.params.get('countryId')).toBe('eg');
    governorates.flush([{ id: 'cairo', countryId: 'eg', nameEn: 'Cairo', nameAr: 'القاهرة', district: null }]);
    await runEffects();
    expect(facade.country()).toEqual(EGYPT);
    expect(facade.governorates.value()?.length).toBe(1);
    backend.verify();
  });

  it('walks start → verify → complete and arms the 60 s resend countdown', async () => {
    await runEffects();
    backend.expectOne(PUBLIC_SUBSCRIPTION_API.countries).flush([EGYPT]);

    const started = facade.start.execute(START_REQUEST);
    await settle();
    const start = backend.expectOne(PUBLIC_SUBSCRIPTION_API.start);
    expect(start.request.body).toEqual(START_REQUEST);
    start.flush(STARTED);
    await started;
    expect(facade.step()).toBe(2);
    expect(facade.resendIn()).toBeGreaterThan(55);
    expect(facade.codeMinutes()).toBe(5);

    const verified = facade.verify.execute({ subscriptionId: 'sub-1', code: '123456' });
    await settle();
    const verify = backend.expectOne(PUBLIC_SUBSCRIPTION_API.verify);
    expect(verify.request.body).toEqual({ subscriptionId: 'sub-1', code: '123456' });
    verify.flush(null, { status: 204, statusText: 'No Content' });
    await verified;
    expect(facade.step()).toBe(3);

    const completed = facade.complete.execute({ subscriptionId: 'sub-1', username: 'lab.admin', password: 'a long passphrase' });
    await settle();
    const complete = backend.expectOne(PUBLIC_SUBSCRIPTION_API.complete);
    expect(complete.request.body).toEqual({ subscriptionId: 'sub-1', username: 'lab.admin', password: 'a long passphrase' });
    complete.flush({ tenantId: 't-1', portalUrl: 'https://alnoor.skylis.app', username: 'lab.admin' });
    await completed;
    expect(facade.step()).toBe(4);
    expect(facade.completed()?.portalUrl).toBe('https://alnoor.skylis.app');
    backend.verify();
  });

  it('keeps the step and exposes the problem when the code is refused', async () => {
    await runEffects();
    backend.expectOne(PUBLIC_SUBSCRIPTION_API.countries).flush([EGYPT]);
    facade.step.set(2);

    const verified = facade.verify.execute({ subscriptionId: 'sub-1', code: '000000' });
    await settle();
    backend.expectOne(PUBLIC_SUBSCRIPTION_API.verify).flush({ code: 'SUB-010', title: 'Wrong code', status: 422 }, { status: 422, statusText: 'Unprocessable' });
    expect(await verified).toBeNull();
    expect(facade.step()).toBe(2);
    expect(facade.verify.problem()?.code).toBe('SUB-010');
  });

  it('checks the username after a pause and reports taken, invalid and available', async () => {
    vi.useFakeTimers();
    facade.checkUsername('ab');
    expect(facade.usernameState()).toBe('invalid');

    facade.checkUsername('Lab.Admin');
    expect(facade.usernameState()).toBe('checking');
    vi.advanceTimersByTime(450);
    const check = backend.expectOne((request) => request.url === PUBLIC_SUBSCRIPTION_API.checkUsername);
    expect(check.request.params.get('username')).toBe('lab.admin');
    check.flush({ username: 'lab.admin', valid: true, available: false });
    await vi.waitFor(() => expect(facade.usernameState()).toBe('taken'));
  });
});
