import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { FormControl, FormGroup } from '@angular/forms';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { CATALOGUE_A_API, type ClinicalGroupListItem, type Paged, type SampleTypeListItem, type ServiceDetail, type TestLookupItem } from '../../../core/api/catalogue-a.contracts';
import type { ProblemDetails } from '../../../core/api/problem-details';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import type { MemberCandidate } from '../state-a/service-editor.facade';
import { CatalogueAOptions } from '../state-a/catalogue-a-options';
import { ServiceEditorFacade } from '../state-a/service-editor.facade';
import { type MemberRow, ServiceEditorPage } from './service-editor.page';

/** The parts of the page the scenarios drive (the page keeps them protected). */
interface ServiceEditorInternals {
  readonly form: FormGroup<{
    code: FormControl<string>;
    type: FormControl<string>;
    shortName: FormControl<string>;
    name: FormGroup<{ en: FormControl<string>; ar: FormControl<string> }>;
    clinicalGroupId: FormControl<string | null>;
    sampleTypeId: FormControl<string | null>;
  }>;
  readonly members: () => readonly MemberRow[];
  readonly candidates: () => readonly MemberCandidate[];
  readonly activeTab: () => string | null;
  readonly membersProblem: () => ProblemDetails | null;
  addMember(candidate: MemberCandidate): void;
  moveMember(index: number, offset: -1 | 1): void;
  save(): Promise<void>;
}

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function page<T>(items: T[]): Paged<T> {
  return { items, page: 1, pageSize: 200, totalCount: items.length, totalPages: 1 };
}

const group: ClinicalGroupListItem = { id: 'g1', code: 'CHEM', unitCode: 'C', nameEn: 'Chemistry', nameAr: 'الكيمياء', reportName: null, reportTemplateId: null, displayOrder: 1, isActive: true };
const serum: SampleTypeListItem = { id: 's1', code: 'SERUM', shortName: 'SER', nameEn: 'Serum', nameAr: 'مصل', activeConditionCount: 0, isActive: true };

function test(id: string, code: string): TestLookupItem {
  return { id, code, nameEn: code, nameAr: code, type: 'Numeric', clinicalGroupId: 'g1', clinicalGroupNameEn: 'Chemistry', clinicalGroupNameAr: 'الكيمياء', unitName: null };
}

