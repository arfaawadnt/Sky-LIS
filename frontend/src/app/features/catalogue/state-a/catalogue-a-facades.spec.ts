import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { CATALOGUE_A_API, type LabTestDetail, type Paged, type ServiceDetail, type ServiceLookupItem, type ServiceSequenceRow } from '../../../core/api/catalogue-a.contracts';
import { ServicesListFacade, TestsListFacade } from './catalogue-lists.facade';
import { ServiceEditorFacade } from './service-editor.facade';
import { ServiceSequenceFacade } from './service-sequence.facade';
import { TestEditorFacade } from './test-editor.facade';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function page<T>(items: T[]): Paged<T> {
  return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 };
}

/** Runs the resource effects (which issue the HTTP calls) and lets the promises settle. */
async function runEffects(): Promise<void> {
  TestBed.tick();
  await settle();
}

describe('catalogue A facades (01 §6.3)', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        ServicesListFacade,
        TestsListFacade,
        ServiceSequenceFacade,
        ServiceEditorFacade,
        TestEditorFacade,
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  it('ServicesListFacade sends the filter and goes back to page 1 when a filter changes', async () => {
    const facade = TestBed.inject(ServicesListFacade);
    await runEffects();
    const first = backend.expectOne((request) => request.url === CATALOGUE_A_API.services);
    expect(first.request.params.get('isActive')).toBe('true');
    expect(first.request.params.has('type')).toBe(false);
    first.flush(page([]));
    await runEffects();

    facade.setPage({ page: 3, pageSize: 20 });
    await runEffects();
    backend.expectOne((request) => request.url === CATALOGUE_A_API.services && request.params.get('page') === '3').flush(page([]));

    facade.setType('Additional');
    facade.setActive(null);
    await runEffects();
    const filtered = backend.expectOne((request) => request.url === CATALOGUE_A_API.services);
    expect(filtered.request.params.get('type')).toBe('Additional');
    expect(filtered.request.params.get('page')).toBe('1');
    expect(filtered.request.params.has('isActive')).toBe(false);
    filtered.flush(page([]));
    backend.verify();
  });

  it('TestsListFacade searches by text, type and group', async () => {
    const facade = TestBed.inject(TestsListFacade);
    await runEffects();
    backend.expectOne((request) => request.url === CATALOGUE_A_API.tests).flush(page([]));

    facade.setSearch('glu');
    facade.setType('Formula');
    facade.setGroup('group-1');
    await runEffects();
    const request = backend.expectOne((candidate) => candidate.url === CATALOGUE_A_API.tests);
    expect([request.request.params.get('search'), request.request.params.get('type'), request.request.params.get('clinicalGroupId')]).toEqual(['glu', 'Formula', 'group-1']);
    request.flush(page([]));
    backend.verify();
  });

  it('ServiceSequenceFacade saves the changed rows in one PUT and reloads', async () => {
    const facade = TestBed.inject(ServiceSequenceFacade);
    await runEffects();
    const rows: ServiceSequenceRow[] = [{ id: 's1', code: 'GLU', nameEn: 'Glucose', nameAr: 'جلوكوز', type: 'Test', clinicalGroupNameEn: null, clinicalGroupNameAr: null, sequence: 1, isActive: true }];
    backend.expectOne((request) => request.url === CATALOGUE_A_API.serviceSequence).flush(rows);
    await runEffects();
    expect(facade.rows.value()?.length).toBe(1);

    const pending = facade.save.execute([{ serviceId: 's1', sequence: 5 }]);
    await settle();
    const put = backend.expectOne((request) => request.url === CATALOGUE_A_API.serviceSequence && request.method === 'PUT');
    expect(put.request.body).toEqual({ items: [{ serviceId: 's1', sequence: 5 }] });
    put.flush(null, { status: 204, statusText: 'No Content' });
    await pending;
    await runEffects();
    backend.expectOne((request) => request.url === CATALOGUE_A_API.serviceSequence && request.method === 'GET').flush(rows);
    backend.verify();
  });

  it('ServiceEditorFacade loads the detail and offers no packages or additional services as package members', async () => {
    const facade = TestBed.inject(ServiceEditorFacade);
    facade.load('svc-1');
    await runEffects();
    backend.expectOne(`${CATALOGUE_A_API.services}/svc-1`).flush({ id: 'svc-1', code: 'PANEL' } as ServiceDetail);
    await runEffects();
    expect(facade.detail.value()?.code).toBe('PANEL');

    facade.search('Service', 'ch');
    await runEffects();
    const lookup = backend.expectOne((request) => request.url === CATALOGUE_A_API.lookupServices);
    expect(lookup.request.params.get('search')).toBe('ch');
    const item = (id: string, type: ServiceLookupItem['type']): ServiceLookupItem => ({
      id, code: id, shortName: id, shortcutNo: null, nameEn: id, nameAr: id, type, clinicalGroupId: null, clinicalGroupNameEn: null, clinicalGroupNameAr: null, isOrderable: true,
    });
    lookup.flush(page([item('a', 'Test'), item('b', 'Package'), item('c', 'Additional'), item('d', 'Profile')]));
    await runEffects();
    expect(facade.candidates.value()?.map((candidate) => candidate.id)).toEqual(['a', 'd']);
    backend.verify();
  });

  it('TestEditorFacade writes each configuration to its own endpoint and checks formulas', async () => {
    const facade = TestBed.inject(TestEditorFacade);
    facade.load('t-1');
    await runEffects();
    backend.expectOne(`${CATALOGUE_A_API.tests}/t-1`).flush({ id: 't-1', code: 'LDL' } as LabTestDetail);

    const bands = facade.bands.execute({ id: 't-1', bands: [] });
    await settle();
    const put = backend.expectOne(`${CATALOGUE_A_API.tests}/t-1/range-bands`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ bands: [] });
    put.flush(null, { status: 204, statusText: 'No Content' });
    expect(await bands).toBeUndefined();

    const check = facade.check.execute({ expression: '[CHOL] - [HDL]', testId: 't-1' });
    await settle();
    const post = backend.expectOne(CATALOGUE_A_API.formulaCheck);
    expect(post.request.body).toEqual({ expression: '[CHOL] - [HDL]', testId: 't-1' });
    post.flush({ valid: true, normalized: '[CHOL] - [HDL]', references: ['CHOL', 'HDL'], code: null, message: null, position: null });
    expect((await check)?.references).toEqual(['CHOL', 'HDL']);
    backend.verify();
  });
});
