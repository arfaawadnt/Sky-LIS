import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { PRICING_API } from '../../../core/api/pricing.contracts';
import { REGISTRATION_API, type PriceQuoteDocument } from '../../../core/api/registration.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { cashTier, insurerTier, settle } from '../testing/registration.fixtures';
import { PriceQueryFacade } from './price-query.facade';

const quote: PriceQuoteDocument = {
  letterhead: { labNameEn: 'Sky Lab', labNameAr: 'سكاي لاب', branchNameEn: null, branchNameAr: null },
  contract: null,
  currency: 'EGP',
  lines: [{ serviceId: 's-cbc', code: 'CBC', nameEn: 'Complete blood count', nameAr: '', type: 'Test', priced: true, listPrice: 100, patientShare: 100, payerShare: 0, expectedResultAtUtc: null, precaution: null, alertCondition: null }],
  totalListPrice: 100,
  totalPatientShare: 100,
  totalPayerShare: 0,
  preparations: [],
  issues: [],
  utcOffsetMinutes: 180,
  printedBy: null,
  printedAtUtc: '2026-10-10T08:00:00Z',
};

describe('PriceQueryFacade (CP-12)', () => {
  let facade: PriceQueryFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(withInterceptors([problemDetailsInterceptor])), provideHttpClientTesting(), PriceQueryFacade],
    });
    facade = TestBed.inject(PriceQueryFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  it('quotes nothing until a service is typed, then posts the contract, services and gender', async () => {
    await runEffects();
    backend.expectOne((request) => request.url === PRICING_API.tierOptions).flush([cashTier, insurerTier]);
    await runEffects();
    backend.expectNone(REGISTRATION_API.quote);
    facade.defaultTier();
    expect(facade.tierId()).toBe('tier-cash');

    facade.tierId.set('tier-ins');
    facade.gender.set('Female');
    expect(facade.addServices([{ serviceId: 's-cbc', code: 'CBC', nameEn: '', nameAr: '' }])).toEqual([]);
    expect(facade.addServices([{ serviceId: 's-cbc', code: 'CBC', nameEn: '', nameAr: '' }])).toEqual(['CBC']);
    await runEffects();

    const post = backend.expectOne(REGISTRATION_API.quote);
    expect(post.request.body).toEqual({ payerId: 'payer-ins', contractTierId: 'tier-ins', serviceIds: ['s-cbc'], gender: 'Female', utcOffsetMinutes: -new Date().getTimezoneOffset() });
    post.flush(quote);
    await runEffects();
    expect(facade.document()?.totalPatientShare).toBe(100);

    facade.removeLine('s-cbc');
    await runEffects();
    expect(facade.document()).toBeNull();
    backend.verify();
  });

  it('sends a null contract for the cash default and clears the services', async () => {
    await runEffects();
    backend.expectOne((request) => request.url === PRICING_API.tierOptions).flush([]);
    facade.addServices([{ serviceId: 's-glu', code: 'GLU', nameEn: '', nameAr: '' }]);
    await runEffects();
    const post = backend.expectOne(REGISTRATION_API.quote);
    expect(post.request.body).toEqual(expect.objectContaining({ payerId: null, contractTierId: null, serviceIds: ['s-glu'], gender: null }));
    post.flush(quote);

    facade.clear();
    expect(facade.lines()).toEqual([]);
    expect(facade.request()).toBeUndefined();
  });
});