describe('ServiceEditorPage (CP-40)', () => {
  let backend: HttpTestingController;
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
        provideRouter([
          {
            path: 'catalogue/services',
            children: [
              { path: 'new', providers: [ServiceEditorFacade, CatalogueAOptions], component: ServiceEditorPage },
              { path: ':id', providers: [ServiceEditorFacade, CatalogueAOptions], component: ServiceEditorPage },
            ],
          },
        ]),
      ],
    });
    backend = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });

  async function open(url: string, detail: ServiceDetail | null): Promise<ServiceEditorInternals> {
    const component = await harness.navigateByUrl(url, ServiceEditorPage);
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.url === CATALOGUE_A_API.clinicalGroups).flush(page([group]));
    backend.expectOne((request) => request.url === CATALOGUE_A_API.sampleTypes).flush(page([serum]));
    backend.expectOne((request) => request.url === CATALOGUE_A_API.units).flush(page([]));
    backend.expectOne((request) => request.url === CATALOGUE_A_API.patientConditions).flush(page([]));
    backend.expectOne((request) => request.url === CATALOGUE_A_API.lookupTests).flush(page([test('t1', 'CHOL'), test('t2', 'HDL'), test('t3', 'TG')]));
    if (detail !== null) {
      backend.expectOne(`${CATALOGUE_A_API.services}/${detail.id}`).flush(detail);
    }

    await settle();
    TestBed.tick();
    return component as unknown as ServiceEditorInternals;
  }

  it('creates a profile with its ordered members in one request', async () => {
    const editor = await open('/catalogue/services/new', null);
    editor.form.controls.type.setValue('Profile');
    editor.form.controls.code.setValue('LIPID');
    editor.form.controls.shortName.setValue('Lipids');
    editor.form.controls.name.setValue({ en: 'Lipid profile', ar: 'دهون الدم' });
    editor.form.controls.clinicalGroupId.setValue('g1');
    editor.form.controls.sampleTypeId.setValue('s1');
    TestBed.tick();
    await settle();
    backend.expectOne(`${CATALOGUE_A_API.sampleTypes}/s1`).flush({ ...serum, conditions: [] });

    const [chol, hdl, tg] = editor.candidates();
    if (!chol || !hdl || !tg) {
      throw new Error('candidates missing');
    }

    editor.addMember(chol);
    editor.addMember(tg);
    editor.addMember(hdl);
    editor.moveMember(2, -1);   // CHOL, HDL, TG
    expect(editor.candidates().length).toBe(0);

    const saving = editor.save();
    await settle();
    const create = backend.expectOne((request) => request.method === 'POST' && request.url === CATALOGUE_A_API.services);
    expect(create.request.body).toEqual(
      expect.objectContaining({
        code: 'LIPID',
        type: 'Profile',
        clinicalGroupId: 'g1',
        sampleTypeId: 's1',
        members: [
          { kind: 'Test', memberId: 't1', order: 1, inSum: false, visible: true },
          { kind: 'Test', memberId: 't2', order: 2, inSum: false, visible: true },
          { kind: 'Test', memberId: 't3', order: 3, inSum: false, visible: true },
        ],
      }),
    );
    create.flush({ id: 'svc-9' }, { status: 201, statusText: 'Created' });
    await saving;

    expect(TestBed.inject(Router).url).toBe('/catalogue/services/svc-9');
  });

  it('saves only the members of an existing service and shows a member refusal on the members tab', async () => {
    const detail: ServiceDetail = {
      id: 'svc-1',
      code: 'GLU',
      type: 'Test',
      shortName: 'GLU',
      shortcutNo: 1,
      nameEn: 'Glucose',
      nameAr: 'جلوكوز',
      reportName: null,
      loinc: null,
      cpt: null,
      clinicalGroupId: 'g1',
      sampleTypeId: 's1',
      sampleConditionId: null,
      header: null,
      aliases: ['Sugar'],
      precaution: null,
      gender: 'Any',
      turnaroundHours: 24,
      labelCount: 1,
      printAlone: false,
      isOrderable: true,
      showOnWeb: false,
      profileSum: null,
      sequence: 4,
      isActive: true,
      members: [{ kind: 'Test', memberId: 't1', code: 'CHOL', nameEn: 'CHOL', nameAr: 'CHOL', memberType: 'Numeric', isActive: true, order: 1, inSum: false, visible: true }],
    };
    const editor = await open('/catalogue/services/svc-1', detail);
    backend.expectOne(`${CATALOGUE_A_API.sampleTypes}/s1`).flush({ ...serum, conditions: [] });
    expect(editor.form.controls.code.disabled).toBe(true);
    expect(editor.members().length).toBe(1);

    const hdl = editor.candidates().find((candidate) => candidate.code === 'HDL');
    if (!hdl) {
      throw new Error('HDL missing');
    }

    editor.addMember(hdl);
    const saving = editor.save();
    await settle();

    backend.expectNone((request) => request.method === 'PUT' && request.url === `${CATALOGUE_A_API.services}/svc-1`);
    const members = backend.expectOne(`${CATALOGUE_A_API.services}/svc-1/members`);
    expect(members.request.body).toEqual({
      members: [
        { kind: 'Test', memberId: 't1', order: 1, inSum: false, visible: true },
        { kind: 'Test', memberId: 't2', order: 2, inSum: false, visible: true },
      ],
    });
    const problem: ProblemDetails = { type: 'about:blank', title: 'Refused', status: 422, code: 'CAT-021', traceId: null, correlationId: null, errors: null };
    members.flush(problem, { status: 422, statusText: 'Unprocessable Entity' });
    await saving;

    expect(editor.activeTab()).toBe('members');
    expect(editor.membersProblem()?.code).toBe('CAT-021');
  });
});
