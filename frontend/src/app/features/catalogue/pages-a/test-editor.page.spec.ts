import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { FormControl, FormGroup } from '@angular/forms';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { CATALOGUE_A_API, type ClinicalGroupListItem, type LabTestDetail, type Paged } from '../../../core/api/catalogue-a.contracts';
import type { ProblemDetails } from '../../../core/api/problem-details';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import type { TabItem } from '../../../shared/ui/tabs/tabs.component';
import { CatalogueAOptions } from '../state-a/catalogue-a-options';
import { TestEditorFacade } from '../state-a/test-editor.facade';
import { TestEditorPage } from './test-editor.page';

/** The parts of the page the scenarios drive (the page keeps them protected). */
interface TestEditorInternals {
  readonly general: FormGroup<{
    code: FormControl<string>;
    name: FormGroup<{ en: FormControl<string>; ar: FormControl<string> }>;
    clinicalGroupId: FormControl<string | null>;
    type: FormControl<string>;
  }>;
  readonly formula: FormControl<string>;
  readonly tabs: () => readonly TabItem[];
  readonly activeTab: () => string | null;
  readonly problems: () => Partial<Record<string, ProblemDetails>>;
  removeAt(kind: 'bands' | 'values' | 'paragraphs', index: number): void;
  save(): Promise<void>;
}

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const group: ClinicalGroupListItem = { id: 'g1', code: 'CHEM', unitCode: 'C', nameEn: 'Chemistry', nameAr: 'الكيمياء', reportName: null, reportTemplateId: null, displayOrder: 1, isActive: true };

function page<T>(items: T[]): Paged<T> {
  return { items, page: 1, pageSize: 200, totalCount: items.length, totalPages: 1 };
}

const glucose: LabTestDetail = {
  id: 't1',
  code: 'GLU',
  nameEn: 'Glucose',
  nameAr: 'جلوكوز',
  reportName: null,
  loinc: null,
  cpt: null,
  clinicalGroupId: 'g1',
  type: 'Numeric',
  sampleTypeId: null,
  sampleConditionId: null,
  unitId: null,
  gender: 'Any',
  methodology: null,
  decimals: 0,
  isHiddenHelper: false,
  printReference: true,
  labelCount: 1,
  prefix: null,
  timing: { durationHours: 24, minDurationHours: 0, frequencyCheckDays: null, frequencyAction: 'Alert', deltaPercent: null, deltaWindowDays: null, testDays: 127 },
  formulaExpression: null,
  formulaReferences: [],
  rangeBands: [{ sex: 'Any', ageFrom: 0, ageTo: 150, ageUnit: 'Years', patientConditionId: null, low: 70, high: 110, panicLow: null, panicHigh: null, reportableLow: null, reportableHigh: null, textRange: null }],
  listValues: [],
  paragraphs: [],
  isActive: true,
};

describe('TestEditorPage (CP-41)', () => {
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
            path: 'catalogue/tests',
            children: [
              { path: 'new', providers: [TestEditorFacade, CatalogueAOptions], component: TestEditorPage },
              { path: ':id', providers: [TestEditorFacade, CatalogueAOptions], component: TestEditorPage },
            ],
          },
        ]),
      ],
    });
    backend = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });

  async function open(url: string, detail: LabTestDetail | null): Promise<TestEditorInternals> {
    const component = await harness.navigateByUrl(url, TestEditorPage);
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.url === CATALOGUE_A_API.clinicalGroups).flush(page([group]));
    backend.expectOne((request) => request.url === CATALOGUE_A_API.sampleTypes).flush(page([]));
    backend.expectOne((request) => request.url === CATALOGUE_A_API.units).flush(page([]));
    backend.expectOne((request) => request.url === CATALOGUE_A_API.patientConditions).flush(page([]));
    if (detail !== null) {
      backend.expectOne(`${CATALOGUE_A_API.tests}/${detail.id}`).flush(detail);
    }

    await settle();
    TestBed.tick();
    return component as unknown as TestEditorInternals;
  }

  it('creates a formula test, then saves its formula, and moves to the new record', async () => {
    const editor = await open('/catalogue/tests/new', null);
    editor.general.controls.code.setValue('LDL');
    editor.general.controls.name.setValue({ en: 'LDL cholesterol', ar: 'كوليسترول منخفض الكثافة' });
    editor.general.controls.clinicalGroupId.setValue('g1');
    editor.general.controls.type.setValue('Formula');
    TestBed.tick();

    const tabs = editor.tabs().map((tab) => tab.id);
    expect(tabs).toEqual(['general', 'ranges', 'formula', 'timing']);

    editor.formula.setValue('[CHOL] - [HDL] - [TG] / 5');
    editor.formula.markAsDirty();
    const saving = editor.save();
    await settle();

    const create = backend.expectOne((request) => request.method === 'POST' && request.url === CATALOGUE_A_API.tests);
    expect(create.request.body).toEqual(expect.objectContaining({ code: 'LDL', type: 'Formula', clinicalGroupId: 'g1', nameEn: 'LDL cholesterol' }));
    create.flush({ id: 't9' }, { status: 201, statusText: 'Created' });
    await settle();

    const formula = backend.expectOne(`${CATALOGUE_A_API.tests}/t9/formula`);
    expect(formula.request.body).toEqual({ expression: '[CHOL] - [HDL] - [TG] / 5' });
    backend.expectNone(`${CATALOGUE_A_API.tests}/t9/range-bands`);
    formula.flush(null, { status: 204, statusText: 'No Content' });
    await saving;

    expect(TestBed.inject(Router).url).toBe('/catalogue/tests/t9');
  });

  it('clears the bands before the type change that needs it, then saves the general data', async () => {
    const editor = await open('/catalogue/tests/t1', glucose);
    expect(editor.tabs().map((tab) => tab.id)).toContain('ranges');

    editor.removeAt('bands', 0);
    editor.general.controls.type.setValue('RestrictedList');
    editor.general.markAsDirty();
    const saving = editor.save();
    await settle();

    const clear = backend.expectOne(`${CATALOGUE_A_API.tests}/t1/range-bands`);
    expect(clear.request.body).toEqual({ bands: [] });
    clear.flush(null, { status: 204, statusText: 'No Content' });
    await settle();

    const general = backend.expectOne((request) => request.method === 'PUT' && request.url === `${CATALOGUE_A_API.tests}/t1`);
    expect(general.request.body).toEqual(expect.objectContaining({ type: 'RestrictedList', clinicalGroupId: 'g1' }));
    general.flush(null, { status: 204, statusText: 'No Content' });
    await saving;
    await settle();
    TestBed.tick();
    backend.expectOne((request) => request.method === 'GET' && request.url === `${CATALOGUE_A_API.tests}/t1`).flush({ ...glucose, type: 'RestrictedList', rangeBands: [] });
  });

  it('shows a refused configuration on its own tab with its code', async () => {
    const editor = await open('/catalogue/tests/t1', glucose);
    editor.removeAt('bands', 0);
    const saving = editor.save();
    await settle();

    const problem: ProblemDetails = { type: 'about:blank', title: 'Refused', status: 422, code: 'CAT-054', traceId: null, correlationId: null, errors: null };
    backend.expectOne(`${CATALOGUE_A_API.tests}/t1/range-bands`).flush(problem, { status: 422, statusText: 'Unprocessable Entity' });
    await saving;

    expect(editor.activeTab()).toBe('ranges');
    expect(editor.problems()['ranges']?.code).toBe('CAT-054');
    backend.expectNone((request) => request.method === 'PUT' && request.url === `${CATALOGUE_A_API.tests}/t1`);
  });
});
