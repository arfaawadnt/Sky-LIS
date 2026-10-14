import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Location, PlatformLocation } from '@angular/common';
import { type WritableSignal, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { FormArray, FormControl, FormGroup } from '@angular/forms';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import {
  CATALOGUE_A_API,
  CATALOGUE_A_PERMISSIONS,
  type ClinicalGroupListItem,
  type LabTestDetail,
  type LabTestParentView,
  type Paged,
  type SampleConditionView,
  type ServiceLookupItem,
} from '../../../core/api/catalogue-a.contracts';
import type { ProblemDetails } from '../../../core/api/problem-details';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import type { TabItem } from '../../../shared/ui/tabs/tabs.component';
import { CatalogueAOptions } from '../state-a/catalogue-a-options';
import { TestEditorFacade } from '../state-a/test-editor.facade';
import { TestEditorPage, formulaVerdict, isNewTestUrl, normalOrCritical } from './test-editor.page';

/** The parts of the page the scenarios drive (the page keeps them protected). */
interface TestEditorInternals {
  readonly general: FormGroup<{
    code: FormControl<string>;
    name: FormGroup<{ en: FormControl<string>; ar: FormControl<string> }>;
    clinicalGroupId: FormControl<string | null>;
    type: FormControl<string>;
  }>;
  readonly formula: FormControl<string>;
  readonly timing: FormGroup<{ durationHours: FormControl<number | null>; minDurationHours: FormControl<number | null> }>;
  readonly values: FormArray<FormGroup<{ isNormal: FormControl<boolean>; isCritical: FormControl<boolean> }>>;
  readonly bands: FormArray<FormGroup<{ low: FormControl<number | null>; high: FormControl<number | null>; ageTo: FormControl<number | null> }>>;
  readonly detail: () => LabTestDetail | null;
  readonly testId: () => string | null;
  readonly canEdit: () => boolean;
  readonly conditions: WritableSignal<readonly SampleConditionView[]>;
  readonly conditionChoices: () => readonly SampleConditionView[];
  addBand(): void;
  readonly tabs: () => readonly TabItem[];
  readonly activeTab: () => string | null;
  readonly problems: () => Partial<Record<string, ProblemDetails>>;
  removeAt(kind: 'bands' | 'values' | 'paragraphs', index: number): void;
  readonly parents: () => readonly LabTestParentView[];
  readonly parentCandidates: () => readonly ServiceLookupItem[];
  searchParents(search: string): void;
  addParent(item: ServiceLookupItem): void;
  removeParent(serviceId: string): void;
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
  parentServices: [],
  isActive: true,
};

describe('TestEditorPage (CP-41)', () => {
  let allowed: (key: string) => boolean = () => true;
  let backend: HttpTestingController;
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: (key: string) => allowed(key) } satisfies Pick<PermissionsService, 'can'> },
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
    allowed = () => true;
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

  function refusal(code: string): ProblemDetails {
    return { type: 'about:blank', title: 'Refused', status: 422, code, traceId: null, correlationId: null, errors: null };
  }

  it('keeps a refused band and the refused timing after a create, stays on the created test and saves them again (UAT B-04 / C-05)', async () => {
    const editor = await open('/catalogue/tests/new', null);
    editor.general.controls.code.setValue('V11GLU');
    editor.general.controls.name.setValue({ en: 'Glucose', ar: 'جلوكوز' });
    editor.general.controls.clinicalGroupId.setValue('g1');
    editor.addBand();
    editor.bands.at(0).controls.ageTo.setValue(150);
    editor.bands.at(0).controls.low.setValue(5);
    editor.bands.at(0).controls.high.setValue(3);
    editor.timing.controls.durationHours.setValue(6);
    editor.timing.controls.minDurationHours.setValue(10);
    editor.timing.markAsDirty();
    const saving = editor.save();
    await settle();

    backend.expectOne((request) => request.method === 'POST' && request.url === CATALOGUE_A_API.tests).flush({ id: 't9' }, { status: 201, statusText: 'Created' });
    await settle();
    backend.expectOne(`${CATALOGUE_A_API.tests}/t9/timing`).flush(refusal('CAT-070'), { status: 422, statusText: 'Unprocessable Entity' });
    await settle();
    // the next section is still tried, so its own refusal shows too
    backend.expectOne(`${CATALOGUE_A_API.tests}/t9/range-bands`).flush(refusal('CAT-053'), { status: 422, statusText: 'Unprocessable Entity' });
    await saving;
    await settle();
    TestBed.tick();

    expect(editor.testId()).toBe('t9');
    expect(TestBed.inject(Location).path()).toBe('/catalogue/tests/t9');
    expect(editor.problems()['timing']?.code).toBe('CAT-070');
    expect(editor.problems()['ranges']?.code).toBe('CAT-053');
    expect(editor.activeTab()).toBe('timing');
    expect(editor.tabs().find((tab) => tab.id === 'ranges')?.badge).toBe('!');

    // the created test is read back; what the user typed in the refused sections stays on screen
    backend.expectOne(`${CATALOGUE_A_API.tests}/t9`).flush({ ...glucose, id: 't9', code: 'V11GLU', rangeBands: [] });
    await settle();
    TestBed.tick();
    expect(editor.detail()?.code).toBe('V11GLU');
    expect(editor.bands.length).toBe(1);
    expect(editor.bands.at(0).controls.low.value).toBe(5);
    expect(editor.timing.controls.minDurationHours.value).toBe(10);
    expect(editor.general.controls.code.disabled).toBe(true);

    // corrected, the next save is an edit of the created test and sends both sections again
    editor.bands.at(0).controls.low.setValue(3);
    editor.bands.at(0).controls.high.setValue(5);
    editor.timing.controls.minDurationHours.setValue(2);
    const again = editor.save();
    await settle();
    backend.expectNone((request) => request.method === 'POST' && request.url === CATALOGUE_A_API.tests);
    backend.expectOne(`${CATALOGUE_A_API.tests}/t9/timing`).flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    const bands = backend.expectOne(`${CATALOGUE_A_API.tests}/t9/range-bands`);
    expect(bands.request.body).toEqual({ bands: [expect.objectContaining({ low: 3, high: 5, ageTo: 150 })] });
    bands.flush(null, { status: 204, statusText: 'No Content' });
    await again;
    expect(editor.problems()).toEqual({});
  });

  it('starts empty again when history brings the address back to /tests/new after a partly refused create (UAT R1-07)', async () => {
    const editor = await open('/catalogue/tests/new', null);
    editor.general.controls.code.setValue('V12RL');
    editor.general.controls.name.setValue({ en: 'Restricted', ar: 'مقيد' });
    editor.general.controls.clinicalGroupId.setValue('g1');
    editor.timing.controls.durationHours.setValue(6);
    editor.timing.controls.minDurationHours.setValue(10);
    editor.timing.markAsDirty();
    const saving = editor.save();
    await settle();
    backend.expectOne((request) => request.method === 'POST' && request.url === CATALOGUE_A_API.tests).flush({ id: 't9' }, { status: 201, statusText: 'Created' });
    await settle();
    backend.expectOne(`${CATALOGUE_A_API.tests}/t9/timing`).flush(refusal('CAT-070'), { status: 422, statusText: 'Unprocessable Entity' });
    await saving;
    await settle();
    backend.expectOne(`${CATALOGUE_A_API.tests}/t9`).flush({ ...glucose, id: 't9', code: 'V12RL' });
    await settle();
    TestBed.tick();
    expect(editor.testId()).toBe('t9');

    // a history step to the "new" address (the router still holds that route, so it does not navigate)
    const platform = TestBed.inject(PlatformLocation);
    platform.pushState(null, '', '/catalogue/tests/new');
    platform.pushState(null, '', '/catalogue/tests/t9');
    platform.back();
    await settle();
    await settle();
    TestBed.tick();

    expect(editor.testId()).toBeNull();
    expect(editor.detail()).toBeNull();
    expect(editor.general.controls.code.value).toBe('');
    expect(editor.general.controls.code.enabled).toBe(true);
    expect(editor.general.controls.name.value).toEqual({ en: '', ar: '' });
    expect(editor.timing.controls.durationHours.value).toBe(24);
    expect(editor.problems()).toEqual({});
  });

  it('recognises the address of the new-test page', () => {
    expect(isNewTestUrl('/catalogue/tests/new')).toBe(true);
    expect(isNewTestUrl('/catalogue/tests/new?x=1')).toBe(true);
    expect(isNewTestUrl('/catalogue/tests/t9')).toBe(false);
    expect(isNewTestUrl('/catalogue/tests/newer')).toBe(false);
  });

  it('keeps a refused section through the re-read that follows a partly saved update (UAT B-04)', async () => {
    const editor = await open('/catalogue/tests/t1', glucose);
    editor.timing.controls.durationHours.setValue(6);
    editor.timing.controls.minDurationHours.setValue(10);
    editor.timing.markAsDirty();
    editor.bands.at(0).controls.low.setValue(80);
    editor.bands.markAsDirty();
    const saving = editor.save();
    await settle();

    backend.expectOne(`${CATALOGUE_A_API.tests}/t1/timing`).flush(refusal('CAT-070'), { status: 422, statusText: 'Unprocessable Entity' });
    await settle();
    backend.expectOne(`${CATALOGUE_A_API.tests}/t1/range-bands`).flush(null, { status: 204, statusText: 'No Content' });
    await saving;
    await settle();
    TestBed.tick();

    const band = glucose.rangeBands[0];
    if (band === undefined) {
      throw new Error('fixture');
    }

    backend.expectOne(`${CATALOGUE_A_API.tests}/t1`).flush({ ...glucose, rangeBands: [{ ...band, low: 80 }] });
    await settle();
    TestBed.tick();
    expect(editor.timing.controls.minDurationHours.value).toBe(10);
    expect(editor.timing.dirty).toBe(true);
    expect(editor.problems()['timing']?.code).toBe('CAT-070');
    expect(editor.activeTab()).toBe('timing');
    expect(editor.bands.at(0).controls.low.value).toBe(80);
    expect(editor.bands.dirty).toBe(false);
  });

  it('follows the id of the address bar when the router reuses the page (UAT B-05)', async () => {
    const first = await open('/catalogue/tests/t1', glucose);
    const second = (await harness.navigateByUrl('/catalogue/tests/t2', TestEditorPage)) as unknown as TestEditorInternals;
    expect(second).toBe(first);
    TestBed.tick();
    await settle();
    expect(second.detail()).toBeNull();   // the previous record is never shown under the new address

    backend.expectOne(`${CATALOGUE_A_API.tests}/t2`).flush({ ...glucose, id: 't2', code: 'HGB', nameEn: 'Haemoglobin' });
    await settle();
    TestBed.tick();
    expect(second.detail()?.code).toBe('HGB');
    expect(second.general.controls.code.value).toBe('HGB');

    second.general.controls.name.setValue({ en: 'Haemoglobin (blood)', ar: 'هيموجلوبين' });
    second.general.markAsDirty();
    const saving = second.save();
    await settle();
    backend.expectNone((request) => request.method === 'PUT' && request.url === `${CATALOGUE_A_API.tests}/t1`);
    backend.expectOne((request) => request.method === 'PUT' && request.url === `${CATALOGUE_A_API.tests}/t2`).flush(null, { status: 204, statusText: 'No Content' });
    await saving;
  });

  it('is read-only without the edit right (UAT B-12)', async () => {
    allowed = (key) => key !== CATALOGUE_A_PERMISSIONS.edit;
    const editor = await open('/catalogue/tests/t1', glucose);

    expect(editor.canEdit()).toBe(false);
    expect(editor.general.disabled).toBe(true);
    expect(editor.timing.disabled).toBe(true);
    expect(editor.bands.disabled).toBe(true);
    expect(editor.formula.disabled).toBe(true);

    await editor.save();
    backend.expectNone((request) => request.method === 'PUT');
  });

  it('offers only active sample conditions, plus the one the test holds (UAT B-08)', async () => {
    const editor = await open('/catalogue/tests/t1', { ...glucose, sampleConditionId: 'c3' });
    editor.conditions.set([
      { id: 'c1', code: 'FAST', nameEn: 'Fasting', nameAr: 'صائم', isActive: true },
      { id: 'c2', code: 'REFR', nameEn: 'Refrigerated', nameAr: 'مبرد', isActive: false },
      { id: 'c3', code: 'OLD', nameEn: 'Old', nameAr: 'قديم', isActive: false },
    ]);

    expect(editor.conditionChoices().map((condition) => condition.code)).toEqual(['FAST', 'OLD']);
  });

  it('says the position of a refused formula once (UAT B-13)', () => {
    expect(formulaVerdict('The formula is not valid.', '(position 12)', 'The formula ends too early. (position 12)')).toBe('The formula is not valid. (position 12) — The formula ends too early.');
    expect(formulaVerdict('The formula is not valid.', null, null)).toBe('The formula is not valid.');
  });

  describe('validation v0.5.0', () => {
    const urine: LabTestDetail = {
      ...glucose,
      id: 'u1',
      code: 'URC',
      type: 'RestrictedList',
      rangeBands: [],
      listValues: [
        { value: 'Yellow', isNormal: true, isCritical: false, isDefault: false, isPlaceholder: false, colour: null },
        { value: 'Red', isNormal: true, isCritical: true, isDefault: false, isPlaceholder: false, colour: null },
      ],
    };

    function root(): HTMLElement {
      return harness.routeNativeElement as HTMLElement;
    }

    /** The rows of the list-value table, in order. */
    function valueRows(): (HTMLTableRowElement | null)[] {
      return [...root().querySelectorAll<HTMLInputElement>('input[formcontrolname="isCritical"]')].map((flag) => flag.closest('tr'));
    }

    async function rendered(): Promise<void> {
      TestBed.tick();
      await harness.fixture.whenStable();
      await settle();
    }

    it('O-2: Normal and Critical on one value is refused under its flags (CAT-061), nothing is sent, the notice shows and the flag takes the focus', async () => {
      const editor = await open('/catalogue/tests/u1', urine);
      await rendered();
      const rows = valueRows();
      expect(rows[1]?.querySelector('.cell-error')?.textContent).toContain('error.CAT-061');
      expect(rows[0]?.querySelector('.cell-error')).toBeNull();

      editor.values.markAsDirty();
      await editor.save();
      await rendered();

      backend.expectNone((request) => request.method === 'PUT');
      expect(editor.activeTab()).toBe('values');
      expect(root().querySelector('.editor > p.problem[role="alert"]')?.textContent).toContain('masterData.invalid');
      const flag = rows[1]?.querySelector<HTMLInputElement>('input[formcontrolname="isCritical"]');
      expect(flag?.getAttribute('aria-invalid')).toBe('true');
      expect(document.activeElement).toBe(flag);

      editor.values.at(1).controls.isNormal.setValue(false);
      await rendered();
      expect(root().querySelector('.cell-error')).toBeNull();
      expect(root().querySelector('.editor > p.problem[role="alert"]')).toBeNull();   // the notice follows the forms

      const saving = editor.save();
      await settle();
      const put = backend.expectOne(`${CATALOGUE_A_API.tests}/u1/list-values`);
      expect((put.request.body as { values: { isNormal: boolean; isCritical: boolean }[] }).values.map((value) => [value.isNormal, value.isCritical])).toEqual([[true, false], [false, true]]);
      put.flush(null, { status: 204, statusText: 'No Content' });
      await saving;
    });

    it('O-2: the server field error on values[n].isCritical lands under that row with the CAT-061 text', async () => {
      const editor = await open('/catalogue/tests/u1', { ...urine, listValues: urine.listValues.map((value) => ({ ...value, isNormal: !value.isCritical })) });
      editor.values.markAsDirty();
      const saving = editor.save();
      await settle();
      const problem: ProblemDetails = {
        type: 'about:blank', title: 'Invalid', status: 400, code: 'VALIDATION-400', traceId: null, correlationId: null,
        errors: { 'values[1].IsCritical': ['A value is Normal or Critical, not both.'] },
        rules: { 'values[1].IsCritical': [{ rule: 'CAT-061' }] },
      };
      backend.expectOne(`${CATALOGUE_A_API.tests}/u1/list-values`).flush(problem, { status: 400, statusText: 'Bad Request' });
      await saving;
      await rendered();

      expect(editor.activeTab()).toBe('values');
      const rows = valueRows();
      expect(rows[1]?.querySelector('.cell-error')?.textContent).toContain('error.CAT-061');
      expect(rows[0]?.querySelector('.cell-error')).toBeNull();
    });

    it('O-2: the row validator', () => {
      const flags = (isNormal: boolean, isCritical: boolean): { value: unknown } => ({ value: { isNormal, isCritical } });
      expect(normalOrCritical(flags(true, true) as never)).toEqual({ normalAndCritical: true });
      expect([normalOrCritical(flags(true, false) as never), normalOrCritical(flags(false, true) as never), normalOrCritical(flags(false, false) as never)]).toEqual([null, null, null]);
    });

    it('round 4: a save blocked by the forms shows the drawer notice and focuses the first marked field', async () => {
      const editor = await open('/catalogue/tests/new', null);
      await rendered();
      expect(root().querySelector('.editor > p.problem[role="alert"]')).toBeNull();

      await editor.save();
      await rendered();

      backend.expectNone((request) => request.method === 'POST');
      expect(root().querySelector('.editor > p.problem[role="alert"]')?.textContent).toContain('masterData.invalid');
      expect(document.activeElement).toBe(root().querySelector('input[formcontrolname="code"]'));
      expect(editor.activeTab()).toBe('general');
    });
  });

  describe('parent services (CP-41, change request 2026-10-14)', () => {
    const parent = (serviceId: string, code: string, type: LabTestParentView['type']): LabTestParentView => ({ serviceId, code, nameEn: code, nameAr: code, type, isActive: true, order: 1 });
    const lookupItem = (id: string, type: ServiceLookupItem['type']): ServiceLookupItem => ({
      id, code: id.toUpperCase(), shortName: id, shortcutNo: null, nameEn: id, nameAr: id, type, clinicalGroupId: null, clinicalGroupNameEn: null, clinicalGroupNameAr: null, isOrderable: true,
    });
    const carried: LabTestDetail = { ...glucose, parentServices: [parent('s1', 'GLU', 'Test'), parent('s2', 'RENAL', 'Profile')] };

    it('shows the carrying services, lets the test leave a profile but not its own test service, and saves the list with the test', async () => {
      const editor = await open('/catalogue/tests/t1', carried);
      expect(editor.parents().map((row) => row.code)).toEqual(['GLU', 'RENAL']);

      editor.removeParent('s1');   // a test service carries exactly this test: set up from the service
      expect(editor.parents().map((row) => row.code)).toEqual(['GLU', 'RENAL']);
      editor.removeParent('s2');
      expect(editor.parents().map((row) => row.code)).toEqual(['GLU']);

      const saving = editor.save();
      await settle();
      const put = backend.expectOne(`${CATALOGUE_A_API.tests}/t1/parent-services`);
      expect(put.request.body).toEqual({ serviceIds: ['s1'] });
      backend.expectNone((request) => request.method === 'PUT' && request.url === `${CATALOGUE_A_API.tests}/t1`);
      put.flush(null, { status: 204, statusText: 'No Content' });
      await saving;
      await settle();
      TestBed.tick();

      // the re-read carries the new list
      backend.expectOne((request) => request.method === 'GET' && request.url === `${CATALOGUE_A_API.tests}/t1`).flush({ ...carried, parentServices: [parent('s1', 'GLU', 'Test')] });
      await settle();
      TestBed.tick();
      expect(editor.parents().map((row) => row.code)).toEqual(['GLU']);
      expect(editor.problems()).toEqual({});
    });

    it('offers only profiles and cultures the test is not in yet; a refusal shows on the general tab and keeps the choice', async () => {
      const editor = await open('/catalogue/tests/t1', carried);
      editor.searchParents('ren');
      await settle();
      const lookup = backend.expectOne((request) => request.url === CATALOGUE_A_API.lookupServices);
      expect(lookup.request.params.get('search')).toBe('ren');
      lookup.flush(page([lookupItem('s2', 'Profile'), lookupItem('s3', 'Culture'), lookupItem('s4', 'Test'), lookupItem('s5', 'Package'), lookupItem('s6', 'Additional'), lookupItem('s7', 'Profile')]));
      await settle();
      TestBed.tick();
      expect(editor.parentCandidates().map((row) => row.id)).toEqual(['s3', 's7']);

      editor.addParent(lookupItem('s3', 'Culture'));
      editor.addParent(lookupItem('s4', 'Test'));   // never offered, never taken
      expect(editor.parents().map((row) => row.code)).toEqual(['GLU', 'RENAL', 'S3']);
      expect(editor.parentCandidates().map((row) => row.id)).toEqual(['s7']);

      const saving = editor.save();
      await settle();
      const put = backend.expectOne(`${CATALOGUE_A_API.tests}/t1/parent-services`);
      expect(put.request.body).toEqual({ serviceIds: ['s1', 's2', 's3'] });
      put.flush(refusal('CAT-036'), { status: 422, statusText: 'Unprocessable Entity' });
      await saving;

      expect(editor.activeTab()).toBe('general');
      expect(editor.problems()['general']?.code).toBe('CAT-036');
      expect(editor.parents().map((row) => row.code)).toEqual(['GLU', 'RENAL', 'S3']);
      backend.expectNone((request) => request.method === 'GET' && request.url === `${CATALOGUE_A_API.tests}/t1`);
    });

    it('sends the chosen parents right after creating the test', async () => {
      const editor = await open('/catalogue/tests/new', null);
      editor.general.controls.code.setValue('UREA');
      editor.general.controls.name.setValue({ en: 'Urea', ar: 'يوريا' });
      editor.general.controls.clinicalGroupId.setValue('g1');
      editor.addParent(lookupItem('s2', 'Profile'));
      const saving = editor.save();
      await settle();

      backend.expectOne((request) => request.method === 'POST' && request.url === CATALOGUE_A_API.tests).flush({ id: 't9' }, { status: 201, statusText: 'Created' });
      await settle();
      const put = backend.expectOne(`${CATALOGUE_A_API.tests}/t9/parent-services`);
      expect(put.request.body).toEqual({ serviceIds: ['s2'] });
      put.flush(null, { status: 204, statusText: 'No Content' });
      await saving;

      expect(TestBed.inject(Router).url).toBe('/catalogue/tests/t9');
    });
  });
});
