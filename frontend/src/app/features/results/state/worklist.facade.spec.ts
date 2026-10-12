import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { RESULTS_API } from '../../../core/api/results.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { settle, worklistItem, worklistPage } from '../testing/results.fixtures';
import { localDayStart, today } from '../ui/result-format';
import { WorklistFacade } from './worklist.facade';

describe('WorklistFacade (CP-18)', () => {
  let facade: WorklistFacade;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(withInterceptors([problemDetailsInterceptor])), provideHttpClientTesting(), WorklistFacade] });
    facade = TestBed.inject(WorklistFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  async function runEffects(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  function flushLookups(): void {
    for (const url of [RESULTS_API.clinicalGroups, RESULTS_API.tests, RESULTS_API.payers, RESULTS_API.analysers]) {
      backend.match((request) => request.url === url).forEach((call) => call.flush([]));
    }
  }

  it('re-reads the rows and the filter lists when the page opens again, keeping the filter (UAT D-14, A-01)', async () => {
    await runEffects();
    backend.expectOne((request) => request.url === RESULTS_API.worklist).flush(worklistPage([worklistItem()]));
    flushLookups();
    await runEffects();
    facade.setSearch('mona');

    await runEffects();
    backend.expectOne((request) => request.url === RESULTS_API.worklist).flush(worklistPage([worklistItem()]));
    await runEffects();

    facade.refresh();
    await runEffects();
    const again = backend.expectOne((request) => request.url === RESULTS_API.worklist);
    expect(again.request.params.get('search')).toBe('MONA');
    again.flush(worklistPage([]));
    for (const url of [RESULTS_API.clinicalGroups, RESULTS_API.tests, RESULTS_API.payers, RESULTS_API.analysers]) {
      expect(backend.match((request) => request.url === url).length).toBe(1);
    }

    await runEffects();
    expect(facade.rows()).toEqual([]);
  });

  it('B-03: sorts the whole worklist on the server from page 1; the consumed percentage has no server order', async () => {
    await runEffects();
    backend.expectOne((request) => request.url === RESULTS_API.worklist).flush(worklistPage([]));
    flushLookups();
    facade.setPage({ page: 2, pageSize: 50 });
    await runEffects();
    backend.expectOne((request) => request.url === RESULTS_API.worklist).flush(worklistPage([]));

    facade.setSort({ key: 'patient', direction: 'asc' });
    await runEffects();
    const byPatient = backend.expectOne((request) => request.url === RESULTS_API.worklist);
    expect([byPatient.request.params.get('sort'), byPatient.request.params.get('direction'), byPatient.request.params.get('page')]).toEqual(['patientEn', 'asc', '1']);
    byPatient.flush(worklistPage([]));

    facade.setSort({ key: 'delay', direction: 'desc' });
    await runEffects();
    expect(backend.expectOne((request) => request.url === RESULTS_API.worklist).request.params.get('sort')).toBe('delay');

    facade.setSort({ key: 'consumed', direction: 'desc' });
    await runEffects();
    expect(backend.expectOne((request) => request.url === RESULTS_API.worklist).request.params.has('sort')).toBe(false);
  });

  it("loads today's collection window of the branch with the counters and the delay thresholds", async () => {
    await runEffects();
    const first = backend.expectOne((request) => request.url === RESULTS_API.worklist);
    expect(first.request.params.get('from')).toBe(localDayStart(today()));
    expect(first.request.params.get('to')).toBe(localDayStart(today(), 1));
    expect(first.request.params.get('branchOnly')).toBe('true');
    expect(first.request.params.has('status')).toBe(false);
    expect(first.request.params.get('page')).toBe('1');
    first.flush(worklistPage([worklistItem(), worklistItem({ resultId: 'res-2', delayState: 'Late', delaySeconds: 3600 })], { warnPercent: 70, latePercent: 100 }));
    flushLookups();
    await runEffects();

    expect(facade.rows().length).toBe(2);
    expect(facade.list.value()?.counters.delayed).toBe(1);
    expect(facade.list.value()?.warnPercent).toBe(70);
    backend.verify();
  });

  it('sends the status boxes, the search upper-cased and the group with the test reset, back on page 1', async () => {
    await runEffects();
    backend.expectOne((request) => request.url === RESULTS_API.worklist).flush(worklistPage([]));
    flushLookups();
    await runEffects();

    facade.setPage({ page: 3, pageSize: 20 });
    facade.update({ testId: 'test-9' });
    facade.setGroup('grp-1');
    facade.toggleStatus('Ordered');
    facade.toggleStatus('Complete');
    facade.update({ delayedOnly: true });
    facade.setSearch('  acc-1 ');
    await runEffects();

    const filtered = backend.expectOne((request) => request.url === RESULTS_API.worklist);
    expect(filtered.request.params.getAll('status')).toEqual(['Ordered', 'Complete']);
    expect(filtered.request.params.get('clinicalGroupId')).toBe('grp-1');
    expect(filtered.request.params.has('testId')).toBe(false);
    expect(filtered.request.params.get('delayedOnly')).toBe('true');
    expect(filtered.request.params.get('search')).toBe('ACC-1');
    expect(filtered.request.params.get('page')).toBe('1');
    expect(filtered.request.params.get('pageSize')).toBe('20');
    filtered.flush(worklistPage([]));
    backend.match((request) => request.url === RESULTS_API.tests).forEach((call) => call.flush([]));

    facade.toggleStatus('Ordered');
    await runEffects();
    const again = backend.expectOne((request) => request.url === RESULTS_API.worklist);
    expect(again.request.params.getAll('status')).toEqual(['Complete']);
    again.flush(worklistPage([]));
    backend.verify();
  });

  it('fetches the bench-list document of the current filter without paging', async () => {
    await runEffects();
    backend.expectOne((request) => request.url === RESULTS_API.worklist).flush(worklistPage([]));
    flushLookups();
    await runEffects();

    facade.update({ clinicalGroupId: 'grp-1' });
    const pending = facade.document.execute(undefined);
    await settle();
    const request = backend.expectOne((call) => call.url === RESULTS_API.worklistDocument);
    expect(request.request.params.get('clinicalGroupId')).toBe('grp-1');
    expect(request.request.params.has('page')).toBe(false);
    request.flush({ letterhead: { labNameEn: 'Lab', labNameAr: 'مختبر', branchNameEn: null, branchNameAr: null }, generatedAtUtc: '2026-10-10T10:00:00Z', generatedBy: 'me', fromUtc: null, toUtc: null, groupNameEn: 'Chemistry', groupNameAr: 'الكيمياء', rows: [worklistItem()], truncated: false });
    const doc = await pending;
    expect(doc?.rows.length).toBe(1);

    backend.match((call) => call.url === RESULTS_API.worklist).forEach((call) => call.flush(worklistPage([])));
    backend.match((call) => call.url === RESULTS_API.tests).forEach((call) => call.flush([]));
    backend.verify();
  });

  it('keeps the problem of a refused load', async () => {
    await runEffects();
    backend.expectOne((request) => request.url === RESULTS_API.worklist).flush({ code: 'AUTHZ-001', title: 'no', status: 403 }, { status: 403, statusText: 'Forbidden' });
    flushLookups();
    await runEffects();

    expect(facade.rows()).toEqual([]);
    expect(facade.list.error()).toBeTruthy();
    backend.verify();
  });
});
