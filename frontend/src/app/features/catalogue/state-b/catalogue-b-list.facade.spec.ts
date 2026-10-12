import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { CATALOGUE_B_API, type Paged, type PreparationListItem } from '../../../core/api/catalogue-b.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { DoctorsFacade, PreparationsFacade, TestRulesFacade, toQuery } from './catalogue-b-list.facade';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

function page<T>(items: T[]): Paged<T> {
  return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 };
}

const fasting: PreparationListItem = {
  id: 'p1',
  code: 'FAST',
  questionEn: 'Fasting hours?',
  questionAr: 'ساعات الصيام؟',
  answerType: 'OptionList',
  options: ['8 h', '12 h'],
  gender: 'Any',
  services: [],
  isActive: true,
};

describe('Catalogue B list facades (01 §6.3)', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), PreparationsFacade, TestRulesFacade, DoctorsFacade],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  /** Runs the resource effects (which issue the HTTP calls) and lets the promises settle. */
  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  it('B-03: sorts the whole register on the server from page 1, by the key of the column in the language on screen', async () => {
    const facade = TestBed.inject(PreparationsFacade);
    await runEffects();
    backend.expectOne((request) => request.url === CATALOGUE_B_API.preparations).flush(page([]));

    facade.setPage({ page: 3, pageSize: 20 });
    await runEffects();
    backend.expectOne((request) => request.url === CATALOGUE_B_API.preparations).flush(page([]));

    facade.setSort({ key: 'question', direction: 'desc' });
    await runEffects();
    const sorted = backend.expectOne((request) => request.url === CATALOGUE_B_API.preparations);
    expect([sorted.request.params.get('sort'), sorted.request.params.get('direction'), sorted.request.params.get('page')]).toEqual(['questionEn', 'desc', '1']);
    sorted.flush(page([]));

    TestBed.inject(I18nService).setLanguage('ar');
    await runEffects();
    const arabic = backend.expectOne((request) => request.url === CATALOGUE_B_API.preparations);
    expect(arabic.request.params.get('sort')).toBe('questionAr');
    arabic.flush(page([]));
    backend.match(() => true).forEach((request) => request.flush({}));   // dictionaries of the language switch
    TestBed.inject(I18nService).setLanguage('en');
    await runEffects();
    backend.match(() => true).forEach((request) => request.flush(page([])));

    facade.setSort(null);
    await runEffects();
    expect(backend.expectOne((request) => request.url === CATALOGUE_B_API.preparations).request.params.has('sort')).toBe(false);
  });

  it('B-03: each register maps its own columns (doctors: the specialty; a column without a server key sends no sort)', async () => {
    const doctors = TestBed.inject(DoctorsFacade);
    await runEffects();
    backend.expectOne((request) => request.url === CATALOGUE_B_API.doctors).flush(page([]));

    doctors.setSort({ key: 'specialty', direction: 'asc' });
    await runEffects();
    const bySpecialty = backend.expectOne((request) => request.url === CATALOGUE_B_API.doctors);
    expect([bySpecialty.request.params.get('sort'), bySpecialty.request.params.get('direction')]).toEqual(['specialty', 'asc']);
    bySpecialty.flush(page([]));

    doctors.setSort({ key: 'branches', direction: 'asc' });
    await runEffects();
    expect(backend.expectOne((request) => request.url === CATALOGUE_B_API.doctors).request.params.has('sort')).toBe(false);
  });

  it('loads the active records first, then follows search, status, extra filters and paging', async () => {
    const facade = TestBed.inject(PreparationsFacade);
    await runEffects();

    const first = backend.expectOne((request) => request.url === CATALOGUE_B_API.preparations);
    expect(first.request.params.get('isActive')).toBe('true');
    expect(first.request.params.get('page')).toBe('1');
    expect(first.request.params.has('search')).toBe(false);
    first.flush(page([fasting]));
    await runEffects();
    expect(facade.rows()).toEqual([fasting]);
    expect(facade.totalCount()).toBe(1);

    facade.setSearch('fast');
    facade.setActiveFilter(null);
    facade.setExtra('answerType', 'OptionList');
    facade.setPage({ page: 3, pageSize: 20 });
    await runEffects();

    const filtered = backend.expectOne((request) => request.url === CATALOGUE_B_API.preparations);
    expect(filtered.request.params.get('search')).toBe('fast');
    expect(filtered.request.params.has('isActive')).toBe(false);
    expect(filtered.request.params.get('answerType')).toBe('OptionList');
    expect(filtered.request.params.get('page')).toBe('3');
    expect(filtered.request.params.get('pageSize')).toBe('20');
    filtered.flush(page([]));
    backend.verify();
  });

  it('creates, deactivates and reloads; the export keeps the filter but not the page', async () => {
    const facade = TestBed.inject(PreparationsFacade);
    await runEffects();
    backend.expectOne((request) => request.url === CATALOGUE_B_API.preparations).flush(page([fasting]));

    const created = facade.create.execute({ code: 'MED', questionEn: 'Medication?', questionAr: 'الأدوية؟', answerType: 'Text', options: [], gender: 'Any', serviceIds: [] });
    await settle();
    const post = backend.expectOne((request) => request.method === 'POST' && request.url === CATALOGUE_B_API.preparations);
    expect(post.request.body).toEqual({ code: 'MED', questionEn: 'Medication?', questionAr: 'الأدوية؟', answerType: 'Text', options: [], gender: 'Any', serviceIds: [] });
    post.flush({ id: 'p2' }, { status: 201, statusText: 'Created' });
    expect(await created).toEqual({ id: 'p2' });
    await runEffects();
    backend.expectOne((request) => request.method === 'GET' && request.url === CATALOGUE_B_API.preparations).flush(page([fasting]));

    const deactivated = facade.activation.execute({ id: 'p1', active: false });
    await settle();
    backend.expectOne(`${CATALOGUE_B_API.preparations}/p1/deactivate`).flush(null, { status: 204, statusText: 'No Content' });
    expect(await deactivated).toBeUndefined();
    await runEffects();
    backend.expectOne((request) => request.method === 'GET' && request.url === CATALOGUE_B_API.preparations).flush(page([]));

    expect(toQuery({ search: 'x', isActive: false, page: 4, pageSize: 20, extra: { answerType: 'Text' } }, false)).toEqual({ search: 'x', isActive: false, answerType: 'Text' });
    backend.verify();
  });

  it('a refused write keeps the problem for the page', async () => {
    const facade = TestBed.inject(PreparationsFacade);
    await runEffects();
    backend.expectOne((request) => request.url === CATALOGUE_B_API.preparations).flush(page([]));

    const result = facade.create.execute({ code: 'FAST', questionEn: 'Q', questionAr: 'س', answerType: 'OptionList', options: ['one'], gender: 'Any', serviceIds: [] });
    await settle();
    backend
      .expectOne((request) => request.method === 'POST')
      .flush({ type: 'about:blank', title: 'Refused', status: 422, code: 'CAT-210' }, { status: 422, statusText: 'Unprocessable Entity' });

    expect(await result).toBeNull();
    expect(facade.create.problem()).not.toBeNull();
    backend.verify();
  });

  it('checks a test rule without saving', async () => {
    const facade = TestBed.inject(TestRulesFacade);
    await runEffects();
    backend.expectOne((request) => request.url === CATALOGUE_B_API.testRules).flush(page([]));

    const result = facade.validate.execute({ ruleId: null, triggerServiceIds: ['a'], addedServiceIds: ['a', 'b'] });
    await settle();
    const check = backend.expectOne(CATALOGUE_B_API.testRuleValidation);
    expect(check.request.method).toBe('POST');
    check.flush({ isValid: false, issues: [{ code: 'CAT-242', severity: 'Error', message: 'both sides', serviceId: 'a', otherRuleId: null }] });

    expect((await result)?.issues[0]?.code).toBe('CAT-242');
    backend.verify();
  });
});
