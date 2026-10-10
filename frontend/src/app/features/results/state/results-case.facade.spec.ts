import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { RESULTS_API } from '../../../core/api/results.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { caseView, page, problemBody, reportDocument, reportUnit, resultRow, settle, visitItem } from '../testing/results.fixtures';
import { ResultsCaseFacade } from './results-case.facade';

describe('ResultsCaseFacade (CP-19)', () => {
  let facade: ResultsCaseFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(withInterceptors([problemDetailsInterceptor])), provideHttpClientTesting(), ResultsCaseFacade] });
    facade = TestBed.inject(ResultsCaseFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  async function openCase(rows = [resultRow()]): Promise<void> {
    facade.open({ accession: 'ACC-2026-00001' });
    await runEffects();
    const request = backend.expectOne((call) => call.url === RESULTS_API.results);
    expect(request.request.params.get('accession')).toBe('ACC-2026-00001');
    request.flush(caseView([reportUnit(rows)]));
    await runEffects();
  }

  it('searches visits with the panel criteria and opens a case by accession or visit id', async () => {
    facade.run({ accession: null, patientNumber: 'PAT', patientName: 'mo%', status: 'Complete', sampleStatus: null, analyserId: null, notPrinted: true, notVerified: false, delta: false, from: null, to: null, branchOnly: true, page: 1, pageSize: 40 });
    await runEffects();
    const search = backend.expectOne((call) => call.url === RESULTS_API.visits);
    expect(search.request.params.get('patientNumber')).toBe('PAT');
    expect(search.request.params.get('patientName')).toBe('mo%');
    expect(search.request.params.get('status')).toBe('Complete');
    expect(search.request.params.get('notPrinted')).toBe('true');
    expect(search.request.params.has('notVerified')).toBe(false);
    expect(search.request.params.get('branchOnly')).toBe('true');
    search.flush(page([visitItem()]));
    await runEffects();
    expect(facade.visits.value()?.items.length).toBe(1);

    await openCase();
    expect(facade.current()?.visit.accession).toBe('ACC-2026-00001');
    expect(facade.activeUnit()?.key).toBe('unit-1');

    facade.open({ visitId: 'visit-1' });
    await runEffects();
    const byId = backend.expectOne((call) => call.url === RESULTS_API.results);
    expect(byId.request.params.get('visitId')).toBe('visit-1');
    byId.flush(caseView());
    backend.verify();
  });

  it('keeps grid drafts, saves them together and leaves refused rows dirty with their code', async () => {
    const glucose = resultRow();
    const sodium = resultRow({ id: 'res-2', testCode: 'NA', testNameEn: 'Sodium', testType: 'Numeric', decimals: 0 });
    await openCase([glucose, sodium]);
    const unit = facade.activeUnit();
    expect(unit).not.toBeNull();

    facade.setDraft(glucose, '95.2');
    facade.setDraft(sodium, 'abc');
    expect(facade.dirtyCount()).toBe(2);
    const items = facade.draftItems(unit!);
    expect(items).toEqual([
      { resultId: 'res-1', numericValue: 95.2, textValue: null, paragraphs: null, culture: null },
      { resultId: 'res-2', numericValue: null, textValue: 'abc', paragraphs: null, culture: null },
    ]);

    const pending = facade.save.execute(items);
    await settle();
    const put = backend.expectOne(RESULTS_API.values);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ values: items });
    put.flush({ changed: ['res-1'], refused: [{ resultId: 'res-2', code: 'RES-003', message: 'not a number' }] });
    await pending;
    await runEffects();

    expect(facade.drafts()).toEqual({ 'res-2': 'abc' });
    expect(facade.refusals()).toEqual({ 'res-2': 'RES-003' });
    backend.expectOne((call) => call.url === RESULTS_API.results).flush(caseView([reportUnit([{ ...glucose, status: 'Complete', numericValue: 95.2, textValue: '95.2' }, sodium])]));
    backend.verify();
  });

  it('reviews and verifies the eligible rows of a unit and surfaces unacknowledged deltas', async () => {
    const complete = resultRow({ id: 'res-1', status: 'Complete', numericValue: 95, textValue: '95.0' });
    const reviewed = resultRow({ id: 'res-2', status: 'Reviewed', numericValue: 140, textValue: '140', delta: { flag: true, percent: 40, previousValue: 100, previousResultId: 'old', acknowledged: false, acknowledgedByName: null, acknowledgedAtUtc: null, comment: null } });
    const ordered = resultRow({ id: 'res-3' });
    await openCase([complete, reviewed, ordered]);
    const unit = facade.activeUnit()!;

    expect(facade.reviewable(unit)).toEqual(['res-1']);
    expect(facade.verifiable(unit)).toEqual(['res-1', 'res-2']);
    expect(facade.unacknowledgedDeltas(unit).map((row) => row.id)).toEqual(['res-2']);
    expect(facade.undoable(unit)).toEqual(['res-2']);

    const pending = facade.acknowledgeAndVerify.execute({ deltaIds: ['res-2'], verifyIds: ['res-1', 'res-2'], comment: null });
    await settle();
    backend.expectOne(RESULTS_API.acknowledgeDelta('res-2')).flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    const verify = backend.expectOne(RESULTS_API.verifyMany);
    expect(verify.request.body).toEqual({ resultIds: ['res-1', 'res-2'] });
    verify.flush({ changed: ['res-1', 'res-2'], refused: [] });
    expect((await pending)?.changed).toEqual(['res-1', 'res-2']);
    await runEffects();
    backend.expectOne((call) => call.url === RESULTS_API.results).flush(caseView());
    backend.verify();
  });

  it('undoes every listed row with the reason and collects refusals instead of stopping', async () => {
    await openCase([resultRow({ id: 'res-1', status: 'Verified' }), resultRow({ id: 'res-2', status: 'Printed' })]);

    const pending = facade.undo.execute({ ids: ['res-1', 'res-2'], reason: 'wrong sample' });
    await settle();
    const first = backend.expectOne(RESULTS_API.undo('res-1'));
    expect(first.request.body).toEqual({ reason: 'wrong sample' });
    first.flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    backend.expectOne(RESULTS_API.undo('res-2')).flush(problemBody('RES-018', 409), { status: 409, statusText: 'Conflict' });
    const outcome = await pending;

    expect(outcome?.changed).toEqual(['res-1']);
    expect(outcome?.refused.map((refusal) => refusal.code)).toEqual(['RES-018']);
    expect(facade.refusals()).toEqual({ 'res-2': 'RES-018' });
    await runEffects();
    backend.expectOne((call) => call.url === RESULTS_API.results).flush(caseView());
    backend.verify();
  });

  it('marks the unit printed before fetching the official report, and previews without marking', async () => {
    await openCase([resultRow({ status: 'Verified', numericValue: 95, textValue: '95.0' })]);

    const preview = facade.preview.execute('unit-1');
    await settle();
    const previewRequest = backend.expectOne((call) => call.url === RESULTS_API.report('unit-1'));
    expect(previewRequest.request.params.get('preview')).toBe('true');
    previewRequest.flush(reportDocument({ isPreview: true }));
    expect((await preview)?.isPreview).toBe(true);
    backend.expectNone((call) => call.url === RESULTS_API.print('unit-1'));

    const print = facade.print.execute('unit-1');
    await settle();
    backend.expectOne(RESULTS_API.print('unit-1')).flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    const official = backend.expectOne((call) => call.url === RESULTS_API.report('unit-1'));
    expect(official.request.params.get('preview')).toBe('false');
    official.flush(reportDocument());
    expect((await print)?.printCount).toBe(1);
    await runEffects();
    backend.expectOne((call) => call.url === RESULTS_API.results).flush(caseView());
    backend.verify();
  });

  it('keeps the problem of a refused print (BR-09) and loads the history of a row on demand', async () => {
    const row = resultRow({ status: 'Complete', numericValue: 95, textValue: '95.0' });
    await openCase([row]);

    const print = facade.print.execute('unit-1');
    await settle();
    backend.expectOne(RESULTS_API.print('unit-1')).flush(problemBody('RES-021', 409), { status: 409, statusText: 'Conflict' });
    expect(await print).toBeNull();
    expect(facade.print.problem()?.code).toBe('RES-021');

    facade.openHistory(row, 'previous');
    await runEffects();
    backend.expectOne(RESULTS_API.log('res-1')).flush([{ id: 'log-1', kind: 'Value', fromStatus: null, toStatus: null, valueBefore: null, valueAfter: '95.0', source: 'Manual', analyserId: null, atUtc: '2026-10-10T09:00:00Z', byUserId: 'u1', byName: 'Tech', detail: null }]);
    backend.expectOne(RESULTS_API.previous('res-1')).flush([]);
    await runEffects();
    expect(facade.historyTab()).toBe('previous');
    expect(facade.log.value()?.length).toBe(1);
    backend.verify();
  });
});
