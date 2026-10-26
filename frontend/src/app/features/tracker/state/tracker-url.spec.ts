import { convertToParamMap } from '@angular/router';

import { sameQuery, trackerQueryOf, trackerUrlQuery, trackerUrlState } from './tracker-url';
import { type TrackerUrlState, defaultTrackerFilter } from './tracker.facade';

describe('tracker URL state (LIS-CR-014 T5)', () => {
  const untouched: TrackerUrlState = { filter: defaultTrackerFilter(), sort: null, layout: 'table' };

  it('writes only what differs from the defaults', () => {
    expect(trackerUrlQuery(untouched)).toEqual({});
    const state: TrackerUrlState = {
      filter: { ...defaultTrackerFilter(), day: '2026-10-20', stage: 'Ready', attention: 'Panic', view: 'FrontDesk', search: 'mona', rows: 'services', page: 3, pageSize: 20 },
      sort: { key: 'service', direction: 'desc' },
      layout: 'board',
    };
    expect(trackerUrlQuery(state)).toEqual({ day: '2026-10-20', stage: 'Ready', attention: 'Panic', view: 'FrontDesk', search: 'mona', rows: 'services', page: '3', size: '20', sort: 'service', dir: 'desc', layout: 'board' });
    // a range replaces the day
    expect(trackerUrlQuery({ ...untouched, filter: { ...defaultTrackerFilter(), from: '2026-10-15T08:00', to: '2026-10-21T17:30' } })).toEqual({ from: '2026-10-15T08:00', to: '2026-10-21T17:30' });
  });

  it('reads back what it wrote (round trip)', () => {
    const state: TrackerUrlState = {
      filter: { ...defaultTrackerFilter(), from: '2026-10-15T08:00', to: '2026-10-21T17:30', stage: 'InLab', search: 'ACC-1', page: 2 },
      sort: { key: 'timeLeft', direction: 'asc' },
      layout: 'table',
    };
    expect(trackerUrlState(convertToParamMap(trackerUrlQuery(state)))).toEqual(state);
  });

  it('is null for a URL without tracker keys, and drops malformed values', () => {
    expect(trackerUrlState(convertToParamMap({}))).toBeNull();
    expect(trackerUrlState(convertToParamMap({ returnTo: '/x' }))).toBeNull();

    const read = trackerUrlState(convertToParamMap({ day: 'yesterday', stage: 'Lost', attention: 'Bored', view: 'Lab', search: 'x'.repeat(61), rows: 'lines', page: '0', size: '500', sort: 'password', dir: 'sideways', layout: 'cards' }));
    expect(read).toEqual(untouched);

    // a range needs both ends, in order; the service order needs service rows
    expect(trackerUrlState(convertToParamMap({ from: '2026-10-21T10:00', to: '2026-10-21T08:00' }))?.filter.from).toBeNull();
    expect(trackerUrlState(convertToParamMap({ sort: 'service' }))?.sort).toBeNull();
    expect(trackerUrlState(convertToParamMap({ sort: 'toString' }))?.sort).toBeNull();
    expect(trackerUrlState(convertToParamMap({ sort: 'service', rows: 'services' }))?.sort).toEqual({ key: 'service', direction: 'asc' });
  });

  it('compares queries by keys and values and keeps only its own keys', () => {
    expect(sameQuery({ a: '1', b: '2' }, { b: '2', a: '1' })).toBe(true);
    expect(sameQuery({ a: '1' }, { a: '1', b: '2' })).toBe(false);
    expect(trackerQueryOf(convertToParamMap({ stage: 'Ready', other: 'x' }))).toEqual({ stage: 'Ready' });
  });
});
