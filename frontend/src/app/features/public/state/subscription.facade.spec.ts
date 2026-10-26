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

  it('checks the username of the verified request after a pause and reports taken, invalid and available (LIS-CR-014 S2)', async () => {
    facade.started.set(STARTED);
    vi.useFakeTimers();
    facade.checkUsername('ab');
    expect(facade.usernameState()).toBe('invalid');

    facade.checkUsername('Lab.Admin');
    expect(facade.usernameState()).toBe('checking');
    vi.advanceTimersByTime(450);
    const check = backend.expectOne((request) => request.url === PUBLIC_SUBSCRIPTION_API.usernameAvailable('sub-1'));
    expect(check.request.params.get('username')).toBe('lab.admin');
    check.flush({ issues: [{ field: 'username', code: 'SUB-013' }] });
    await vi.waitFor(() => expect(facade.usernameState()).toBe('taken'));

    facade.checkUsername('lab.owner');
    vi.advanceTimersByTime(450);
    backend.expectOne((request) => request.url === PUBLIC_SUBSCRIPTION_API.usernameAvailable('sub-1')).flush({ issues: [] });
    await vi.waitFor(() => expect(facade.usernameState()).toBe('available'));
  });

  describe('back to the details (LIS-CR-014 S3)', () => {
    const LIVE: SubscriptionStarted = { ...STARTED, expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString() };

    async function startAndVerify(): Promise<void> {
      await runEffects();
      backend.expectOne(PUBLIC_SUBSCRIPTION_API.countries).flush([EGYPT]);
      const moved = facade.continueFromDetails(START_REQUEST);
      await settle();
      backend.expectOne(PUBLIC_SUBSCRIPTION_API.start).flush(LIVE);
      expect(await moved).toBe(true);
      const verified = facade.verify.execute({ subscriptionId: 'sub-1', code: '123456' });
      await settle();
      backend.expectOne(PUBLIC_SUBSCRIPTION_API.verify).flush(null, { status: 204, statusText: 'No Content' });
      await verified;
      expect(facade.step()).toBe(3);
    }

    it('offers the done steps of the rail from steps 2 and 3, never from the last step', async () => {
      await startAndVerify();
      expect([1, 2, 3, 4].filter((step) => facade.canOpenStep(step as 1 | 2 | 3 | 4))).toEqual([1, 2]);
      facade.openStep(2);
      expect(facade.reviewingVerified()).toBe(true);
      facade.continueVerified();
      expect(facade.step()).toBe(3);

      facade.step.set(4);
      expect(facade.canOpenStep(1)).toBe(false);
      facade.backToDetails();
      expect(facade.step()).toBe(4);
    });

    it('resumes the verified request without a new code when no detail changed', async () => {
      await startAndVerify();
      facade.backToDetails();
      expect(facade.step()).toBe(1);

      expect(await facade.continueFromDetails({ ...START_REQUEST, language: 'ar' })).toBe(true);   // the page language is not a detail
      backend.expectNone(PUBLIC_SUBSCRIPTION_API.start);
      expect(facade.step()).toBe(3);
      backend.verify();
    });

    it('starts a new request, and a new code, when a detail changed', async () => {
      await startAndVerify();
      facade.backToDetails();

      const moved = facade.continueFromDetails({ ...START_REQUEST, managerMobile: '01099999999' });
      await settle();
      backend.expectOne(PUBLIC_SUBSCRIPTION_API.start).flush({ ...LIVE, subscriptionId: 'sub-2' });
      expect(await moved).toBe(true);
      expect(facade.step()).toBe(2);
      expect(facade.verified()).toBe(false);
      expect(facade.started()?.subscriptionId).toBe('sub-2');
    });

    it('forces a new request after a step-1 field was refused at step 3', async () => {
      await startAndVerify();
      facade.returnForDetails();
      expect(facade.step()).toBe(1);
      expect(facade.returnedForDetails()).toBe(true);

      const moved = facade.continueFromDetails(START_REQUEST);
      await settle();
      backend
        .expectOne(PUBLIC_SUBSCRIPTION_API.start)
        .flush({ type: 'about:blank', title: 'in use', status: 409, code: 'SUB-011', errors: { managerMobile: ['in use'] } }, { status: 409, statusText: 'Conflict' });
      expect(await moved).toBe(false);
      expect(facade.start.problem()?.errors?.['managerMobile']).toEqual(['in use']);
      expect(facade.step()).toBe(1);
    });

    it('checks the step-1 mobiles and answers no issue when the check fails', async () => {
      const answer = facade.checkDetails({ countryId: 'eg', mobile: null, managerMobile: '+201012345679' });
      await settle();
      const check = backend.expectOne(PUBLIC_SUBSCRIPTION_API.check);
      expect(check.request.body).toEqual({ countryId: 'eg', mobile: null, managerMobile: '+201012345679' });
      check.flush({ issues: [{ field: 'managerMobile', code: 'SUB-011' }] });
      expect((await answer).issues).toEqual([{ field: 'managerMobile', code: 'SUB-011' }]);

      const failed = facade.checkDetails({ countryId: null, mobile: '+201012345678', managerMobile: null });
      await settle();
      backend.expectOne(PUBLIC_SUBSCRIPTION_API.check).flush({ code: 'RATE-429' }, { status: 429, statusText: 'Too Many' });
      expect((await failed).issues).toEqual([]);
    });
  });
});
