import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { type BranchListItem, type CreateBranchRequest, ORGANIZATION_API } from '../../../core/api/organization.contracts';
import { ProblemCodes, ProblemError, type ProblemDetails } from '../../../core/api/problem-details';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import type { MasterDataFilter } from '../../../shared/ui/master-data-page/master-data.types';
import { BranchesPage } from './branches.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const main: BranchListItem = { id: 'b1', code: 'MAIN', nameEn: 'Main', nameAr: 'الرئيسي', countryId: 'eg', governorateId: 'alx', phone: null, email: null, currency: 'EGP', isActive: true };

const request: CreateBranchRequest = { code: 'NORTH', nameEn: 'North', nameAr: 'الشمال', countryId: 'eg', governorateId: null, phone: null, email: 'not-an-email', currency: 'EGP' };

/** The parts of the page the scenarios drive (the page keeps them protected). */
interface BranchesInternals {
  readonly config: {
    readonly adapter: {
      list(filter: MasterDataFilter, page: { page: number; pageSize: number }): Promise<unknown>;
      create(dto: CreateBranchRequest): Promise<unknown>;
    };
  };
  exportCsv(): Promise<void>;
}

function refusal(code: string): ProblemDetails {
  return { type: 'about:blank', title: 'Refused', status: 422, code, traceId: null, correlationId: null, errors: null };
}

describe('BranchesPage (CP-36)', () => {
  let backend: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BranchesPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function open(): Promise<BranchesInternals> {
    const fixture = TestBed.createComponent(BranchesPage);
    TestBed.tick();
    await settle();
    backend.match((candidate) => candidate.url === ORGANIZATION_API.branches).forEach((candidate) => candidate.flush({ items: [main], page: 1, pageSize: 50, totalCount: 1, totalPages: 1 }));
    await settle();
    return fixture.componentInstance as unknown as BranchesInternals;
  }

  async function refusedWith(page: BranchesInternals, code: string): Promise<ProblemError> {
    const creating = page.config.adapter.create(request).then(
      () => {
        throw new Error('the create should be refused');
      },
      (error: unknown) => error,
    );
    await settle();
    backend.expectOne({ method: 'POST', url: ORGANIZATION_API.branches }).flush(refusal(code), { status: 422, statusText: 'Unprocessable Entity' });
    const error = await creating;
    if (!(error instanceof ProblemError)) {
      throw new Error('a problem was expected');
    }

    return error;
  }

  it('marks the e-mail field with the refusal instead of a general banner (UAT B-03)', async () => {
    const page = await open();
    const error = await refusedWith(page, 'ORG-016');

    expect(error.code).toBe(ProblemCodes.validationFailed);
    expect(Object.keys(error.problem.errors ?? {})).toEqual(['email']);
  });

  it('marks the governorate field when it belongs to another country (UAT B-02)', async () => {
    const page = await open();
    const error = await refusedWith(page, 'ORG-017');

    expect(Object.keys(error.problem.errors ?? {})).toEqual(['governorateId']);
  });

  it('leaves other refusals to the banner', async () => {
    const page = await open();
    const creating = page.config.adapter.create(request).catch((error: unknown) => error);
    await settle();
    backend.expectOne({ method: 'POST', url: ORGANIZATION_API.branches }).flush({ ...refusal('ORG-100'), status: 409 }, { status: 409, statusText: 'Conflict' });
    const error = await creating;

    expect(error instanceof ProblemError && error.code).toBe('ORG-100');
  });

  it('exports exactly the branches of the current filter, with place names and Yes / No (UAT B-10)', async () => {
    const page = await open();
    const listed = page.config.adapter.list({ search: 'ma', active: null }, { page: 1, pageSize: 50 });
    await settle();
    backend.expectOne((candidate) => candidate.url === ORGANIZATION_API.branches).flush({ items: [main], page: 1, pageSize: 50, totalCount: 1, totalPages: 1 });
    await listed;

    const exporting = page.exportCsv();
    await settle();
    const list = backend.expectOne((candidate) => candidate.url === ORGANIZATION_API.branches);
    expect(list.request.params.get('search')).toBe('ma');
    expect(list.request.params.has('isActive')).toBe(false);
    expect(list.request.params.get('pageSize')).toBe('200');
    list.flush({ items: [main], page: 1, pageSize: 200, totalCount: 1, totalPages: 1 });
    backend.match((candidate) => candidate.url === ORGANIZATION_API.countries).forEach((candidate) => candidate.flush([{ id: 'eg', isoCode: 'EG', nameEn: 'Egypt', nameAr: 'مصر' }]));
    backend.match((candidate) => candidate.url === ORGANIZATION_API.governorates).forEach((candidate) => candidate.flush([{ id: 'alx', nameEn: 'Alexandria', nameAr: 'الإسكندرية', countryId: 'eg' }]));
    await exporting;
  });
});
