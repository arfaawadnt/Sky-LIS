import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { DestroyRef, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { TRACKER_API, TRACKER_LANES } from '../../../core/api/tracker.contracts';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { ManualTimers, resetVisibility, setVisibility, settle, trackerCounters, trackerItem, trackerPage, trackerServiceItem } from '../testing/tracker.fixtures';
import { LANE_PAGE_SIZE, TRACKER_TIMERS, TrackerFacade } from './tracker.facade';

describe('TrackerFacade (LIS-CR-006)', () => {
  let facade: TrackerFacade;
  let backend: HttpTestingController;
  let timers: ManualTimers;

  beforeEach(() => {
    timers = new ManualTimers();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        { provide: TRACKER_TIMERS, useValue: timers },
        TrackerFacade,
      ],
    });
    facade = TestBed.inject(TrackerFacade);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    resetVisibility();
  });

  async function run(): Promise<void> {
    TestBed.tick();
    await settle();
  }

  const visits = (): ReturnType<HttpTestingController['match']> => backend.match((request) => request.url === TRACKER_API.visits);

  it('sends the filters as query parameters, dropping the empty ones, and starts every change at page 1', async () => {
    await run();
    const first = backend.expectOne((request) => request.url === TRACKER_API.visits);
    expect(first.request.params.keys().sort()).toEqual(['page', 'pageSize']);
    first.flush(trackerPage([trackerItem()]));
    await run();
    expect(facade.rows().length).toBe(1);
    expect(facade.counters().inLab).toBe(4);

    facade.setPage({ page: 3, pageSize: 20 });
    facade.setView('Verification');
    facade.setDay('2026-10-21');
    facade.toggleStage('InLab');
    facade.toggleAttention('Panic');
    facade.setSearch('  mona  ');
    facade.setSort({ key: 'timeLeft', direction: 'desc' });
    await run();

    const params = backend.expectOne((request) => request.url === TRACKER_API.visits).request.params;
    expect(Object.fromEntries(params.keys().map((key) => [key, params.get(key)]))).toEqual({
      day: '2026-10-21',
      stage: 'InLab',
      attention: 'Panic',
      view: 'Verification',
      search: 'mona',
      page: '1',
      pageSize: '20',
      sort: 'timeLeft',
      direction: 'desc',
    });
  });

  it('LIS-CR-014 T3: asks for service rows, shows them, and drops the service order when going back to visits', async () => {
    await run();
    visits().forEach((call) => call.flush(trackerPage([trackerItem()])));
    await run();

    facade.setPage({ page: 2, pageSize: 50 });
    facade.setRows('services');
    facade.setSort({ key: 'service', direction: 'desc' });
    await run();
    const call = backend.expectOne((request) => request.url === TRACKER_API.visits);
    expect([call.request.params.get('rows'), call.request.params.get('sort'), call.request.params.get('page')]).toEqual(['Services', 'service', '1']);
    call.flush(trackerPage([], { rows: 'Services', services: [trackerServiceItem()] }));
    await run();
    expect(facade.rows().map((row) => (row as { lineId?: string }).lineId)).toEqual(['line-1']);
    expect(facade.urlState().filter.rows).toBe('services');

    facade.setRows('visits');
    await run();
    const back = backend.expectOne((request) => request.url === TRACKER_API.visits).request.params;
    expect([back.get('rows'), back.get('sort')]).toEqual([null, null]);
    expect(facade.sort()).toBeNull();
  });

  it('sends a from / to range in place of the day, and a day in place of the range', async () => {
    await run();
    visits().forEach((call) => call.flush(trackerPage([])));

    facade.setDay('2026-10-21');
    facade.setRange('2026-10-15T08:00', '2026-10-21T17:30');
    await run();
    let params = backend.expectOne((request) => request.url === TRACKER_API.visits).request.params;
    expect([params.has('day'), params.get('from'), params.get('to')]).toEqual([false, '2026-10-15T08:00', '2026-10-21T17:30']);

    facade.setDay('2026-10-20');
    await run();
    params = backend.expectOne((request) => request.url === TRACKER_API.visits).request.params;
    expect([params.get('day'), params.has('from'), params.has('to')]).toEqual(['2026-10-20', false, false]);
  });

  it('takes the laboratory\'s today from a default-day answer only, never from a chosen day or range', async () => {
    await run();
    visits().forEach((call) => call.flush(trackerPage([], { day: '2026-10-22' })));
    await run();
    expect(facade.servedDay()).toBe('2026-10-22');

    facade.setRange('2026-10-15T08:00', '2026-10-21T17:30');
    await run();
    backend.expectOne((request) => request.url === TRACKER_API.visits).flush(trackerPage([], { day: '2026-10-15' }));
    await run();
    facade.setDay('2026-10-01');
    await run();
    backend.expectOne((request) => request.url === TRACKER_API.visits).flush(trackerPage([], { day: '2026-10-01' }));
    await run();
    expect(facade.servedDay()).toBe('2026-10-22');
  });

  it('maps the sortable columns to the server keys and toggles a stage or an attention flag off on a second press', async () => {
    await run();
    visits().forEach((call) => call.flush(trackerPage([])));

    facade.setSort({ key: 'registration', direction: 'asc' });
    facade.toggleStage('Ready');
    facade.toggleStage('Ready');
    facade.toggleAttention('Delayed');
    facade.toggleAttention('Delayed');
    await run();
    const params = backend.expectOne((request) => request.url === TRACKER_API.visits).request.params;
    expect([params.get('sort'), params.has('stage'), params.has('attention')]).toEqual(['registeredAt', false, false]);
  });

  it('reads one request per lane (stage, page size 20) while the board is shown, and none for the table', async () => {
    await run();
    visits().forEach((call) => call.flush(trackerPage([])));

    facade.setLayout('board');
    await run();
    const lanes = visits();
    expect(lanes.map((call) => call.request.params.get('stage'))).toEqual([...TRACKER_LANES]);
    expect(lanes.every((call) => call.request.params.get('pageSize') === String(LANE_PAGE_SIZE) && call.request.params.get('page') === '1')).toBe(true);
    lanes.forEach((call) => call.flush(trackerPage([trackerItem({ stage: call.request.params.get('stage') === 'Ready' ? 'Ready' : 'InLab' })], { totalCount: 25, counters: trackerCounters({ ready: 7 }) })));
    await run();
    expect(facade.counters().ready).toBe(7);
    expect(facade.lanes.every((lane) => lane.page.hasValue())).toBe(true);

    facade.toggleStage('Ready');
    await run();
    expect(visits().map((call) => call.request.params.get('stage'))).toEqual(['Ready']);
    expect(facade.visibleLanes().map((lane) => lane.stage)).toEqual(['Ready']);
  });

  it('"+ n more" opens the table filtered by that stage', async () => {
    facade.setLayout('board');
    await run();
    visits().forEach((call) => call.flush(trackerPage([])));

    facade.showStage('AwaitingSample');
    await run();
    expect(facade.layout()).toBe('table');
    const request = backend.expectOne((call) => call.url === TRACKER_API.visits).request;
    expect([request.params.get('stage'), request.params.get('pageSize')]).toEqual(['AwaitingSample', '50']);
  });

  it('re-reads every 30 s while the document is visible, pauses while hidden and re-reads on return', async () => {
    facade.connect(TestBed.inject(DestroyRef));
    await run();
    backend.expectOne((request) => request.url === TRACKER_API.visits).flush(trackerPage([]));
    await run();
    expect(facade.live()).toBe(true);

    timers.advance(29_000);
    await run();
    expect(visits().length).toBe(0);

    timers.advance(1_000);
    await run();
    backend.expectOne((request) => request.url === TRACKER_API.visits).flush(trackerPage([]));
    await run();

    setVisibility('hidden');
    expect(facade.live()).toBe(false);
    expect(timers.running).toBe(0);
    timers.advance(120_000);
    await run();
    expect(visits().length).toBe(0);

    setVisibility('visible');
    await run();
    expect(facade.live()).toBe(true);
    expect(timers.running).toBe(1);
    backend.expectOne((request) => request.url === TRACKER_API.visits).flush(trackerPage([]));
  });

  it('counts the seconds since the last answer for the live label', async () => {
    facade.connect(TestBed.inject(DestroyRef));
    expect(facade.secondsSinceUpdate()).toBeNull();
    await run();
    backend.expectOne((request) => request.url === TRACKER_API.visits).flush(trackerPage([]));
    await run();
    expect(facade.secondsSinceUpdate()).toBe(0);

    timers.advance(12_000);
    expect(facade.secondsSinceUpdate()).toBe(12);
  });

  it('stops its timers when the page goes away', async () => {
    facade.connect(TestBed.inject(DestroyRef));
    await run();
    expect(timers.running).toBe(1);
    TestBed.resetTestingModule();
    expect(timers.running).toBe(0);
  });
});
