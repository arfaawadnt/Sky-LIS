import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { type CountryRequest, PLATFORM_API } from '../../../core/api/platform.contracts';
import { ProblemError } from '../../../core/api/problem-details';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { isDialCodeValid } from '../ui/validators';
import { CountriesPage, DIAL_CODE_AS_TYPED } from './countries.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

interface CountryAdapterPage {
  readonly config: { readonly fields: readonly { readonly name: string; readonly pattern?: RegExp; readonly patternMessage?: string }[]; readonly adapter: { create(dto: CountryRequest): Promise<unknown>; update(id: string, dto: CountryRequest): Promise<unknown> } };
}

const uzbekistan: CountryRequest = {
  nameEn: 'Uzbekistan', nameAr: 'أوزبكستان', isoCode: 'UZ', dialCode: '998', currency: 'Som', currencyCode: 'UZS', language: 'en',
  timeZone: 'Asia/Tashkent', mobileLength: 9, flag: null, remarks: null,
};

/**
 * Validation 2026-10-11 C-09: a dial code without '+' used to pass the browser, and the server's GEO-002 refusal appeared at
 * the top of the dialog with the field unflagged. Now the page refuses it before sending and turns the server's refusal into
 * a field error of `dialCode` (the master-data page shows a VALIDATION-400 field error under its control).
 */
describe('Countries page dial code (C-09)', () => {
  let backend: HttpTestingController;
  let page: CountryAdapterPage;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(CountriesPage);
    TestBed.tick();
    await settle();
    backend.match(() => true).forEach((request) => request.flush({ items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 }));
    await settle();
    page = fixture.componentInstance as unknown as CountryAdapterPage;
  });

  async function refusal(action: Promise<unknown>): Promise<ProblemError> {
    try {
      await action;
    } catch (error: unknown) {
      if (error instanceof ProblemError) {
        return error;
      }

      throw error;
    }

    throw new Error('the save was not refused');
  }

  it('refuses a dial code without + under the field, before anything is sent', async () => {
    const error = await refusal(page.config.adapter.create(uzbekistan));
    expect(error.code).toBe('VALIDATION-400');
    expect(Object.keys(error.problem.errors ?? {})).toEqual(['dialCode']);
    expect(error.problem.errors?.['dialCode']).toEqual(['platform.countries.error.GEO-002']);
    backend.expectNone(PLATFORM_API.countries);

    await refusal(page.config.adapter.update('uz', { ...uzbekistan, dialCode: '+0998' }));
    backend.expectNone(`${PLATFORM_API.countries}/uz`);
  });

  it('maps the server refusal GEO-002 onto the dial code field and passes other refusals through', async () => {
    const saving = refusal(page.config.adapter.create({ ...uzbekistan, dialCode: '+998' }));
    await settle();
    backend.expectOne(PLATFORM_API.countries).flush(
      { type: 'about:blank', title: 'Refused', status: 422, code: 'GEO-002', traceId: null, correlationId: null },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    const error = await saving;
    expect([error.code, error.problem.errors]).toEqual(['VALIDATION-400', { dialCode: ['platform.countries.error.GEO-002'] }]);

    const duplicate = refusal(page.config.adapter.create({ ...uzbekistan, dialCode: '+998' }));
    await settle();
    backend.expectOne(PLATFORM_API.countries).flush(
      { type: 'about:blank', title: 'Exists', status: 409, code: 'GEO-100', traceId: null, correlationId: null },
      { status: 409, statusText: 'Conflict' },
    );
    expect((await duplicate).code).toBe('GEO-100');
  });

  it('sends a valid dial code', async () => {
    const saving = page.config.adapter.create({ ...uzbekistan, dialCode: '+998' });
    await settle();
    const request = backend.expectOne(PLATFORM_API.countries);
    expect((request.request.body as CountryRequest).dialCode).toBe('+998');
    request.flush({ id: 'uz' });
    await saving;
  });

  it('C-09 live check: the dial code field carries the GEO-002 rule as a pattern with its message, the same rule as Save', () => {
    const field = page.config.fields.find((candidate) => candidate.name === 'dialCode');
    expect([field?.pattern, field?.patternMessage]).toEqual([DIAL_CODE_AS_TYPED, 'platform.countries.error.GEO-002']);
    for (const typed of ['+20', '+998', '+1', '+1234', '+ 20', '+9 98', '20', '+0', '+020', '+12345', '+', '+2a', '++20']) {
      expect([typed, DIAL_CODE_AS_TYPED.test(typed.trim())]).toEqual([typed, isDialCodeValid(typed)]);
    }
  });
});
