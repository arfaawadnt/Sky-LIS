import { EMPTY_TRACKER_COUNTERS, type LabTrackerCounters, type LabTrackerItem, type LabTrackerPage } from '../../../core/api/tracker.contracts';
import type { TrackerTimers } from '../state/tracker.facade';

/** Runs the resource effects and lets the promises settle; `whenStable()` would wait for the very requests a test flushes. */
export const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

export function trackerItem(overrides: Partial<LabTrackerItem> = {}): LabTrackerItem {
  return {
    visitId: 'visit-1',
    accession: 'ACC-2026-00001',
    patientId: 'pat-1',
    patientNumber: 'P-0001',
    patientName: 'Mona Adel',
    patientNameAr: 'منى عادل',
    patientMasked: false,
    gender: 'Female',
    ageText: '34y',
    urgent: false,
    registeredAtUtc: '2026-10-22T07:30:00Z',
    registeredByName: 'Reception One',
    payerName: 'Cash',
    payerNameAr: 'نقدي',
    tierName: null,
    tierNameAr: null,
    branchId: 'branch-1',
    stage: 'InLab',
    nextStep: 'EnterResults',
    minutesLeft: 80,
    delayState: 'OnTime',
    registration: { lines: 3, cancelledLines: 0 },
    samples: { total: 2, ordered: 0, collected: 0, received: 2, rejected: 0, firstRejectionReason: null, firstRejectionReasonAr: null },
    results: { total: 3, ordered: 2, complete: 1, reviewed: 0, verified: 0, printed: 0, hidden: 0, panic: 0, delta: 0, firstPanicTest: null, firstPanicTestAr: null },
    payment: { status: 'Paid', patientShare: 150, paid: 150, remaining: 0 },
    ...overrides,
  };
}

export function trackerCounters(overrides: Partial<LabTrackerCounters> = {}): LabTrackerCounters {
  return { ...EMPTY_TRACKER_COUNTERS, total: 12, awaitingSample: 2, inLab: 4, awaitingVerification: 3, ready: 2, delivered: 1, delayed: 1, panic: 1, ...overrides };
}

export function trackerPage(items: readonly LabTrackerItem[], overrides: Partial<LabTrackerPage> = {}): LabTrackerPage {
  return { items, page: 1, pageSize: 50, totalCount: items.length, counters: trackerCounters(), day: '2026-10-22', branchId: 'branch-1', fromUtc: '2026-10-21T21:00:00+00:00', toUtc: '2026-10-22T21:00:00+00:00', ...overrides };
}

/** Timers a spec drives by hand: `advance(ms)` moves the clock a second at a time and runs the periodic tasks. */
export class ManualTimers implements TrackerTimers {
  time = 1_800_000_000_000;
  private readonly tasks = new Set<() => void>();

  every(_ms: number, task: () => void): () => void {
    this.tasks.add(task);
    return () => {
      this.tasks.delete(task);
    };
  }

  now(): number {
    return this.time;
  }

  get running(): number {
    return this.tasks.size;
  }

  advance(ms: number): void {
    for (let elapsed = 0; elapsed < ms; elapsed += 1000) {
      this.time += 1000;
      for (const task of [...this.tasks]) {
        task();
      }
    }
  }
}

/** Sets `document.visibilityState` and tells the page, as a browser does when the tab is hidden or shown again. */
export function setVisibility(state: DocumentVisibilityState): void {
  Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => state });
  document.dispatchEvent(new Event('visibilitychange'));
}

export function resetVisibility(): void {
  Reflect.deleteProperty(document, 'visibilityState');
}
